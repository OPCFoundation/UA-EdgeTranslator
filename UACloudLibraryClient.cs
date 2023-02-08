
namespace Opc.Ua.Edge.Translator
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Export;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text;

    public class UACloudLibraryClient
    {
        public List<string> _nodeSetFilenames = new List<string>();

        private HttpClient _client = new HttpClient();

        private Dictionary<string, string> _namespacesInCloudLibrary = new Dictionary<string, string>();

        public void Login(string nodesetUrl, string clientId, string secret)
        {
            if (!string.IsNullOrEmpty(nodesetUrl) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(secret))
            {
                _client.DefaultRequestHeaders.Remove("Authorization");
                _client.DefaultRequestHeaders.Add("Authorization", "basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId + ":" + secret)));

                // get namespaces
                string address = "https://uacloudlibrary.opcfoundation.org/infomodel/namespaces";
                HttpResponseMessage response = _client.Send(new HttpRequestMessage(HttpMethod.Get, address));
                string[] identifiers = JsonConvert.DeserializeObject<string[]>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                _namespacesInCloudLibrary.Clear();
                foreach (string nodeset in identifiers)
                {
                    string[] tuple = nodeset.Split(",");

                    if (_namespacesInCloudLibrary.ContainsKey(tuple[0]))
                    {
                        _namespacesInCloudLibrary[tuple[0]] = tuple[1];
                    }
                    else
                    {
                        _namespacesInCloudLibrary.Add(tuple[0], tuple[1]);
                    }
                }

                response = _client.Send(new HttpRequestMessage(HttpMethod.Get, nodesetUrl));
                AddressSpace addressSpace = JsonConvert.DeserializeObject<AddressSpace>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                // store the file locally
                string filePath = Path.Combine(Directory.GetCurrentDirectory(), addressSpace.Title + ".nodeset2.xml");
                File.WriteAllText(filePath, addressSpace.Nodeset.NodesetXml);
                _nodeSetFilenames.Add(filePath);

                ValidateNamespacesAndModels(true);
            }
        }

        private string ValidateNamespacesAndModels(bool autodownloadreferences)
        {
            // Collect all models as well as all required/referenced model namespace URIs listed in each file
            List<string> models = new List<string>();
            List<string> modelreferences = new List<string>();
            foreach (string nodesetFile in _nodeSetFilenames)
            {
                using (Stream stream = new FileStream(nodesetFile, FileMode.Open))
                {
                    UANodeSet nodeSet = UANodeSet.Read(stream);

                    // validate namespace URIs
                    if ((nodeSet.NamespaceUris != null) && (nodeSet.NamespaceUris.Length > 0))
                    {
                        foreach (string ns in nodeSet.NamespaceUris)
                        {
                            if (string.IsNullOrEmpty(ns) || !Uri.IsWellFormedUriString(ns, UriKind.Absolute))
                            {
                                return "Nodeset file " + nodesetFile + " contains an invalid Namespace URI: \"" + ns + "\"";
                            }
                        }
                    }
                    else
                    {
                        return "'NamespaceUris' entry missing in " + nodesetFile + ". Please add it!";
                    }

                    // validate model URIs
                    if ((nodeSet.Models != null) && (nodeSet.Models.Length > 0))
                    {
                        foreach (ModelTableEntry model in nodeSet.Models)
                        {
                            if (model != null)
                            {
                                if (Uri.IsWellFormedUriString(model.ModelUri, UriKind.Absolute))
                                {
                                    // ignore the default namespace which is always present and don't add duplicates
                                    if ((model.ModelUri != "http://opcfoundation.org/UA/") && !models.Contains(model.ModelUri))
                                    {
                                        models.Add(model.ModelUri);
                                    }
                                }
                                else
                                {
                                    return "Nodeset file " + nodesetFile + " contains an invalid Model Namespace URI: \"" + model.ModelUri + "\"";
                                }

                                if ((model.RequiredModel != null) && (model.RequiredModel.Length > 0))
                                {
                                    foreach (ModelTableEntry requiredModel in model.RequiredModel)
                                    {
                                        if (requiredModel != null)
                                        {
                                            if (Uri.IsWellFormedUriString(requiredModel.ModelUri, UriKind.Absolute))
                                            {
                                                // ignore the default namespace which is always required and don't add duplicates
                                                if ((requiredModel.ModelUri != "http://opcfoundation.org/UA/") && !modelreferences.Contains(requiredModel.ModelUri))
                                                {
                                                    modelreferences.Add(requiredModel.ModelUri);
                                                }
                                            }
                                            else
                                            {
                                                return "Nodeset file " + nodesetFile + " contains an invalid referenced Model Namespace URI: \"" + requiredModel.ModelUri + "\"";
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        return "'Model' entry missing in " + nodesetFile + ". Please add it!";
                    }
                }
            }

            // now check if we have all references for each model we want to load
            foreach (string modelreference in modelreferences)
            {
                if (!models.Contains(modelreference))
                {
                    if (!autodownloadreferences)
                    {
                        return "Referenced OPC UA model " + modelreference + " is missing from selected list of nodeset files, please add the corresponding nodeset file to the list of loaded files!";
                    }
                    else
                    {
                        try
                        {
                            // try to auto-download the missing references from the UA Cloud Library
                            string address = "https://uacloudlibrary.opcfoundation.org/infomodel/download/" + Uri.EscapeDataString(_namespacesInCloudLibrary[modelreference]);
                            HttpResponseMessage response = _client.Send(new HttpRequestMessage(HttpMethod.Get, address));
                            AddressSpace addressSpace = JsonConvert.DeserializeObject<AddressSpace>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                            // store the file
                            string filePath = Path.Combine(Directory.GetCurrentDirectory(), addressSpace.Category.Name + ".nodeset2.xml");
                            File.WriteAllText(filePath, addressSpace.Nodeset.NodesetXml);
                            _nodeSetFilenames.Add(filePath);
                        }
                        catch (Exception ex)
                        {
                            return "Could not download referenced nodeset " + modelreference + ": " + ex.Message;
                        }
                    }
                }
            }

            return string.Empty; // no error
        }
    }
}
