namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using Sharp7;
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    public class SiemensProtocolDriver : IProtocolDriver
    {
        public string Scheme => "s7";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/s7";

        public IEnumerable<string> Discover()
        {
            // S7Comm does not support discovery
            return new List<string>();
        }

        /// <summary>
        /// Browses the connected PLC and emits placeholder Properties shaped
        /// exactly like generator-produced individual tags so the asset's
        /// Read/Write path treats them identically:
        ///   - one Word (Short) sample at offset 0 of every DB found via
        ///     ListBlocksOfType, carrying the correct DB number;
        ///   - one Byte sample at offset 0 of the merker / process input /
        ///     process output areas;
        ///   - one Word (Short) sample at offset 0 of the counter / timer
        ///     areas (16-bit natural width).
        ///
        /// S7Comm classic does not expose symbol information, so this "blind"
        /// browse cannot enumerate the individual variables inside DBs. Each
        /// emitted Property is a sample placeholder that consumers should
        /// duplicate and re-point at the real offset/type/length of the
        /// variable they want. For full symbol-aware import (one Property per
        /// leaf member of every standard-access DB, with the correct byte+bit
        /// offset, type and length) use WoTThingModelGenerator with TIA
        /// Openness against the engineering project (.ap2x).
        /// </summary>
        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            ThingDescription td = new()
            {
                Context = ["https://www.w3.org/2022/wot/td/v1.1"],
                Id = "urn:" + assetName,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = ["nosec_sc"],
                Type = ["Thing"],
                Name = assetName,
                Base = assetEndpoint,
                Title = assetName,
                Description =
                    "DISCLAIMER: This Thing Description was produced by the SiemensProtocolDriver " +
                    "blind S7Comm browse. S7Comm classic does not expose symbol information, so the " +
                    "individual variables inside data blocks cannot be enumerated at runtime. Each " +
                    "Property below is a sample placeholder (one Word at offset 0 of every DB; one " +
                    "Byte at offset 0 of M / PE / PA; one Word at offset 0 of C / T) that you must " +
                    "duplicate and re-point at the real offset, type and length of the variable you " +
                    "want. For full symbol-aware coverage (one Property per leaf member of every " +
                    "standard-access DB, with the correct byte+bit offset, type and length) run " +
                    "WoTThingModelGenerator against the TIA Portal project (.ap2x) via the bundled " +
                    "TIA Openness importer.",
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            (string ip, int rack, int slot) = ParseEndpoint(assetEndpoint);

            SiemensAsset asset = new();
            try
            {
                asset.Connect(ip, rack, slot);
                if (!asset.IsConnected || asset.S7 == null)
                {
                    Log.Logger.Warning("Could not browse S7 endpoint {ep} — connection failed.", assetEndpoint);
                    return td;
                }

                Log.Logger.Information(
                    "S7 browse on {ep} produces sample placeholders only. For full symbol coverage " +
                    "run WoTThingModelGenerator against the TIA project (.ap2x).", assetEndpoint);

                EnumerateDataBlocks(asset.S7, td.Properties);
                AddSampleTag(td.Properties, "M0",  S7Target.Merker,          TypeString.Byte,  TypeEnum.Integer, sizeBytes: 1);
                AddSampleTag(td.Properties, "PE0", S7Target.IPIProcessInput, TypeString.Byte,  TypeEnum.Integer, sizeBytes: 1);
                AddSampleTag(td.Properties, "PA0", S7Target.IPUProcessInput, TypeString.Byte,  TypeEnum.Integer, sizeBytes: 1);
                AddSampleTag(td.Properties, "C0",  S7Target.Counter,         TypeString.Short, TypeEnum.Integer, sizeBytes: 2);
                AddSampleTag(td.Properties, "T0",  S7Target.Timer,           TypeString.Short, TypeEnum.Integer, sizeBytes: 2);
            }
            finally
            {
                asset.Disconnect();
            }

            return td;
        }

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
        {
            unitId = 1; // S7 does not use unit IDs

            (string ip, int rack, int slot) = ParseEndpoint(td.Base);

            SiemensAsset asset = new();
            asset.Connect(ip, rack, slot);

            return asset;
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

            return new AssetTag()
            {
                Name = variableId,
                Address = formJson,
                UnitID = unitId,
                Type = s7Form?.Type.ToString(),
                PollingInterval = (int)(s7Form?.PollingTime ?? 1000),
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }

        // ---- helpers ------------------------------------------------------

        /// <summary>
        /// Accepted formats:
        ///   s7://ip            -> rack 0, slot 1 (S7‑1500 default)
        ///   s7://ip:slot       -> rack 0, given slot (legacy)
        ///   s7://ip:rack:slot  -> explicit
        /// </summary>
        internal static (string ip, int rack, int slot) ParseEndpoint(string endpoint)
        {
            if (string.IsNullOrEmpty(endpoint))
            {
                throw new ArgumentException("S7 endpoint is empty.");
            }

            string[] parts = endpoint.Split([':', '/'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !string.Equals(parts[0], "s7", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Expected S7 PLC address in the format s7://ipaddress[:rack:slot]!");
            }

            string ip = parts.Length >= 2 ? parts[1] : throw new ArgumentException("S7 endpoint missing host.");
            int rack = 0;
            int slot = 1;

            if (parts.Length == 3)
            {
                _ = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot);
            }
            else if (parts.Length >= 4)
            {
                _ = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out rack);
                _ = int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot);
            }

            return (ip, rack, slot);
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

                // Emit a single Word (Short) sample at offset 0 of the DB.
                // The shape mirrors a generator-produced tag (one variable
                // with explicit DB number, byte offset, type and size) so the
                // asset's Read/Write path treats it identically.
                string propertyName = "DB" + dbNumber.ToString(CultureInfo.InvariantCulture);

                S7Form form = new()
                {
                    Href = propertyName + "?0",
                    Op = [Op.Readproperty, Op.Observeproperty, Op.Writeproperty],
                    PollingTime = 1000,
                    S7Target = S7Target.DataBlock,
                    S7DBNumber = dbNumber,
                    S7Start = 0,
                    S7Pos = 0,
                    S7Size = 2,
                    Type = TypeString.Short,
                };

                Property property = new()
                {
                    Type = TypeEnum.Integer,
                    ReadOnly = false,
                    Observable = true,
                    Forms = [form]
                };

                properties.TryAdd(propertyName, property);
            }
        }

        private static void AddSampleTag(
            Dictionary<string, Property> properties,
            string name,
            S7Target target,
            TypeString tdType,
            TypeEnum typeEnum,
            int sizeBytes)
        {
            S7Form form = new()
            {
                Href = name,
                Op = [Op.Readproperty, Op.Observeproperty, Op.Writeproperty],
                PollingTime = 1000,
                S7Target = target,
                S7DBNumber = 0,
                S7Start = 0,
                S7Pos = 0,
                S7Size = sizeBytes,
                Type = tdType,
            };

            Property property = new()
            {
                Type = typeEnum,
                ReadOnly = false,
                Observable = true,
                Forms = [form]
            };

            properties.TryAdd(name, property);
        }
    }
}
