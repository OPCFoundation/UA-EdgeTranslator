namespace Opc.Ua.Edge.Translator.Tools
{
    using Newtonsoft.Json;
    using Opc.Ua.Export;
    using Aml.Engine.CAEX;
    using System;
    using Opc.Ua.Edge.Translator.Models;
    using Aml.Engine.CAEX.Extensions;
    using System.Runtime.InteropServices;
    using Org.BouncyCastle.Asn1.Cms;

#nullable enable

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
            }
        }

        private static void ImportAASAID(string filename)
        {
            AAS_AID? aid = JsonConvert.DeserializeObject<AAS_AID>(File.ReadAllText(filename));

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

            if (aid?.SubmodelElements != null)
            {
                ImportSubmodelElementCollection(aid.SubmodelElements);
            }

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"), JsonConvert.SerializeObject(td, Formatting.Indented));
        }

        private static void ImportSubmodelElementCollection(List<SubmodelElement> smec)
        {
            foreach (SubmodelElement submodelElement in smec)
            {
                if (submodelElement.ValueType == null)
                {
                    List<SubmodelElement>? nestedSMEC = JsonConvert.DeserializeObject<List<SubmodelElement>>(submodelElement?.Value?.ToString());

                    if (nestedSMEC != null)
                    {
                        ImportSubmodelElementCollection(nestedSMEC);
                    }
                }
                else
                {
                    Console.WriteLine("Name: " + submodelElement?.IdShort + " Value: " + submodelElement?.Value?.ToString());
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

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"), JsonConvert.SerializeObject(td, Formatting.Indented));
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
                    Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
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

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), Path.GetFileNameWithoutExtension(filename) + ".tm.jsonld"), JsonConvert.SerializeObject(td, Formatting.Indented));
        }

        private static void AddPredefinedNode(UANode predefinedNode, string namespaceUri, Dictionary<string, Property> tdProperties)
        {
            if (predefinedNode is UAVariable)
            {
                UAVariable variable = (UAVariable)predefinedNode;

                OPCUAForm form = new()
                {
                    Href = "nsu=" + namespaceUri + ";" + variable.NodeId.Replace("ns=1;", ""),
                    Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                    OPCUAType = OPCUAType.Float,
                    OPCUAPollingTime = 1000
                };

                foreach( Reference reference in variable.References)
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
