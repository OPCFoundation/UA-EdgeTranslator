namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using libplctag;
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;

    public class RockwellProtocolDriver : IProtocolDriver
    {
        public string Scheme => "eip";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/eip";

        // Cache of UDT definitions resolved during BrowseAndGenerateTD, keyed by UDT type ID.
        // Avoids re-reading the same UDT definition from the PLC when multiple tags share a type.
        private readonly Dictionary<int, EIPStructureDefinition> _udtDefinitionCache = new();

        /// <summary>
        /// Cache of assets created by this driver, keyed by asset name.
        /// Used to register UDT definitions on the asset during tag creation.
        /// </summary>
        private readonly Dictionary<string, RockwellAsset> _assets = new();

        public IEnumerable<string> Discover()
        {
            List<string> assets = new();

            // Ethernet/IP uses the boradcast message "ListIdentity" to discover PLCs on the network
            var broadcastAddress = GetBroadcastAddress();

            // Create a UDP client
            using (var udpClient = new UdpClient())
            {
                udpClient.EnableBroadcast = true;

                var listIdentityMessage = new byte[24];
                for (var i = 0; i < listIdentityMessage.Length; i++)
                {
                    listIdentityMessage[i] = 0;
                }
                listIdentityMessage[0] = 0x63;

                var endPoint = new IPEndPoint(IPAddress.Parse(broadcastAddress), 0xAF12);
                udpClient.Send(listIdentityMessage, listIdentityMessage.Length, endPoint);
                udpClient.Client.ReceiveTimeout = 10000;

                try
                {
                    while (true)
                    {
                        var receiveEndPoint = new IPEndPoint(IPAddress.Any, 0xAF12);
                        var response = udpClient.Receive(ref receiveEndPoint);

                        Log.Logger.Information($"Ethernet/IP discovery: Received response from {receiveEndPoint.Address}");

                        assets.Add("eip://" + receiveEndPoint.Address.ToString());
                    }
                }
                catch (SocketException)
                {
                    // do nothing
                }
            }

            return assets;
        }

        private string GetBroadcastAddress()
        {
            // try to restricted local broadcast
            foreach (var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                foreach (var ip in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork
                     && !ip.Address.ToString().StartsWith("169.254.")
                     && !ip.Address.ToString().StartsWith("127.0."))
                    {
                        var strCurrentIP = ip.Address.ToString().Split('.');
                        var strIPNetMask = ip.IPv4Mask.ToString().Split('.');
                        var BroadcastStr = new StringBuilder();

                        for (var i = 0; i < 4; i++)
                        {
                            BroadcastStr.Append(((byte)(int.Parse(strCurrentIP[i]) | ~int.Parse(strIPNetMask[i]))).ToString());
                            if (i != 3) BroadcastStr.Append('.');
                        }

                        return BroadcastStr.ToString();
                    }
                }
            }

            // return generic broadcast address
            return "255.255.255.255";
        }

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            ThingDescription td = new()
            {
                Context = new string[1] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + assetName,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = ["nosec_sc"],
                Type = ["Thing"],
                Name = assetName,
                Base = assetEndpoint,
                Title = assetName,
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };

            var address = td.Base.Split([':', '/']);
            if (address.Length != 4 || address[0] != "eip")
            {
                throw new Exception("Expected Rockwell PLC address in the format eip://ipaddress!");
            }

            string gateway = address[3];

            // Clear the UDT cache for each new browse
            _udtDefinitionCache.Clear();

            Tag tags = new()
            {
                Gateway = gateway,
                Path = "1,0",
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip,
                Name = "@tags",
                Timeout = TimeSpan.FromSeconds(10),
            };

            tags.Read();
            var tagInfos = DecodeAllTags(tags);
            foreach (var tag in tagInfos)
            {
                if (!IsTag(tag))
                {
                    // not interested in anything that is not a tag
                    continue;
                }

                if (TagIsUdt(tag))
                {
                    var udtTypeId = GetUdtId(tag);

                    // Recursively build the structure definition, resolving nested UDTs
                    EIPStructureDefinition structDef = ResolveUdtDefinition(gateway, udtTypeId);

                    Log.Logger.Information($"EIP Tag: Id={tag.Name} UDT={structDef.TypeName}");

                    string propertyName = tag.Name;
                    string assetNamespaceUri = "http://opcfoundation.org/UA/" + assetName + "/";

                    EIPForm form = new()
                    {
                        Href = propertyName,
                        Op = new Op[2] { Op.Readproperty, Op.Observeproperty },
                        PollingTime = 1000,
                        StructureDefinition = structDef
                    };

                    Property property = new()
                    {
                        Type = TypeEnum.Object,
                        ReadOnly = true,
                        Observable = true,
                        OpcUaType = $"nsu={assetNamespaceUri};s={structDef.TypeName}Type",
                        Forms = [form]
                    };

                    if (!td.Properties.ContainsKey(propertyName))
                    {
                        td.Properties.Add(propertyName, property);
                    }
                }
                else
                {
                    Log.Logger.Information($"EIP Tag: Id={tag.Name} Type=" + ParseDataType(tag.Type));

                    string properyName = tag.Name;

                    EIPForm form = new()
                    {
                        Href = properyName + "?0",
                        Op = [Op.Readproperty, Op.Observeproperty],
                        PollingTime = 1000,
                        Type = ParseDataType(tag.Type)
                    };

                    Property property = new()
                    {
                        Type = TypeEnum.Number,
                        ReadOnly = true,
                        Observable = true,
                        Forms = [form]
                    };

                    if (!td.Properties.ContainsKey(properyName))
                    {
                        td.Properties.Add(properyName, property);
                    }
                }
            }

            return td;
        }

        /// <summary>
        /// Resolves a UDT type ID into a full EIPStructureDefinition, recursively
        /// resolving any nested UDT fields. Results are cached by type ID so each
        /// UDT is only read from the PLC once.
        /// </summary>
        private EIPStructureDefinition ResolveUdtDefinition(string gateway, int udtTypeId)
        {
            if (_udtDefinitionCache.TryGetValue(udtTypeId, out EIPStructureDefinition cached))
            {
                return cached;
            }

            Tag udtTag = new() {
                Gateway = gateway,
                Path = "1,0",
                PlcType = PlcType.ControlLogix,
                Protocol = Protocol.ab_eip,
                Name = $"@udt/{udtTypeId}",
            };

            udtTag.Read();
            var udt = DecodeUdtInfo(udtTag);

            EIPStructureDefinition structDef = new()
            {
                TypeName = udt.Name,
                Fields = new EIPFieldDefinition[udt.NumFields]
            };

            for (int i = 0; i < udt.NumFields; i++)
            {
                var field = udt.Fields[i];

                if (FieldIsUdt(field))
                {
                    // Nested UDT: recursively resolve its definition
                    int nestedUdtTypeId = GetFieldUdtId(field);
                    EIPStructureDefinition nestedDef = ResolveUdtDefinition(gateway, nestedUdtTypeId);

                    structDef.Fields[i] = new EIPFieldDefinition {
                        Name = field.Name,
                        Type = null, // no primitive type — use nested structure
                        Offset = (int)field.Offset,
                        StructureDefinition = nestedDef
                    };

                    Log.Logger.Information(
                        $"  Field: {field.Name} -> nested UDT '{nestedDef.TypeName}' at offset {field.Offset}");
                }
                else
                {
                    structDef.Fields[i] = new EIPFieldDefinition {
                        Name = field.Name,
                        Type = "xsd:" + ParseDataType(field.Type).ToString().Replace("xsd:", ""),
                        Offset = (int)field.Offset
                    };
                }
            }

            _udtDefinitionCache[udtTypeId] = structDef;
            return structDef;
        }

        /// <summary>
        /// Checks whether a UDT field is itself a nested UDT (struct).
        /// Uses the same bit mask as TagIsUdt but on the field's element type.
        /// </summary>
        private static bool FieldIsUdt(UdtFieldInfo field)
        {
            const ushort TYPE_IS_STRUCT = 0x8000;
            const ushort TYPE_IS_SYSTEM = 0x1000;

            return (field.Type & TYPE_IS_STRUCT) != 0 && (field.Type & TYPE_IS_SYSTEM) == 0;
        }

        /// <summary>
        /// Extracts the UDT type ID from a nested UDT field's element type.
        /// </summary>
        private static int GetFieldUdtId(UdtFieldInfo field)
        {
            const ushort TYPE_UDT_ID_MASK = 0x0FFF;
            return field.Type & TYPE_UDT_ID_MASK;
        }

        /// <summary>
        /// Scans all properties in the TD for eip:structureDefinition entries and
        /// creates corresponding OPC UA StructureType DataType nodes in the asset's namespace.
        /// Nested UDTs are created bottom-up (dependencies first) via recursion.
        /// Called by UANodeManager after namespace setup, before AddNodeForWoTForm().
        /// </summary>
        public void RegisterStructureTypes(ThingDescription td, UANodeManager nodeManager)
        {
            if (td.Properties == null || td.Properties.Count == 0)
            {
                return;
            }

            string assetNamespaceUri = "http://opcfoundation.org/UA/" + td.Name + "/";
            ushort assetNamespaceIndex = (ushort)nodeManager.Server.NamespaceUris.GetIndex(assetNamespaceUri);

            // Track which type names we've already created to avoid duplicates
            // (multiple tags can share the same UDT type)
            HashSet<string> registeredTypes = new();

            foreach (var property in td.Properties)
            {
                if (property.Value.Forms == null)
                {
                    continue;
                }

                foreach (object formObj in property.Value.Forms)
                {
                    EIPForm eipForm;
                    try
                    {
                        eipForm = JsonConvert.DeserializeObject<EIPForm>(formObj.ToString());
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Error($"Failed to parse EIP form for property '{property.Key}': {ex.Message}");
                        continue;
                    }

                    if (eipForm?.StructureDefinition == null)
                    {
                        continue;
                    }

                    // Recursively register this type and all its nested dependencies
                    RegisterStructureTypeRecursive(
                        eipForm.StructureDefinition,
                        nodeManager,
                        assetNamespaceIndex,
                        registeredTypes);
                }
            }
        }

        /// <summary>
        /// Recursively registers an EIP structure definition as an OPC UA DataType.
        /// Nested UDT fields are registered first (depth-first) so that when a parent
        /// type is created, the DataType NodeIds for its nested fields already exist.
        /// </summary>
        private static void RegisterStructureTypeRecursive(
            EIPStructureDefinition structDef,
            UANodeManager nodeManager,
            ushort namespaceIndex,
            HashSet<string> registeredTypes)
        {
            if (structDef?.TypeName == null || registeredTypes.Contains(structDef.TypeName))
            {
                return;
            }

            // Depth-first: register nested types before the parent
            if (structDef.Fields != null)
            {
                foreach (var field in structDef.Fields)
                {
                    if (field.StructureDefinition != null)
                    {
                        RegisterStructureTypeRecursive(
                            field.StructureDefinition,
                            nodeManager,
                            namespaceIndex,
                            registeredTypes);
                    }
                }
            }

            CreateStructureDataType(structDef, nodeManager, namespaceIndex);
            registeredTypes.Add(structDef.TypeName);

            Log.Logger.Information(
                $"Registered OPC UA StructureType '{structDef.TypeName}' " +
                $"with {structDef.Fields?.Length ?? 0} fields");
        }

        /// <summary>
        /// Creates an OPC UA DataTypeState node with StructureDefinition and
        /// Default Binary encoding. For fields that are nested UDTs, the
        /// StructureField.DataType points to the nested type's NodeId (which
        /// must already exist — ensured by depth-first registration).
        /// Plus a Binary Schema type dictionary entry so legacy clients can
        /// decode the structure.
        /// </summary>
        private static void CreateStructureDataType(
            EIPStructureDefinition structDef,
            UANodeManager nodeManager,
            ushort namespaceIndex)
        {
            NodeId dataTypeNodeId = new(structDef.TypeName + "Type", namespaceIndex);

            // Check if this type already exists (e.g. loaded from a Companion Spec)
            if (nodeManager.Find(dataTypeNodeId) != null)
            {
                return;
            }

            // --- 1. DataType node ---
            DataTypeState dataTypeNode = new() {
                SymbolicName = structDef.TypeName,
                NodeId = dataTypeNodeId,
                BrowseName = new QualifiedName(structDef.TypeName + "Type", namespaceIndex),
                DisplayName = new LocalizedText("en", structDef.TypeName + "Type"),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                IsAbstract = false,
                SuperTypeId = DataTypeIds.Structure
            };

            // StructureDefinition with fields
            StructureDefinition structureDefinition = new() {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure
            };

            StructureFieldCollection fields = new();
            if (structDef.Fields != null)
            {
                foreach (EIPFieldDefinition eipField in structDef.Fields)
                {
                    NodeId fieldDataType = eipField.StructureDefinition != null
                        ? new NodeId(eipField.StructureDefinition.TypeName + "Type", namespaceIndex)
                        : MapEIPTypeToOpcUaDataType(eipField.Type);

                    fields.Add(new StructureField() {
                        Name = eipField.Name,
                        Description = new LocalizedText("en", eipField.Description ?? string.Empty),
                        DataType = fieldDataType,
                        ValueRank = ValueRanks.Scalar,
                        ArrayDimensions = null,
                        MaxStringLength = 0,
                        IsOptional = false
                    });
                }
            }

            structureDefinition.Fields = fields;

            // --- 2. Default Binary encoding node ---
            NodeId encodingNodeId = new(structDef.TypeName + "Type_Encoding_DefaultBinary", namespaceIndex);
            BaseObjectState encodingNode = new(null)
            {
                SymbolicName = "Default Binary",
                NodeId = encodingNodeId,
                BrowseName = new QualifiedName("Default Binary", 0),
                DisplayName = new LocalizedText("en", "Default Binary"),
                TypeDefinitionId = ObjectTypeIds.DataTypeEncodingType,
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None
            };

            structureDefinition.DefaultEncodingId = encodingNodeId;
            dataTypeNode.DataTypeDefinition = new ExtensionObject(structureDefinition);

            // Wire DataType <-> Encoding using HasEncoding only (NOT AddChild which uses HasComponent)
            dataTypeNode.AddReference(ReferenceTypeIds.HasSubtype, true, DataTypeIds.Structure);
            dataTypeNode.AddReference(ReferenceTypeIds.HasEncoding, false, encodingNode.NodeId);
            encodingNode.AddReference(ReferenceTypeIds.HasEncoding, true, dataTypeNode.NodeId);

            // Add both nodes individually (no parent-child — they're linked by HasEncoding)
            nodeManager.AddPredefinedNodePublic(dataTypeNode);
            nodeManager.AddPredefinedNodePublic(encodingNode);

            // --- 3. Binary Schema type dictionary ---
            string namespaceUri = nodeManager.Server.NamespaceUris.GetString(namespaceIndex);
            NodeId dictionaryNodeId = new(namespaceUri + ".BinarySchema", namespaceIndex);
            BaseDataVariableState dictionary = nodeManager.Find(dictionaryNodeId) as BaseDataVariableState;

            bool dictionaryIsNew = (dictionary == null);
            if (dictionaryIsNew)
            {
                dictionary = new BaseDataVariableState(null) {
                    NodeId = dictionaryNodeId,
                    SymbolicName = namespaceUri + ".BinarySchema",
                    BrowseName = new QualifiedName(namespaceUri, namespaceIndex),
                    DisplayName = new LocalizedText("en", namespaceUri),
                    WriteMask = AttributeWriteMask.None,
                    UserWriteMask = AttributeWriteMask.None,
                    TypeDefinitionId = VariableTypeIds.DataTypeDictionaryType,
                    DataType = DataTypeIds.ByteString,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                };

                // Link to OPC Binary type system
                dictionary.AddReference(
                    ReferenceTypeIds.HasComponent, true,
                    ObjectIds.OPCBinarySchema_TypeSystem);

                var typeSystemNode = nodeManager.Find(ObjectIds.OPCBinarySchema_TypeSystem);
                typeSystemNode?.AddReference(
                    ReferenceTypeIds.HasComponent, false,
                    dictionaryNodeId);

                // NamespaceUri property — add as child BEFORE registering the dictionary
                PropertyState<string> nsProperty = new(dictionary) {
                    NodeId = new NodeId(namespaceUri + ".BinarySchema.NamespaceUri", namespaceIndex),
                    BrowseName = new QualifiedName("NamespaceUri", 0),
                    DisplayName = new LocalizedText("en", "NamespaceUri"),
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    DataType = DataTypeIds.String,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = namespaceUri
                };

                // AddChild here is fine — dictionary hasn't been added to the node manager yet
                dictionary.AddChild(nsProperty);
            }

            // --- 4. DataTypeDescription for this structure type ---
            NodeId descriptionNodeId = new(structDef.TypeName + "Type_Description", namespaceIndex);
            BaseDataVariableState description = new(dictionary)
            {
                NodeId = descriptionNodeId,
                BrowseName = new QualifiedName(structDef.TypeName, namespaceIndex),
                DisplayName = new LocalizedText("en", structDef.TypeName),
                TypeDefinitionId = VariableTypeIds.DataTypeDescriptionType,
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = structDef.TypeName
            };

            // HasDescription: encoding <-> description
            encodingNode.AddReference(ReferenceTypeIds.HasDescription, false, descriptionNodeId);
            description.AddReference(ReferenceTypeIds.HasDescription, true, encodingNodeId);

            // Add description as child of dictionary BEFORE the dictionary is registered
            dictionary.AddChild(description);

            if (dictionaryIsNew)
            {
                // First type: dictionary and all children get registered together
                nodeManager.AddPredefinedNodePublic(dictionary);
            }
            else
            {
                // Dictionary already registered: add the new description individually
                nodeManager.AddPredefinedNodePublic(description);
            }

            // --- 5. Rebuild BSD XML with all types registered so far ---
            RebuildBinarySchemaXml(dictionary, namespaceUri, namespaceIndex, nodeManager);
        }

        private static NodeId MapEIPTypeToOpcUaDataType(string eipType)
        {
            return eipType switch
            {
                "xsd:BOOL"   => DataTypeIds.Boolean,
                "xsd:SINT"   => DataTypeIds.SByte,
                "xsd:INT"    => DataTypeIds.Int16,
                "xsd:DINT"   => DataTypeIds.Int32,
                "xsd:LINT"   => DataTypeIds.Int64,
                "xsd:USINT"  => DataTypeIds.Byte,
                "xsd:UINT"   => DataTypeIds.UInt16,
                "xsd:UDINT"  => DataTypeIds.UInt32,
                "xsd:ULINT"  => DataTypeIds.UInt64,
                "xsd:REAL"   => DataTypeIds.Float,
                "xsd:LREAL"  => DataTypeIds.Double,
                "xsd:STRING" => DataTypeIds.String,
                _            => DataTypeIds.Float
            };
        }

        /// <summary>
        /// Rebuilds the OPC Binary Schema XML content for the dictionary from
        /// all DataTypeDescription children. Called each time a new type is
        /// registered so the schema stays current.
        /// </summary>
        private static void RebuildBinarySchemaXml(
            BaseDataVariableState dictionary,
            string namespaceUri,
            ushort namespaceIndex,
            UANodeManager nodeManager)
        {
            var structuredTypes = new StringBuilder();

            // Walk all HasComponent references to find DataTypeDescriptionState children
            var refs = new List<IReference>();
            dictionary.GetReferences(nodeManager.SystemContext, refs, ReferenceTypeIds.HasComponent, false);

            foreach (var r in refs)
            {
                var targetId = ExpandedNodeId.ToNodeId(r.TargetId, nodeManager.Server.NamespaceUris);
                var targetNode = nodeManager.Find(targetId);

                // Only process description nodes (skip the NamespaceUri property etc.)
                if (targetNode is BaseDataVariableState descNode
                    && descNode.TypeDefinitionId == VariableTypeIds.DataTypeDescriptionType)
                {
                    string typeName = descNode.BrowseName.Name;
                    NodeId dtNodeId = new(typeName + "Type", namespaceIndex);
                    var dtNode = nodeManager.Find(dtNodeId) as DataTypeState;

                    if (dtNode?.DataTypeDefinition?.Body is StructureDefinition sd)
                    {
                        structuredTypes.AppendLine($"  <opc:StructuredType Name=\"{typeName}\" BaseType=\"ua:ExtensionObject\">");

                        foreach (StructureField field in sd.Fields)
                        {
                            string bsdType = MapNodeIdToBsdTypeName(field.DataType, namespaceIndex, nodeManager);
                            structuredTypes.AppendLine($"    <opc:Field Name=\"{field.Name}\" TypeName=\"{bsdType}\" />");
                        }

                        structuredTypes.AppendLine("  </opc:StructuredType>");
                    }
                }
            }

            string schemaXml =
                "<opc:TypeDictionary" +
                " xmlns:opc=\"http://opcfoundation.org/BinarySchema/\"" +
                " xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"" +
                " xmlns:ua=\"http://opcfoundation.org/UA/\"" +
                $" xmlns:tns=\"{namespaceUri}\"" +
                " DefaultByteOrder=\"LittleEndian\"" +
                $" TargetNamespace=\"{namespaceUri}\">" +
                Environment.NewLine +
                "  <opc:Import Namespace=\"http://opcfoundation.org/UA/\" Location=\"Opc.Ua.BinarySchema.bsd\"/>" +
                Environment.NewLine +
                structuredTypes.ToString() +
                "</opc:TypeDictionary>";

            dictionary.Value = Encoding.UTF8.GetBytes(schemaXml);
        }

        /// <summary>
        /// Maps an OPC UA DataType NodeId to a BSD type name.
        /// Built-in types → "opc:X", custom types in our namespace → "tns:X".
        /// </summary>
        private static string MapNodeIdToBsdTypeName(
            NodeId dataType,
            ushort namespaceIndex,
            UANodeManager nodeManager)
        {
            if (dataType.NamespaceIndex == 0 && dataType.IdType == IdType.Numeric)
            {
                return (uint)dataType.Identifier switch
                {
                    DataTypes.Boolean => "opc:Boolean",
                    DataTypes.SByte   => "opc:SByte",
                    DataTypes.Byte    => "opc:Byte",
                    DataTypes.Int16   => "opc:Int16",
                    DataTypes.UInt16  => "opc:UInt16",
                    DataTypes.Int32   => "opc:Int32",
                    DataTypes.UInt32  => "opc:UInt32",
                    DataTypes.Int64   => "opc:Int64",
                    DataTypes.UInt64  => "opc:UInt64",
                    DataTypes.Float   => "opc:Float",
                    DataTypes.Double  => "opc:Double",
                    DataTypes.String  => "opc:String",
                    _                 => "opc:Variant"
                };
            }

            if (dataType.NamespaceIndex == namespaceIndex)
            {
                var customType = nodeManager.Find(dataType);
                if (customType != null)
                {
                    return "tns:" + customType.BrowseName.Name;
                }
            }

            return "opc:Variant";
        }

        private EIPTypeString ParseDataType(ushort eipDataTypeId)
        {
            // Data Type:   Tag Type Value:     Size of Transmitted Data:
            // BOOL         0x0nC1              1 byte
            // The BOOL value includes an additional field (n) for specifying the bit position within the SINT (n = 0 - 7).
            if ((eipDataTypeId & 0xC1) == 0xC1)
            {
                return EIPTypeString.BOOL;
            }

            switch (eipDataTypeId)
            {
                case 0xC2: return EIPTypeString.SINT;
                case 0xC3: return EIPTypeString.INT;
                case 0xC4: return EIPTypeString.DINT;
                case 0xC5: return EIPTypeString.LINT;
                case 0xC6: return EIPTypeString.USINT;
                case 0xC7: return EIPTypeString.UINT;
                case 0xC8: return EIPTypeString.UDINT;
                case 0xC9: return EIPTypeString.ULINT;
                case 0xCA: return EIPTypeString.REAL;
                case 0xCB: return EIPTypeString.LREAL;
                default: return EIPTypeString.REAL;
            }
        }

        private TagInfo DecodeOneTagInfo(Tag tag, int offset, out int elementSize)
        {
            var tagInstanceId = tag.GetUInt32(offset);
            var tagType = tag.GetUInt16(offset + 4);
            var tagLength = tag.GetUInt16(offset + 6);
            var tagArrayDims = new uint[]
            {
                tag.GetUInt32(offset + 8),
                tag.GetUInt32(offset + 12),
                tag.GetUInt32(offset + 16)
            };

            var apparentTagNameLength = (int)tag.GetUInt16(offset + 20);
            var actualTagNameLength = Math.Min(apparentTagNameLength, 200 * 2 - 1);

            var tagNameBytes = Enumerable.Range(offset + 22, actualTagNameLength)
                .Select(o => tag.GetUInt8(o))
                .Select(Convert.ToByte)
                .ToArray();

            var tagName = Encoding.ASCII.GetString(tagNameBytes);

            elementSize = 22 + actualTagNameLength;

            return new TagInfo()
            {
                Id = tagInstanceId,
                Type = tagType,
                Name = tagName,
                Length = tagLength,
                Dimensions = tagArrayDims
            };
        }

        private TagInfo[] DecodeAllTags(Tag tag)
        {
            var buffer = new List<TagInfo>();

            var tagSize = tag.GetSize();

            var offset = 0;
            while (offset < tagSize)
            {
                buffer.Add(DecodeOneTagInfo(tag, offset, out var elementSize));
                offset += elementSize;
            }

            return buffer.ToArray();
        }

        private UdtInfo DecodeUdtInfo(Tag tag)
        {
            var template_id = tag.GetUInt16(0);
            var member_desc_size = tag.GetUInt32(2);
            var udt_instance_size = tag.GetUInt32(6);
            var num_members = tag.GetUInt16(10);
            var struct_handle = tag.GetUInt16(12);

            var udtInfo = new UdtInfo()
            {
                Fields = new UdtFieldInfo[num_members],
                NumFields = num_members,
                Handle = struct_handle,
                Id = template_id,
                Size = udt_instance_size
            };

            var offset = 14;

            for (var field_index = 0; field_index < num_members; field_index++)
            {
                var field_metadata = tag.GetUInt16(offset);
                offset += 2;

                var field_element_type = tag.GetUInt16(offset);
                offset += 2;

                var field_offset = tag.GetUInt16(offset);
                offset += 4;

                var field = new UdtFieldInfo()
                {
                    Offset = field_offset,
                    Metadata = field_metadata,
                    Type = field_element_type,
                };

                udtInfo.Fields[field_index] = field;
            }

            var name_str = tag.GetString(offset).Split(';')[0];
            udtInfo.Name = name_str;

            offset += tag.GetStringTotalLength(offset);

            for (var field_index = 0; field_index < num_members; field_index++)
            {
                udtInfo.Fields[field_index].Name = tag.GetString(offset);
                offset += tag.GetStringTotalLength(offset);
            }

            return udtInfo;
        }

        private bool TagIsUdt(TagInfo tag)
        {
            const ushort TYPE_IS_STRUCT = 0x8000;
            const ushort TYPE_IS_SYSTEM = 0x1000;

            return (tag.Type & TYPE_IS_STRUCT) != 0 && !((tag.Type & TYPE_IS_SYSTEM) != 0);
        }

        private int GetUdtId(TagInfo tag)
        {
            const ushort TYPE_UDT_ID_MASK = 0x0FFF;
            return tag.Type & TYPE_UDT_ID_MASK;
        }

        private bool IsTag(TagInfo tag)
        {
            if (tag.Name.StartsWith("Program:"))
            {
                return false;
            }

            if (tag.Name.StartsWith("Task:"))
            {
                return false;
            }

            if (tag.Name.StartsWith("Map:"))
            {
                return false;
            }

            return true;
        }

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
        {
            unitId = 1;

            string[] address = td.Base.Split([':', '/']);
            if ((address.Length != 4) || (address[0] != "eip"))
            {
                throw new Exception("Expected Rockwell PLC address in the format eip://ipaddress!");
            }

            RockwellAsset asset = new();
            asset.Connect(address[3], 0);

            _assets[td.Name] = asset;

            return asset;
        }

        public AssetTag CreateTag(
            ThingDescription td,
            object form,
            string assetId,
            byte unitId,
            string variableId,
            string mappedUAExpandedNodeId,
            string mappedUAFieldPath)
        {
            EIPForm eipForm = JsonConvert.DeserializeObject<EIPForm>(form.ToString());

            // Register the UDT structure definition on the asset so
            // Read() can decode the raw PLC bytes at EIP field offsets.
            if ((eipForm.StructureDefinition != null) && _assets.TryGetValue(td.Name, out var asset))
            {
                asset.RegisterUdtDefinition(eipForm.Href, eipForm.StructureDefinition);
            }

            return new AssetTag()
            {
                Name = variableId,
                Address = eipForm.Href,
                UnitID = unitId,
                Type = eipForm.StructureDefinition != null ? "UDT" : eipForm.Type.ToString(),
                PollingInterval = (int)eipForm.PollingTime,
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }
    }
}
