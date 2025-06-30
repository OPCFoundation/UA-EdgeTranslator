namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using IEC61850.Client;
    using IEC61850.Common;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Serialization.Formatters.Binary;
    using System.Text;
    using System.Threading.Tasks;

    internal class IEC61850Client : IAsset
    {
        private IedConnection _client = new();

        private string _endpoint = string.Empty;

        public ThingDescription BrowseAndGenerateTD(string name, string endpoint)
        {
            var address = endpoint.Split([':', '/']);

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

        public void Connect(string ipAddress, int port)
        {
            _client.Connect(ipAddress, port);
            _endpoint = ipAddress + ":" + port;
        }

        public void Disconnect()
        {
            _client.Abort();
        }

        public List<string> Discover()
        {
            // IEC61850 does not support discovery
            return new List<string>();
        }

        public string GetRemoteEndpoint()
        {
            return _endpoint;
        }

        public object Read(AssetTag tag)
        {
            object value = null;

            string[] addressParts = tag.Address.Split(['?', '&', '=']);

            if (addressParts.Length == 2)
            {
                byte[] tagBytes = Read(addressParts[0], 0, null, ushort.Parse(addressParts[1])).GetAwaiter().GetResult();

                if ((tagBytes != null) && (tagBytes.Length > 0))
                {
                    if (tag.Type == "Float")
                    {
                        value = BitConverter.ToSingle(tagBytes);
                    }
                    else if (tag.Type == "Boolean")
                    {
                        value = BitConverter.ToBoolean(tagBytes);
                    }
                    else if (tag.Type == "Integer")
                    {
                        value = BitConverter.ToInt32(tagBytes);
                    }
                    else if (tag.Type == "String")
                    {
                        value = Encoding.UTF8.GetString(tagBytes);
                    }
                    else
                    {
                        throw new ArgumentException("Type not supported by IEC61850.");
                    }
                }
            }

            return value;
        }

        public void Write(AssetTag tag, string value)
        {
            string[] addressParts = tag.Address.Split(['?', '&', '=']);
            byte[] tagBytes = null;

            if (tag.Type == "Float")
            {
                tagBytes = BitConverter.GetBytes(float.Parse(value));
            }
            else if (tag.Type == "Boolean")
            {
                tagBytes = BitConverter.GetBytes(bool.Parse(value));
            }
            else if (tag.Type == "Integer")
            {
                tagBytes = BitConverter.GetBytes(int.Parse(value));
            }
            else if (tag.Type == "String")
            {
                tagBytes = Encoding.UTF8.GetBytes(value);
            }
            else
            {
                throw new ArgumentException("Type not supported by IEC61850.");
            }

            Write(addressParts[0], 0, string.Empty, tagBytes, false).GetAwaiter().GetResult();
        }

        private Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count)
        {
            var value = _client.ReadFloatValue(addressWithinAsset, FunctionalConstraint.SP);

#pragma warning disable SYSLIB0011
            BinaryFormatter bf = new();
            using (MemoryStream ms = new())
            {
                bf.Serialize(ms, value);
#pragma warning restore SYSLIB0011

                return Task.FromResult(ms.ToArray());
            }
        }

        private Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly)
        {
            using (MemoryStream memStream = new(values))
            {
#pragma warning disable SYSLIB0011
                BinaryFormatter binForm = new();

                var value = (float)binForm.Deserialize(memStream);
#pragma warning restore SYSLIB0011

                _client.WriteValue(addressWithinAsset, FunctionalConstraint.SP, new MmsValue(values));
            }

            return Task.CompletedTask;
        }

        public string ExecuteAction(string address, string actionName, string[] inputArgs, string[] outputArgs)
        {
            throw new NotImplementedException();
        }
    }
}
