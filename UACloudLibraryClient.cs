
namespace Opc.Ua.Edge.Translator
{
    using Newtonsoft.Json;
    using Opc.Ua.Cloud.Library.Models;
    using Opc.Ua.Export;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text;

    public class UACloudLibraryClient
    {
        private HttpClient _client = new HttpClient();

        private Dictionary<string, Tuple<string, string>> _namespacesInCloudLibrary = new();

        private string _uaCloudLibraryUrl = Environment.GetEnvironmentVariable("UACLURL");

        public void Login()
        {
            if (string.IsNullOrEmpty(_uaCloudLibraryUrl))
            {
                Log.Logger.Warning("UACLURL environment variable is not set.");
                return;
            }

            if (!_uaCloudLibraryUrl.EndsWith('/'))
            {
                _uaCloudLibraryUrl += "/";
            }

            string clientId = Environment.GetEnvironmentVariable("UACLUsername");
            if (string.IsNullOrEmpty(clientId))
            {
                Log.Logger.Warning("UACLUsername environment variable is not set.");
                return;
            }

            string secret = Environment.GetEnvironmentVariable("UACLPassword");
            if (string.IsNullOrEmpty(secret))
            {
                Log.Logger.Warning("UACLPassword environment variable is not set.");
                return;
            }

            _client.DefaultRequestHeaders.Remove("Authorization");
            _client.DefaultRequestHeaders.Add("Authorization", "basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(clientId + ":" + secret)));

            // get namespaces
            string address = _uaCloudLibraryUrl + "infomodel/namespaces";
            HttpResponseMessage response = _client.Send(new HttpRequestMessage(HttpMethod.Get, address));
            string[] identifiers = JsonConvert.DeserializeObject<string[]>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

            if (identifiers != null)
            {
                foreach (string nodeset in identifiers)
                {
                    string[] tuple = nodeset.Split(",");

                    if (_namespacesInCloudLibrary.ContainsKey(tuple[0]))
                    {
                        // only store the latest version of a given nodeset
                        if (int.Parse(_namespacesInCloudLibrary[tuple[0]].Item2.Replace(".", "")) < int.Parse(tuple[2].Replace(".", "")))
                        {
                            _namespacesInCloudLibrary[tuple[0]] = new Tuple<string, string>(tuple[1], tuple[2]);
                        }
                    }
                    else
                    {
                        _namespacesInCloudLibrary.Add(tuple[0], new Tuple<string, string>(tuple[1], tuple[2]));
                    }
                }
            }
        }

        public List<string> DownloadNodeset(string namespaceUrl)
        {
            List<string> namespaces = new List<string>();

            if (string.IsNullOrEmpty(namespaceUrl))
            {
                Log.Logger.Error("Namespace URL is null or empty.");
                return namespaces;
            }

            string filePath = null;

            // check if we have the nodeset already downloaded
            List<string> existingNamespaces = GetNamespacesFromDownloadedNodesets();
            if (!existingNamespaces.Contains(namespaceUrl))
            {
                Log.Logger.Information("Nodeset " + namespaceUrl + " not available locally, trying download from cloud library.");

                // check the cloud library if we have the nodeset available
                if (_namespacesInCloudLibrary.ContainsKey(namespaceUrl))
                {
                    try
                    {
                        string address = _uaCloudLibraryUrl + "infomodel/download/" + Uri.EscapeDataString(_namespacesInCloudLibrary[namespaceUrl].Item1);
                        HttpResponseMessage response = _client.Send(new HttpRequestMessage(HttpMethod.Get, address));

                        UANameSpace nameSpace = JsonConvert.DeserializeObject<UANameSpace>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                        if (!string.IsNullOrEmpty(nameSpace.Nodeset.NodesetXml))
                        {
                            filePath = Path.Combine(Directory.GetCurrentDirectory(), "nodesets", nameSpace.Title + ".nodeset2.xml");
                            File.WriteAllText(filePath, nameSpace.Nodeset.NodesetXml);
                            Log.Logger.Information("Downloaded nodeset " + namespaceUrl + " from cloud library.");
                            namespaces.Add(namespaceUrl);
                        }
                        else
                        {
                            Log.Logger.Error("Nodeset " + namespaceUrl + " not found in cloud library.");
                            return namespaces;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error("Could not download nodeset " + namespaceUrl + ": " + ex.Message);
                        return namespaces;
                    }
                }
                else
                {
                    Log.Logger.Error("Nodeset " + namespaceUrl + " not available in cloud library.");
                    return namespaces;
                }
            }
            else
            {
                filePath = GetFilePathFromDownloadedNamespace(namespaceUrl);
                Log.Logger.Information("Nodeset " + namespaceUrl + " already downloaded.");
                namespaces.Add(namespaceUrl);
            }

            // check that we also have all dependent nodesets
            List<string> dependentNamespaces = EnumerateDependentNodesets(filePath);
            foreach (string dependentNodeset in dependentNamespaces)
            {
                if (!namespaces.Contains(dependentNodeset))
                {
                    // recursively download dependent nodesets
                    List<string> downloadedDependentNamespaces = DownloadNodeset(dependentNodeset);
                    foreach (string downloadedDependentNamespace in downloadedDependentNamespaces)
                    {
                        if (!namespaces.Contains(downloadedDependentNamespace))
                        {
                            namespaces.Add(downloadedDependentNamespace);
                        }
                    }
                }
            }

            return namespaces;
        }

        private string GetFilePathFromDownloadedNamespace(string namespaceUrl)
        {
            string filePath = null;

            foreach (string file in Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "nodesets")))
            {
                using (Stream stream = new FileStream(file, FileMode.Open))
                {
                    UANodeSet nodeSet = UANodeSet.Read(stream);
                    if ((nodeSet.NamespaceUris != null) && (nodeSet.NamespaceUris.Length > 0))
                    {
                        foreach (string ns in nodeSet.NamespaceUris)
                        {
                            if (ns == namespaceUrl)
                            {
                                filePath = file;
                                break;
                            }
                        }
                    }
                }
            }

            return filePath;
        }

