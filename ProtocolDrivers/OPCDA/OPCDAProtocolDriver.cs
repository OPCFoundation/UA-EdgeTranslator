namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using TitaniumAS.Opc.Client.Common;
    using TitaniumAS.Opc.Client.Da;
    using TitaniumAS.Opc.Client.Da.Browsing;

    public class OPCDAProtocolDriver : IProtocolDriver
    {
        public string Scheme => "opc.da";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/opcda";

        public IEnumerable<string> Discover()
        {
            List<string> discoveredServers = new();

            try
            {
                // Discover OPC DA servers on local machine
                var serverHost = Environment.GetEnvironmentVariable("OPC_DA_SERVER_HOST") ?? "localhost";
                var serverEnumerator = new OpcServerEnumeratorAuto();
                var servers = serverEnumerator.Enumerate(serverHost, OpcServerCategory.OpcDaServer10,
                    OpcServerCategory.OpcDaServer20, OpcServerCategory.OpcDaServer30);

                foreach (var server in servers)
                {
                    string serverUri = $"opc.da://{serverHost}/{server.ProgId}";
                    discoveredServers.Add(serverUri);
                    Log.Logger.Information($"Discovered OPC DA Server: {server.ProgId}");
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error discovering OPC DA servers: {ex.Message}");
            }

            return discoveredServers;
        }

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            var td = new ThingDescription()
            {
                Context = ["https://www.w3.org/2022/wot/td/v1.1"],
                Id = "urn:" + assetName,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = ["nosec_sc"],
                Type = ["Thing"],
                Name = assetName,
                Base = assetEndpoint,
                Title = assetName,
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            try
            {
                // Parse the endpoint to extract host and ProgId
                string[] parts = assetEndpoint.Replace("opc.da://", "").Split('/');
                if (parts.Length >= 2)
                {
                    string host = parts[0];
                    string progId = string.Join("/", parts, 1, parts.Length - 1);

                    using var server = new OpcDaServer(progId, host);
                    server.Connect();

                    // Browse root items
                    var browser = new OpcDaBrowserAuto(server);
                    var items = browser.GetElements(null);

                    foreach (var item in items)
                    {
                        if (item.IsItem)
                        {
                            td.Properties.Add(item.ItemId, new Property()
                            {
                                Type = TypeEnum.String,
                                ReadOnly = false,
                                Forms = [new GenericForm()
                                {
                                    Href = item.ItemId,
                                    Op = [Op.Readproperty],
                                    Type = TypeString.String,
                                    PollingTime = 1000
                                }]
                            });
                        }
                    }

                    server.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error browsing OPC DA server: {ex.Message}");
            }

            return td;
        }

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
        {
            unitId = 1; // not used for OPC DA

            // Expected format: opc.da://hostname/ProgId
            string baseUri = td.Base.Replace("opc.da://", "");
            string[] parts = baseUri.Split('/');

            if (parts.Length < 2)
            {
                throw new Exception("Expected OPC DA server address in the format opc.da://hostname/ProgId!");
            }

            string host = parts[0];
            string progId = string.Join("/", parts, 1, parts.Length - 1);

            // Create and connect the OPC DA asset
            OPCDAAsset asset = new();
            asset.SetProgId(progId);
            asset.Connect(host, 0); // port is not used for OPC DA

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
            GenericForm opcDaForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());

            return new AssetTag()
            {
                Name = variableId,
                Address = opcDaForm.Href,
                UnitID = unitId,
                Type = opcDaForm.Type.ToString(),
                PollingInterval = (int)opcDaForm.PollingTime,
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }
    }
}
