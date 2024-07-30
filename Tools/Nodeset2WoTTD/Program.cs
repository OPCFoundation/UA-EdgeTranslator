namespace Opc.Ua.Edge.Translator.Tools
{
    using Newtonsoft.Json;
    using Opc.Ua.Export;

#nullable enable

    internal class Program
    {
        static void Main(string[] args)
        {
            ImportNodeset2Xml(args[0]);
        }

        // Nodeset2 files can be edited using e.g. the SIEMENS OPC UA Modeling Editor (SiOME)
        // see https://support.industry.siemens.com/cs/document/109755133/siemens-opc-ua-modeling-editor-(siome)-for-implementing-opc-ua-companion-specification
        private static void ImportNodeset2Xml(string filename)
        {
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

            Stream stream = new FileStream(filename, FileMode.Open);
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
