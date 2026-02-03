namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using IEC61850.Client;
    using IEC61850.Common;
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;

    public class IEC61850ProtocolDriver: IProtocolDriver
    {
        public string Scheme => "iec61850";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/iec61850";

        private IedConnection _client = new();

        public IEnumerable<string> Discover()
        {
            // IEC61850 does not support discovery
            return new List<string>();
        }

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            var address = assetEndpoint.Split([':', '/']);

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

            try
            {
                _client.Connect(address[1], 102);

                Log.Logger.Information("Connected to IEC61850 at " + address[1]);

                var mmsCon = _client.GetMmsConnection();

                var identity = mmsCon.GetServerIdentity();

                Log.Logger.Information("Vendor:   " + identity.vendorName);
                Log.Logger.Information("Model:    " + identity.modelName);
                Log.Logger.Information("Revision: " + identity.revision);

                var serverDirectory = _client.GetServerDirectory(false);

                foreach (var ldName in serverDirectory)
                {
                    Log.Logger.Information("LD: " + ldName);

                    var lnNames = _client.GetLogicalDeviceDirectory(ldName);

                    foreach (var lnName in lnNames)
                    {
                        Log.Logger.Information("  LN: " + lnName);

                        var logicalNodeReference = ldName + "/" + lnName;

                        // discover data objects
                        var dataObjects = _client.GetLogicalNodeDirectory(logicalNodeReference, ACSIClass.ACSI_CLASS_DATA_OBJECT);

                        foreach (var dataObject in dataObjects)
                        {
                            Log.Logger.Information("    DO: " + dataObject);

                            var dataDirectory = _client.GetDataDirectoryFC(logicalNodeReference + "." + dataObject);

                            foreach (var dataDirectoryElement in dataDirectory)
                            {
                                var daReference = logicalNodeReference + "." + dataObject + "." + ObjectReference.getElementName(dataDirectoryElement);

                                // get the type specification of a variable
                                var specification = _client.GetVariableSpecification(daReference, ObjectReference.getFC(dataDirectoryElement));

                                Log.Logger.Information("      DA/SDO: [" + ObjectReference.getFC(dataDirectoryElement) + "] " +
                                                   ObjectReference.getElementName(dataDirectoryElement) + " : " + specification.GetType()
                                                   + "(" + specification.Size() + ")");

                                if (specification.GetType() == MmsType.MMS_STRUCTURE)
                                {
                                    foreach (MmsVariableSpecification elementSpec in specification)
                                    {
                                        Log.Logger.Information("           " + elementSpec.GetName() + " : " + elementSpec.GetType());
                                    }
                                }
                            }
                        }

                        // discover data sets
                        var dataSets = _client.GetLogicalNodeDirectory(logicalNodeReference, ACSIClass.ACSI_CLASS_DATA_SET);
                        foreach (var dataSet in dataSets)
                        {
                            Log.Logger.Information("    Dataset: " + dataSet);
                        }

                        // discover unbuffered report control blocks
                        var urcbs = _client.GetLogicalNodeDirectory(logicalNodeReference, ACSIClass.ACSI_CLASS_URCB);

                        foreach (var urcb in urcbs)
                        {
                            Log.Logger.Information("    URCB: " + urcb);
                        }

                        // discover buffered report control blocks
                        var brcbs = _client.GetLogicalNodeDirectory(logicalNodeReference, ACSIClass.ACSI_CLASS_BRCB);

                        foreach (var brcb in brcbs)
                        {
                            Log.Logger.Information("    BRCB: " + brcb);
                        }
                    }
                }

                _client.Abort();
            }
            catch (IedConnectionException e)
            {
                Log.Logger.Information(e.Message);
            }

            return td;
        }

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
        {
            unitId = 1;

            string[] address = td.Base.Split([':', '/']);
            if ((address.Length != 6) || (address[0] != "iec61850"))
            {
                throw new Exception("Expected IEC61850 device address in the format iec61850://ipaddress:port!");
            }

            // check if we can reach the IEC61850 asset
            IEC61850Asset asset = new();
            asset.Connect(address[3] + ":" + address[4], int.Parse(address[5]));

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
            GenericForm iec61850Form = JsonConvert.DeserializeObject<GenericForm>(form.ToString());

            return new AssetTag()
            {
                Name = variableId,
                Address = iec61850Form.Href,
                UnitID = unitId,
                Type = iec61850Form.Type.ToString(),
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };

        }
    }
}