        public List<string> GetNamespacesFromDownloadedNodesets()
        {
            List<string> namespaces = new List<string>();

            // check if we have any nodesets in our directory
            string[] files = Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "nodesets"));
            if (files.Length > 0)
            {
                foreach (string file in files)
                {
                    using (Stream stream = new FileStream(file, FileMode.Open))
                    {
                        UANodeSet nodeSet = UANodeSet.Read(stream);
                        if ((nodeSet.Models != null) && (nodeSet.Models.Length > 0))
                        {
                            foreach (ModelTableEntry te in nodeSet.Models)
                            {
                                if (!namespaces.Contains(te.ModelUri))
                                {
                                    namespaces.Add(te.ModelUri);
                                }
                            }
                        }
                    }
                }
            }

            return namespaces;
        }

        private List<string> EnumerateDependentNodesets(string nodesetFilename)
        {
            // Collect all referenced model namespace URIs listed in the file
            List<string> modelreferences = new List<string>();

            using (Stream stream = new FileStream(nodesetFilename, FileMode.Open))
            {
                UANodeSet nodeSet = UANodeSet.Read(stream);

                // validate namespace URIs
                if ((nodeSet.NamespaceUris != null) && (nodeSet.NamespaceUris.Length > 0))
                {
                    foreach (string ns in nodeSet.NamespaceUris)
                    {
                        if (string.IsNullOrEmpty(ns) || !Uri.IsWellFormedUriString(ns, UriKind.Absolute))
                        {
                            Log.Logger.Error("Nodeset file " + nodesetFilename + " contains an invalid Namespace URI: \"" + ns + "\"");
                            return modelreferences;
                        }
                    }
                }
                else
                {
                    Log.Logger.Error("'NamespaceUris' entry missing in " + nodesetFilename + ".");
                    return modelreferences;
                }

                // validate model URIs
                if ((nodeSet.Models != null) && (nodeSet.Models.Length > 0))
                {
                    foreach (ModelTableEntry model in nodeSet.Models)
                    {
                        if (model != null)
                        {
                            if (!Uri.IsWellFormedUriString(model.ModelUri, UriKind.Absolute))
                            {
                                Log.Logger.Error("Nodeset file " + nodesetFilename + " contains an invalid Model Namespace URI: \"" + model.ModelUri + "\"");
                                return modelreferences;
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
                                            Log.Logger.Error("Nodeset file " + nodesetFilename + " contains an invalid referenced Model Namespace URI: \"" + requiredModel.ModelUri + "\"");
                                            return modelreferences;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    Log.Logger.Error("'Model' entry missing in " + nodesetFilename + ".");
                    return modelreferences;
                }
            }

            // return the collected model references
            return modelreferences;
        }
    }
}
