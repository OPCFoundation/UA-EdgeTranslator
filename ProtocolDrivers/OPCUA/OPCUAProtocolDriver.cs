namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;

    public class OPCUAProtocolDriver: IProtocolDriver
    {
        public string Scheme => "opc.tcp";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/opcua";

        public IEnumerable<string> Discover()
        {
            List<string> discoveredServers = new();

            // connect to an OPC UA Global Discovery Server
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPC_UA_GDS_ENDPOINT_URL")))
            {
                var client = DiscoveryClient.CreateAsync(new Uri(Environment.GetEnvironmentVariable("OPC_UA_GDS_ENDPOINT_URL")), Program.Telemetry).GetAwaiter().GetResult();

                var servers = client.FindServersAsync(null).GetAwaiter().GetResult();
                foreach (var server in servers)
                {
                    Log.Logger.Information($"Server: {server.ApplicationName}");
                    foreach (var endpoint in server.DiscoveryUrls)
                    {
                        discoveredServers.Add(endpoint);
                    }
                }
            }

            return discoveredServers;
        }

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
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            string[] address = assetEndpoint.Split([':', '/']);
            if ((address.Length != 5) || (address[0] != "opc.tcp"))
            {
                throw new Exception("Expected OPC UA server address in the format opc.tcp://ipaddress:port!");
            }

            OPCUAAsset asset = new();
            try
            {
                asset.Connect(address[3], int.Parse(address[4]));

                List<BrowsedNode> nodes = asset.BrowseObjectsFolder();
                foreach (BrowsedNode node in nodes)
                {
                    if (string.IsNullOrEmpty(node.BrowsePath) || td.Properties.ContainsKey(node.BrowsePath))
                    {
                        continue;
                    }

                    GenericForm form = new()
                    {
                        Href = node.ExpandedNodeId,
                        Op = node.ReadOnly
                            ? [Op.Readproperty, Op.Observeproperty]
                            : [Op.Readproperty, Op.Observeproperty, Op.Writeproperty],
                        Type = node.XsdType,
                        PollingTime = 1000
                    };

                    Property property = new()
                    {
                        Type = node.WoTType,
                        ReadOnly = node.ReadOnly,
                        Observable = true,
                        OpcUaNodeId = node.ExpandedNodeId,
                        Forms = [form]
                    };

                    td.Properties.Add(node.BrowsePath, property);
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, $"Failed to browse OPC UA server {assetEndpoint}");
            }
            finally
            {
                asset.Disconnect();
            }

            return td;
        }

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
        {
            unitId = 1; // not used for OPC UA

            string[] address = td.Base.Split([':', '/']);
            if ((address.Length != 5) || (address[0] != "opc.tcp"))
            {
                throw new Exception("Expected OPC UA server address in the format opc.tcp://ipaddress:port!");
            }

            // check if we can reach the OPC UA asset
            OPCUAAsset asset = new();
            asset.Connect(address[3], int.Parse(address[4]));

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
            GenericForm opcuaForm = JsonConvert.DeserializeObject<GenericForm>(form.ToString());

            return new AssetTag()
            {
                Name = variableId,
                Address = opcuaForm.Href,
                UnitID = unitId,
                Type = opcuaForm.Type.ToString(),
                PollingInterval = (int)opcuaForm.PollingTime,
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }
    }
}
