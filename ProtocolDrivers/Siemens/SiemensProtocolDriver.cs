namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using Sharp7;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Globalization;

    public class SiemensProtocolDriver : IProtocolDriver
    {
        public string Scheme => S7Endpoint.Scheme;

        public string WoTBindingUri => S7Form.BindingNamespace;

        public IEnumerable<string> Discover()
        {
            // S7Comm does not support discovery
            return new List<string>();
        }

        /// <summary>
        /// Browses the connected PLC and emits placeholder Properties following the
        /// W3C WoT S7Comm binding:
        ///   - one WORD sample at byte 0 of every DB found via ListBlocksOfType;
        ///   - one BYTE sample at byte 0 of the M, I and Q areas;
        ///   - one WORD sample of counter 0 and timer 0 on the families that have them.
        ///
        /// S7Comm classic does not expose symbol information, so this "blind"
        /// browse cannot enumerate the individual variables inside DBs. Each
        /// emitted Property is a sample placeholder that consumers should
        /// duplicate and re-point at the real address and type of the variable
        /// they want. For full symbol-aware import (one Property per leaf member
        /// of every standard-access DB, with the correct byte+bit offset, type
        /// and length) use UA-WoTGenerator with TIA Openness against the
        /// engineering project (.ap2x).
        /// </summary>
        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            S7Endpoint endpoint = ParseEndpoint(assetEndpoint);

            ThingDescription td = new()
            {
                Context = ["https://www.w3.org/2022/wot/td/v1.1", new Dictionary<string, string> { [S7Form.Prefix] = S7Form.BindingNamespace }],
                Id = ThingDescriptionId.FromAssetName(assetName),
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = ["nosec_sc"],
                Type = ["Thing"],
                Name = assetName,

                // The binding wants rack and slot in base, which must end in / so the
                // relative addresses resolve below it.
                Base = endpoint.ToBaseUri(),
                Title = assetName,
                Description =
                    "DISCLAIMER: This Thing Description was produced by the SiemensProtocolDriver " +
                    "blind S7Comm browse. S7Comm classic does not expose symbol information, so the " +
                    "individual variables inside data blocks cannot be enumerated at runtime. Each " +
                    "Property below is a sample placeholder (one WORD at byte 0 of every DB; one " +
                    "BYTE at byte 0 of M / I / Q; one WORD of counter and timer 0) that you must " +
                    "duplicate and re-point at the real address, type and length of the variable you " +
                    "want. For full symbol-aware coverage (one Property per leaf member of every " +
                    "standard-access DB, with the correct byte+bit offset, type and length) run " +
                    "UA-WoTGenerator against the TIA Portal project (.ap2x) via the bundled " +
                    "TIA Openness importer.",
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            SiemensAsset asset = new();
            try
            {
                asset.Connect(endpoint, new S7ConnectionTerms());
                if (!asset.IsConnected || asset.S7 == null)
                {
                    Log.Logger.Warning("Could not browse S7 endpoint {ep} — connection failed.", assetEndpoint);
                    return td;
                }

                Log.Logger.Information(
                    "S7 browse on {ep} produces sample placeholders only. For full symbol coverage " +
                    "run UA-WoTGenerator against the TIA project (.ap2x).", assetEndpoint);

                // The family is detected from the CPU order code, as the binding allows.
                S7Controller? controller = DetectController(asset.S7);
                if (controller.HasValue)
                {
                    td.AdditionalData = new Dictionary<string, object> { [S7Controllers.ControllerTerm] = controller.Value.ToString() };
                }

                EnumerateDataBlocks(asset.S7, td.Properties);
                AddSampleTag(td.Properties, "M0", new S7Address { Area = S7MemoryArea.M, ByteOffset = 0, Type = S7DataType.BYTE });
                AddSampleTag(td.Properties, "I0", new S7Address { Area = S7MemoryArea.I, ByteOffset = 0, Type = S7DataType.BYTE });
                AddSampleTag(td.Properties, "Q0", new S7Address { Area = S7MemoryArea.Q, ByteOffset = 0, Type = S7DataType.BYTE });

                // Counters and timers exist on the S7-300 and S7-400 families only.
                if (controller != S7Controller.S7_1200 && controller != S7Controller.S7_1500)
                {
                    AddSampleTag(td.Properties, "C0", new S7Address { Area = S7MemoryArea.C, ByteOffset = 0, Type = S7DataType.WORD });
                    AddSampleTag(td.Properties, "T0", new S7Address { Area = S7MemoryArea.T, ByteOffset = 0, Type = S7DataType.WORD });
                }
            }
            finally
            {
                AsyncBridge.RunSync(() => asset.DisconnectAsync());
            }

            return td;
        }

        public Task<AssetConnection> CreateAndConnectAssetAsync(ThingDescription td, CancellationToken cancellationToken = default)
        {
            byte unitId = 1; // S7 does not use unit IDs

            S7Endpoint endpoint = ParseEndpoint(td.Base);
            S7ConnectionTerms terms = S7ConnectionTerms.FromThingDescription(td);

            SiemensAsset asset = new();

            // The S7 client library is synchronous.
            asset.Connect(endpoint, terms);

            return Task.FromResult(new AssetConnection(asset, unitId));
        }

        public AssetTag CreateTag(
            ThingDescription td,
            object form,
            string assetId,
            byte unitId,
            string variableId,
            string mappedUAExpandedNodeId,
            string mappedUAFieldPath)
        {
            // The asset re-deserializes the form on every read/write so we
            // store it verbatim in AssetTag.Address.
            string formJson = form is string s ? s : JsonConvert.SerializeObject(form);

            S7Form s7Form = JsonConvert.DeserializeObject<S7Form>(formJson);

            // Every normative rule of the binding, for the declared controller family.
            S7Controller? controller = S7ConnectionTerms.FromThingDescription(td).Controller;
            string problem = s7Form.Validate(controller) ?? s7Form.TryResolveAddress(out _);
            if (problem != null)
            {
                throw new ArgumentException($"Form {s7Form.Href}: {problem}");
            }

            s7Form.TryResolveAddress(out S7Address address);

            return new AssetTag()
            {
                Name = variableId,
                Address = formJson,
                UnitID = unitId,
                Type = address.Type?.ToString(),
                PollingInterval = (int)s7Form.EffectivePollingTime,
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }

        // ---- helpers ------------------------------------------------------

        /// <summary>
        /// Parses the binding's endpoint, <c>s7://host[:port][/rack,slot][/]</c>, with
        /// port 102 and rack 0, slot 1 when omitted.
        /// </summary>
        public static S7Endpoint ParseEndpoint(string endpoint)
        {
            if (!S7Endpoint.TryParse(endpoint, out S7Endpoint parsed))
            {
                if (Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out Uri uri) && uri.HostNameType == UriHostNameType.IPv6)
                {
                    throw new ArgumentException(
                        $"S7 controllers do not support IPv6; use an IPv4 address or a host name, not '{endpoint}'.");
                }

                throw new ArgumentException(
                    $"Expected S7 PLC address in the format s7://host[:port][/rack,slot]/, not '{endpoint}'.");
            }

            return parsed;
        }

        /// <summary>The controller family from the CPU order code (6ES7 5.. = S7-1500, 6ES7 21. = S7-1200, ...).</summary>
        internal static S7Controller? DetectController(S7Client s7)
        {
            try
            {
                S7Client.S7OrderCode orderCode = new();
                if (s7.GetOrderCode(ref orderCode) != 0)
                {
                    return null;
                }

                return S7Controllers.FromOrderNumber(orderCode.Code);
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "S7 order code could not be read.");
                return null;
            }
        }

        private static void EnumerateDataBlocks(S7Client s7, Dictionary<string, Property> properties)
        {
            ushort[] list = new ushort[1024];
            int count = list.Length;
            int err = s7.ListBlocksOfType(S7Client.Block_DB, list, ref count);
            if (err != 0)
            {
                Log.Logger.Warning("S7 ListBlocksOfType(DB) failed: {err}", s7.ErrorText(err));
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int dbNumber = list[i];

                // Emit a single WORD sample at byte 0 of the DB, shaped like a
                // generator-produced tag so the asset treats it identically.
                string propertyName = "DB" + dbNumber.ToString(CultureInfo.InvariantCulture);
                AddSampleTag(properties, propertyName, new S7Address { Area = S7MemoryArea.DB, DbNumber = dbNumber, ByteOffset = 0, Type = S7DataType.WORD });
            }
        }

        private static void AddSampleTag(Dictionary<string, Property> properties, string name, S7Address address)
        {
            properties.TryAdd(name, CreateProperty(address, readOnly: false));
        }

        /// <summary>A Property with one binding form for the address and the data schema the binding recommends.</summary>
        public static Property CreateProperty(S7Address address, bool readOnly)
        {
            S7Form form = new()
            {
                Href = S7Address.ToHref(address.ToString()),
                Op = readOnly ? [Op.Readproperty, Op.Observeproperty] : [Op.Readproperty, Op.Observeproperty, Op.Writeproperty],
                Type = address.Type,
                PollingTime = 1000
            };

            S7DataTypes.Bounds(address.Type.Value, out long? minimum, out long? maximum);
            return new Property()
            {
                Type = S7DataTypes.WotTypeOf(address.Type.Value),
                Minimum = minimum,
                Maximum = maximum,
                ReadOnly = readOnly,
                Observable = true,
                Forms = [form]
            };
        }
    }
}
