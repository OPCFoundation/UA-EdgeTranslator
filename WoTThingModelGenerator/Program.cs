namespace Opc.Ua.Edge.Translator.Tools
{
    using Aml.Engine.CAEX;
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Export;
    using System;
    using System.IO;
    using System.Xml;
    using System.Xml.Serialization;
    using WoTThingModelGenerator.TwinCAT;
    using Property = Models.Property;

#nullable disable

    internal class Program
    {
        static void Main()
        {
            foreach (string filename in Directory.GetFiles(Directory.GetCurrentDirectory()))
            {
                if (filename.EndsWith(".nodeset2.xml"))
                {
                    ImportNodeset2Xml(filename);
                }

                if (filename.EndsWith(".aml"))
                {
                    ImportAutomationML(filename);
                }

                if (filename.EndsWith(".aas.json"))
                {
                    ImportAASAID(filename);
                }

                if (filename.EndsWith(".tmc"))
                {
                    ImportTwinCAT(filename);
                }

                if (filename.EndsWith(".csv"))
                {
                    ImportCSV(filename);
                }
            }
        }

        private static void ImportCSV(string filename)
        {
            IEnumerable<string> content = File.ReadLines(filename);

            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + Path.GetFileNameWithoutExtension(filename),
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "tm:ThingModel" },
                Name = "{{name}}",
                Base = "eip://{{address}}:{{port}}",
                Title = Path.GetFileNameWithoutExtension(filename),
                Properties = new Dictionary<string, Property>()
            };

            foreach (string line in content)
            {
                string[] tokens = line.Split(',');

                // ignore everything but tags in the form TYPE,SCOPE,NAME,DESCRIPTION,DATATYPE,SPECIFIER,ATTRIBUTES, where TYPE must be "TAG"
                if ((tokens.Length > 6) && tokens[0] == "TAG")
                {
                    string reference = tokens[2];
                    TypeString type = TypeString.Float;

                    if ((tokens[4] != "\"REAL\"") && (tokens[5] != "\"FLOAT\""))
                    {
                        // can only handle floats and reals for now
                        continue;
                    }

                    GenericForm form = new()
                    {
                        Href = reference,
                        Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                        PollingTime = 1000,
                        Type = type
                    };

                    Property property = new()
                    {
                        Type = TypeEnum.Number,
                        ReadOnly = true,
                        Observable = true,
                        Forms = new object[1] { form }
                    };

                    if (!td.Properties.ContainsKey(reference))
                    {
                        td.Properties.Add(reference, property);
                    }
                }
            }

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"), JsonConvert.SerializeObject(td, Newtonsoft.Json.Formatting.Indented));
        }

        private static void ImportTwinCAT(string filename)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(TwinCAT));
            TwinCAT twinCAT = null;
            using (XmlReader reader = XmlReader.Create(filename))
            {
                twinCAT = serializer.Deserialize(reader) as TwinCAT;
            }

            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + Path.GetFileNameWithoutExtension(filename),
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "tm:ThingModel" },
                Name = "{{name}}",
                Base = "ads://{{address}}:{{port}}",
                Title = twinCAT.Modules.Module.Name,
                Properties = new Dictionary<string, Property>()
            };

            foreach (Symbol symbol in twinCAT.Modules.Module.DataAreas.DataArea.Symbol)
            {
                string reference = symbol.Name;

                GenericForm form = new()
                {
                    Href = reference + "?" + symbol.BitSize/8,
                    Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                    PollingTime = 1000,
                    Type = TypeString.Float
                };

                Property property = new()
                {
                    Type = TypeEnum.Number,
                    ReadOnly = true,
                    Observable = true,
                    Forms = new object[1] { form }
                };

                if (!td.Properties.ContainsKey(reference))
                {
                    td.Properties.Add(reference, property);
                }
            }

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"), JsonConvert.SerializeObject(td, Newtonsoft.Json.Formatting.Indented));
        }

        private static void ImportAASAID(string filename)
        {
            AAS_AID aid = JsonConvert.DeserializeObject<AAS_AID>(File.ReadAllText(filename));

            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + Path.GetFileNameWithoutExtension(filename),
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "tm:ThingModel" },
                Name = "{{name}}",
                Base = "{{protocol}}://{{address}}:{{port}}",
                Title = Path.GetFileNameWithoutExtension(filename),
                Properties = new Dictionary<string, Property>()
            };

            Dictionary<string, string> subModelElements = new();
            if (aid?.SubmodelElements != null)
            {
                ImportSubmodelElementCollection(aid.Id, aid.SubmodelElements, subModelElements);
            }

            foreach( KeyValuePair<string, string> pair in subModelElements)
            {
                if (pair.Key.EndsWith(":title"))
                {
                    td.Title = pair.Value;
                }

                if (pair.Key.EndsWith(":base"))
                {
                    td.Base = pair.Value;
                }

                if (pair.Key.Contains(":properties:"))
                {
                    string propertyName = pair.Key.Substring(pair.Key.IndexOf(":properties:") + 12);
                    propertyName = propertyName.Substring(0, propertyName.IndexOf(":"));

                    // check if we have to create a new property
                    CreateNewProperty(td, pair.Key, propertyName);

                    if (pair.Key.EndsWith(":href"))
                    {
                        if (pair.Key.Contains(":InterfaceMODBUS_TCP:"))
                        {
                            ModbusForm form = (ModbusForm)td.Properties[propertyName].Forms[0];
                            form.Href = pair.Value;
                        }
                        else
                        {
                            GenericForm form = (GenericForm)td.Properties[propertyName].Forms[0];
                            form.Href = pair.Value;
                        }
                    }

                    if (pair.Key.EndsWith(":modv_pollingTime"))
                    {
                        if (pair.Key.Contains(":InterfaceMODBUS_TCP:"))
                        {
                            ModbusForm form = (ModbusForm)td.Properties[propertyName].Forms[0];
                            form.ModbusPollingTime = long.Parse(pair.Value);
                        }
                    }
                }
            }

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"), JsonConvert.SerializeObject(td, Newtonsoft.Json.Formatting.Indented));
        }

        private static void CreateNewProperty(ThingDescription td, string key, string propertyName)
        {
            if (!td.Properties.ContainsKey(propertyName))
            {
                object form = null;
                if (key.Contains(":InterfaceMODBUS_TCP:"))
                {
                    ModbusForm modbusForm = new()
                    {
                        Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                        ModbusEntity = ModbusEntity.HoldingRegister,
                        ModbusType = TypeString.Float
                    };
                    form = modbusForm;
                }
                else
                {
                    GenericForm genericForm = new()
                    {
                        Op = new Op[2] { Op.Readproperty, Op.Observeproperty }
                    };
                    form = genericForm;
                }

                Property property = new()
                {
                    Type = TypeEnum.Number,
                    ReadOnly = true,
                    Observable = true,
                    Forms = new object[1] { form }
                };

                td.Properties.Add(propertyName, property);
            }
        }

        private static void ImportSubmodelElementCollection(string parentName, List<SubmodelElement> smec, Dictionary<string, string> subModelElements)
        {
            foreach (SubmodelElement submodelElement in smec)
            {
                if (submodelElement.ValueType == null)
                {
                    try
                    {
                        List<SubmodelElement> nestedSMEC = JsonConvert.DeserializeObject<List<SubmodelElement>>(submodelElement.Value.ToString());
                        if (nestedSMEC.Count > 0)
                        {
                            ImportSubmodelElementCollection(parentName + ":" + submodelElement.IdShort, nestedSMEC, subModelElements);
                        }
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Ignoring SubmodelElement " + submodelElement.IdShort);
                    }
                }
                else
                {
                    subModelElements.Add(parentName + ":" + submodelElement.IdShort, submodelElement.Value.ToString());
                }
            }
        }

        private static void ImportAutomationML(string filename)
        {
            CAEXDocument doc = CAEXDocument.LoadFromString(File.ReadAllText(filename));

            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + Path.GetFileNameWithoutExtension(filename),
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "tm:ThingModel" },
                Name = "{{name}}",
                Base = "{{protocol}}://{{address}}:{{port}}",
                Title = Path.GetFileNameWithoutExtension(filename),
                Properties = new Dictionary<string, Property>()
            };

            foreach (InstanceHierarchyType instanceHirarchy in doc.CAEXFile.InstanceHierarchy)
            {
                foreach (InternalElementType internalElement in instanceHirarchy.InternalElement)
                {
                    ImportInternalElement(instanceHirarchy.Name, internalElement, td.Properties);
                }
            }

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"), JsonConvert.SerializeObject(td, Newtonsoft.Json.Formatting.Indented));
        }

        private static void ImportInternalElement(string parent, InternalElementType internalElement, Dictionary<string, Property> tdProperties)
        {
            // check if this is a leaf element
            if (internalElement.InternalElement.Count > 0)
            {
                foreach (InternalElementType childInternalElement in internalElement.InternalElement)
                {
                    ImportInternalElement(parent + "_" + internalElement.Name, childInternalElement, tdProperties);
                }
            }
            else
            {
                foreach (AttributeType attribute in internalElement.Attribute)
                {
                    ImportAttribute(parent + "_" + internalElement.Name, attribute, tdProperties);
                }
            }
        }

        private static void ImportAttribute(string parent, AttributeType attribute, Dictionary<string, Property> tdProperties)
        {
            // check if this is a leaf attribute
            if (attribute.Attribute.Count > 0)
            {
                foreach (AttributeType childAttribute in attribute.Attribute)
                {
                    ImportAttribute(parent + "_" + attribute.Name, childAttribute, tdProperties);
                }
            }
            else
            {
                // for leaf attributes, we generate WoT properties
                string reference = parent + "_" + attribute.Name;

                GenericForm form = new()
                {
                    Href = reference,
                    Op = new Op[2] { Op.Readproperty, Op.Observeproperty }
                };

                Property property = new()
                {
                    Type = TypeEnum.Number,
                    ReadOnly = true,
                    Observable = true,
                    Forms = new object[1] { form }
                };

                if (!tdProperties.ContainsKey(reference))
                {
                    tdProperties.Add(reference, property);
                }
            }
        }

        // Nodeset2 files can be edited using e.g. the SIEMENS OPC UA Modeling Editor (SiOME)
        // see https://support.industry.siemens.com/cs/document/109755133/siemens-opc-ua-modeling-editor-(siome)-for-implementing-opc-ua-companion-specification
        private static void ImportNodeset2Xml(string filename)
        {
            Stream stream = new FileStream(filename, FileMode.Open);

            ThingDescription td = new() {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + Path.GetFileNameWithoutExtension(filename),
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = new string[1] { "nosec_sc" },
                Type = new string[1] { "tm:ThingModel" },
                Name = "{{name}}",
                Base = "opc.tcp://{{address}}:{{port}}",
                Title = Path.GetFileNameWithoutExtension(filename),
                Properties = new Dictionary<string, Property>()
            };

            UANodeSet nodeSet = UANodeSet.Read(stream);
            foreach (UANode node in nodeSet.Items)
            {
                AddPredefinedNode(node, nodeSet.NamespaceUris[0], td.Properties);
            }

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"), JsonConvert.SerializeObject(td, Newtonsoft.Json.Formatting.Indented));
        }

        private static void AddPredefinedNode(UANode predefinedNode, string namespaceUri, Dictionary<string, Property> tdProperties)
        {
            if (predefinedNode is UAVariable)
            {
                UAVariable variable = (UAVariable)predefinedNode;

                GenericForm form = new()
                {
                    Href = "nsu=" + namespaceUri + ";" + variable.NodeId.Replace("ns=1;", ""),
                    Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                    Type = TypeString.Float,
                    PollingTime = 1000
                };

                foreach( Export.Reference reference in variable.References)
                {
                    if (reference.ReferenceType == "HasModellingRule")
                    {
                        // we're not interested in models, only instances
                        return;
                    }
                }

                if ((predefinedNode.DisplayName[0].Value == "InputArguments") || (predefinedNode.DisplayName[0].Value == "OuputArguments"))
                {
                    // we're not interested in method arguments
                    return;
                }

                Property property = new()
                {
                    Type = TypeEnum.Number,
                    ReadOnly = true,
                    Observable = true,
                    Forms = new object[1] { form }
                };

                if (!tdProperties.ContainsKey(predefinedNode.DisplayName[0].Value))
                {
                    tdProperties.Add(predefinedNode.DisplayName[0].Value, property);
                }
            }
        }
    }
}
