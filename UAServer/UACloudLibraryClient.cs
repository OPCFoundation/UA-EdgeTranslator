
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

        public string DownloadNodeset(string namespaceUrl)
        {
            if (string.IsNullOrEmpty(namespaceUrl))
            {
                Log.Logger.Error("Namespace URL is null or empty.");
                return string.Empty;
            }

            string filePath = null;

            // check if we have the nodeset already downloaded
            string nodesetXml = GetDownloadedNodesetXml(namespaceUrl);
            if (string.IsNullOrEmpty(nodesetXml))
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
                            return nameSpace.Nodeset.NodesetXml;
                        }
                        else
                        {
                            Log.Logger.Error("Nodeset " + namespaceUrl + " not found in cloud library.");
                            return string.Empty;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error("Could not download nodeset " + namespaceUrl + ": " + ex.Message);
                        return string.Empty;
                    }
                }
                else
                {
                    Log.Logger.Error("Nodeset " + namespaceUrl + " not available in cloud library.");
                    return string.Empty;
                }
            }
            else
            {
                Log.Logger.Information("Nodeset " + namespaceUrl + " already downloaded.");
                return nodesetXml;
            }
        }

        public string GetDownloadedNodesetXml(string namespaceUrl)
        {
            if (string.IsNullOrEmpty(namespaceUrl))
            {
                Log.Logger.Error("Namespace URL is null or empty.");
                return string.Empty;
            }

            foreach (string file in Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "nodesets")))
            {
                using (Stream stream = new FileStream(file, FileMode.Open))
                {
                    UANodeSet nodeSet = UANodeSet.Read(stream);
                    if ((nodeSet.Models != null) && (nodeSet.Models.Length > 0))
                    {
                        foreach (ModelTableEntry te in nodeSet.Models)
                        {
                            if (te.ModelUri == namespaceUrl)
                            {
                                return File.ReadAllText(file);
                            }
                        }
                    }
                }
            }

            Log.Logger.Error("Nodeset " + namespaceUrl + " not found.");
            return string.Empty;
        }
    }
}
