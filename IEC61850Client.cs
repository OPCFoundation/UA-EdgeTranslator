namespace Opc.Ua.Edge.Translator
{
    using IEC61850.Client;
    using IEC61850.Common;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Threading.Tasks;

    internal class IEC61850Client : IAsset
    {
        private IedConnection _client = new();

        private string _endpoint = string.Empty;

        ThingDescription IAsset.BrowseAndGenerateTD(string name, string endpoint)
        {
            string[] address = endpoint.Split([':', '/']);

            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + name,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "Thing" },
                Name = name,
                Base = endpoint,
                Title = name,
                Properties = new Dictionary<string, Property>()
            };

            try
            {
                _client.Connect(address[1], 102);

                Log.Logger.Information("Connected to IEC61850 at " + address[1]);

                MmsConnection mmsCon = _client.GetMmsConnection();

                MmsServerIdentity identity = mmsCon.GetServerIdentity();

                Log.Logger.Information("Vendor:   " + identity.vendorName);
                Log.Logger.Information("Model:    " + identity.modelName);
                Log.Logger.Information("Revision: " + identity.revision);

                List<string> serverDirectory = _client.GetServerDirectory(false);

                foreach (string ldName in serverDirectory)
                {
                    Log.Logger.Information("LD: " + ldName);

                    List<string> lnNames = _client.GetLogicalDeviceDirectory(ldName);

                    foreach (string lnName in lnNames)
                    {
                        Log.Logger.Information("  LN: " + lnName);

                        string logicalNodeReference = ldName + "/" + lnName;

                        // discover data objects
                        List<string> dataObjects = _client.GetLogicalNodeDirectory(logicalNodeReference, ACSIClass.ACSI_CLASS_DATA_OBJECT);

                        foreach (string dataObject in dataObjects)
                        {
                            Log.Logger.Information("    DO: " + dataObject);

                            List<string> dataDirectory = _client.GetDataDirectoryFC(logicalNodeReference + "." + dataObject);

                            foreach (string dataDirectoryElement in dataDirectory)
                            {
                                string daReference = logicalNodeReference + "." + dataObject + "." + ObjectReference.getElementName(dataDirectoryElement);

                                // get the type specification of a variable
                                MmsVariableSpecification specification = _client.GetVariableSpecification(daReference, ObjectReference.getFC(dataDirectoryElement));

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
                        List<string> dataSets = _client.GetLogicalNodeDirectory(logicalNodeReference, ACSIClass.ACSI_CLASS_DATA_SET);
                        foreach (string dataSet in dataSets)
                        {
                            Log.Logger.Information("    Dataset: " + dataSet);
                        }

                        // discover unbuffered report control blocks
                        List<string> urcbs = _client.GetLogicalNodeDirectory(logicalNodeReference, ACSIClass.ACSI_CLASS_URCB);

                        foreach (string urcb in urcbs)
                        {
                            Log.Logger.Information("    URCB: " + urcb);
                        }

                        // discover buffered report control blocks
                        List<string> brcbs = _client.GetLogicalNodeDirectory(logicalNodeReference, ACSIClass.ACSI_CLASS_BRCB);

                        foreach (string brcb in brcbs)
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

        void IAsset.Connect(string ipAddress, int port)
        {
            _client.Connect(ipAddress, port);
            _endpoint = ipAddress + ":" + port;
        }

        void IAsset.Disconnect()
        {
            _client.Abort();
        }

        List<string> IAsset.Discover()
        {
            // IEC61850 does not support discovery
            return new List<string>();
        }

        string IAsset.GetRemoteEndpoint()
        {
            return _endpoint;
        }

        Task<byte[]> IAsset.Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            float value = _client.ReadFloatValue(addressWithinAsset, FunctionalConstraint.SP);

#pragma warning disable SYSLIB0011
            BinaryFormatter bf = new();
            using (MemoryStream ms = new())
            {
                bf.Serialize(ms, value);
#pragma warning restore SYSLIB0011

                return Task.FromResult(ms.ToArray());
            }
        }

        Task IAsset.Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            using (MemoryStream memStream = new(values))
            {
#pragma warning disable SYSLIB0011
                BinaryFormatter binForm = new();

                float value = (float)binForm.Deserialize(memStream);
#pragma warning restore SYSLIB0011

                _client.WriteValue(addressWithinAsset, FunctionalConstraint.SP, new MmsValue(values));
            }

            return Task.CompletedTask;
        }
    }
}
