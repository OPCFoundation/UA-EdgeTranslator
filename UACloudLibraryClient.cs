
namespace Opc.Ua.Edge.Translator
{
    using Newtonsoft.Json;
    using Opc.Ua.Cloud.Library.Models;
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

        public Dictionary<string, string> NamespacesInCloudLibrary { get; private set; } = new Dictionary<string, string>();

        public void Login(string uaCloudLibraryUrl, string clientId, string secret)
        {
            if ((NamespacesInCloudLibrary.Count == 0) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(secret))
            {
                _client.DefaultRequestHeaders.Remove("Authorization");
                _client.DefaultRequestHeaders.Add("Authorization", "basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId + ":" + secret)));

                if (!uaCloudLibraryUrl.EndsWith('/'))
                {
                    uaCloudLibraryUrl += "/";
                }

                // get namespaces
                string address = uaCloudLibraryUrl + "infomodel/namespaces";
                HttpResponseMessage response = _client.Send(new HttpRequestMessage(HttpMethod.Get, address));
                string[] identifiers = JsonConvert.DeserializeObject<string[]>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                foreach (string nodeset in identifiers)
                {
                    string[] tuple = nodeset.Split(",");

                    if (NamespacesInCloudLibrary.ContainsKey(tuple[0]))
                    {
                        NamespacesInCloudLibrary[tuple[0]] = tuple[1];
                    }
                    else
                    {
                        NamespacesInCloudLibrary.Add(tuple[0], tuple[1]);
                    }
                }
            }
        }

        public bool DownloadNamespace(string uaCloudLibraryUrl, string namespaceUrl)
        {
            if (!string.IsNullOrEmpty(uaCloudLibraryUrl) && !string.IsNullOrEmpty(namespaceUrl) && NamespacesInCloudLibrary.ContainsKey(namespaceUrl))
            {
                if (!uaCloudLibraryUrl.EndsWith('/'))
                {
                    uaCloudLibraryUrl += "/";
                }

                string address = uaCloudLibraryUrl + "infomodel/download/" + Uri.EscapeDataString(NamespacesInCloudLibrary[namespaceUrl]);
                HttpResponseMessage response = _client.Send(new HttpRequestMessage(HttpMethod.Get, address));

                try
                {
                    UANameSpace nameSpace = JsonConvert.DeserializeObject<UANameSpace>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                    if (!string.IsNullOrEmpty(nameSpace.Nodeset.NodesetXml))
                    {
                        // store the file locally
                        string filePath = Path.Combine(Directory.GetCurrentDirectory(), nameSpace.Title + ".nodeset2.xml");
                        File.WriteAllText(filePath, nameSpace.Nodeset.NodesetXml);

                        _nodeSetFilenames.Add(filePath);

                        return true;
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return false;
        }

        public string ValidateNamespacesAndModels(string uaCloudLibraryUrl, bool autodownloadreferences)
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
                            if (!uaCloudLibraryUrl.EndsWith('/'))
                            {
                                uaCloudLibraryUrl += "/";
                            }

                            string address = uaCloudLibraryUrl + "infomodel/download/" + Uri.EscapeDataString(NamespacesInCloudLibrary[modelreference]);
                            HttpResponseMessage response = _client.Send(new HttpRequestMessage(HttpMethod.Get, address));
                            UANameSpace nameSpace = JsonConvert.DeserializeObject<UANameSpace>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                            // store the file
                            string filePath = Path.Combine(Directory.GetCurrentDirectory(), nameSpace.Category.Name + ".nodeset2.xml");
                            File.WriteAllText(filePath, nameSpace.Nodeset.NodesetXml);
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
