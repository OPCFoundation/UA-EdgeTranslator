namespace Opc.Ua.Edge.Translator
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    public class WoT2DTDLMapper
    {
        public static string WoT2DTDL(string contents)
        {
            // Map WoT Thing Description to DTDL device model equivalent
            try
            {
                ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(contents);

                DTDL dtdl = new();
                dtdl.Context = "dtmi:dtdl:context;2";
                dtdl.Id = "dtmi:" + td.Title.ToLowerInvariant().Replace(' ', ':') + ";1";
                dtdl.Type = "Interface";
                dtdl.DisplayName = td.Name;
                dtdl.Description = td.Title;
                dtdl.Comment = td.Base;

                foreach (object ns in td.Context)
                {
                    if (!ns.ToString().Contains("https://www.w3.org/") && ns.ToString().Contains("opcua"))
                    {
                        OpcUaNamespaces namespaces = JsonConvert.DeserializeObject<OpcUaNamespaces>(ns.ToString());
                        foreach (Uri opcuaCompanionSpecUrl in namespaces.Namespaces)
                        {
                            dtdl.Comment += ";" + opcuaCompanionSpecUrl.ToString();
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(td.OpcUaType))
                {
                    dtdl.Comment += $";type:{td.OpcUaType}";
                }

                dtdl.Contents = new List<Content>();
                foreach (KeyValuePair<string, Property> property in td.Properties)
                {
                    foreach (object form in property.Value.Forms)
                    {
                        if (td.Base.ToLower().StartsWith("modbus+tcp://"))
                        {
                            ModbusForm modbusForm = JsonConvert.DeserializeObject<ModbusForm>(form.ToString());
                            Content content = new();
                            content.Type = "Telemetry";
                            content.Name = modbusForm.Href;
                            content.DisplayName = property.Key;
                            content.Description = modbusForm.ModbusEntity.ToString().ToLower() + ";" + modbusForm.ModbusPollingTime.ToString();

                            content.Comment = $"type:{property.Value.OpcUaType}";

                            if (!string.IsNullOrWhiteSpace(property.Value.OpcUaFieldPath))
                            {
                                content.Comment += $";fieldpath:{property.Value.OpcUaFieldPath}";
                            }

                            switch (modbusForm.ModbusType)
                            {
                                case ModbusType.Float: content.Schema = "float"; break;
                                default: content.Schema = "float"; break;
                            }

                            dtdl.Contents.Add(content);
                        }
                    }
                }

                return JsonConvert.SerializeObject(dtdl, Formatting.Indented);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                return string.Empty;
            }
        }

        public static string DTDL2WoT(string contents)
        {
            // Map DTDL device model to WoT Thing Description equivalent
            try
            {
                DTDL dtdl = JsonConvert.DeserializeObject<DTDL>(contents);

                ThingDescription td = new();

                List<Uri> context = new()
                {
                    new Uri("https://www.w3.org/2022/wot/td/v1.1", UriKind.Absolute)
                };

                string[] comments = SplitWithNodeIds(';', dtdl.Comment);
                if ((comments != null) && (comments.Length > 1))
                {
                    for (var i = 1; i < comments.Length; i++)
                    {
                        var comment = comments[i];
                        Log.Logger.Debug(comment);

                        if (comment.StartsWith("type:"))
                        {
                            td.OpcUaType = comment.Substring("type:".Length);
                        }
                        else
                        {
                            context.Add(new Uri(comment, UriKind.Absolute));
                        }
                    }
                }

                td.Context = new object[context.Count];
                for (int i = 0;i < context.Count; i++)
                {
                    if (context[i].ToString().Contains("https://www.w3.org/"))
                    {
                        td.Context[i] = context[i];
                    }
                    else
                    {
                        JObject jsonObject = new()
                        {
                            { "opcua", context[i] }
                        };

                        td.Context[i] = jsonObject;
                    }
                }

                string[] idParts = dtdl.Id.Split(":");
                if (idParts != null && idParts.Length > 1)
                {
                    string deviceID = idParts[idParts.Length - 1];
                    deviceID = deviceID.Substring(0, deviceID.IndexOf(';'));
                    td.Id = "urn:" + deviceID;
                }

                td.SecurityDefinitions = new();
                td.SecurityDefinitions.NosecSc = new();
                td.SecurityDefinitions.NosecSc.Scheme = "nosec";
                td.Security = new List<string>(){ "nosec_sc" }.ToArray();
                td.Type = new List<string>() { "Thing" }.ToArray();
                td.Name = dtdl.DisplayName;
                td.Title = dtdl.Description;

                if ((comments != null) && (comments.Length > 0))
                {
                    td.Base = comments[0];
                }

                td.Properties = new Dictionary<string, Property>();
                List<ModbusForm> forms = new();
                Property property = null;

                string displayName = string.Empty;

                foreach (Content content in dtdl.Contents)
                {
                    property = new();
                    property.Type = TypeEnum.Number;
                    property.ReadOnly = true;
                    property.Observable = true;

                    if ((comments != null) && (comments.Length > 0) && comments[0].StartsWith("modbus+tcp://"))
                    {
                        if (string.IsNullOrEmpty(displayName))
                        {
                            displayName = content.DisplayName;
                        }

                        ModbusForm form = new();
                        form.Href = content.Name;
                        form.Op = new List<Op>() { Op.Readproperty, Op.Observeproperty }.ToArray();

                        string[] uaData = SplitWithNodeIds(';', content.Comment);
                        foreach (string uaDataPart in uaData)
                        {
                            if (uaDataPart.StartsWith("type:"))
                            {
                                property.OpcUaType = uaDataPart.Substring("type:".Length);
                            }

                            if (uaDataPart.StartsWith("fieldpath:"))
                            {
                                property.OpcUaFieldPath = uaDataPart.Substring("fieldpath:".Length);
                            }
                        }

                        switch (content.Schema)
                        {
                            case "float": form.ModbusType = ModbusType.Float; break;
                            default: form.ModbusType = ModbusType.Float; break;
                        }

                        string[] descriptionParts = content.Description.Split(';');
                        if ((descriptionParts != null) && (descriptionParts.Length > 0) && descriptionParts[0] == "holdingRegister")
                        {
                            form.ModbusEntity = ModbusEntity.HoldingRegister;
                        }

                        if ((descriptionParts != null) && (descriptionParts.Length > 1) && long.TryParse(descriptionParts[1], out long result))
                        {
                            form.ModbusPollingTime = long.Parse(descriptionParts[1]);
                        }

                        // check if we are at a new property
                        if (displayName != content.DisplayName)
                        {
                            property.Forms = forms.ToArray();
                            forms.Clear();

                            td.Properties.Add(displayName, property);
                            displayName = content.DisplayName;
                        }

                        forms.Add(form);
                    }
                }

                property = new();
                property.Type = TypeEnum.Number;
                property.ReadOnly = true;
                property.Observable = true;
                property.Forms = forms.ToArray();
                td.Properties.Add(displayName, property);

                return JsonConvert.SerializeObject(td, Formatting.Indented, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex.Message, ex);
                return string.Empty;
            }
        }

        private static string[] SplitWithNodeIds(char separator, string content)
        {
            if (content != null)
            {
                return Regex.Split(content, $"{separator}(?![sigb]=)", RegexOptions.None);
            }
            else
            {
                return null;
            }
        }
    }
}
