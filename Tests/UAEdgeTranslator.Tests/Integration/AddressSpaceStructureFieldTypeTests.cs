namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua.Edge.Translator.Diagnostics;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Covers the structure (UDT) decoding paths of
    /// <see cref="AddressSpaceService"/> field type by field type.
    /// <para>
    /// The decoder mirrors <c>UANodeManager.EncodeField</c> and is positional:
    /// each field is read at an offset determined by every field before it, so
    /// a single wrong reader corrupts everything that follows. Exercising every
    /// supported type is therefore about correctness, not just coverage.
    /// </para>
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class AddressSpaceStructureFieldTypeTests : IAsyncLifetime
    {
        private OpcUaServerFixture _fixture;
        private AddressSpaceService _service;
        private ushort _namespaceIndex;

        public Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();
            _service = new AddressSpaceService();
            _namespaceIndex = (ushort)UANodeManager.Instance.Server.NamespaceUris.GetIndex(
                "http://opcfoundation.org/UA/EdgeTranslator/");

            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            if (_fixture != null)
            {
                await _fixture.DisposeAsync().ConfigureAwait(false);
                _fixture = null;
            }
        }

        [Fact]
        public async Task Every_supported_field_type_round_trips_through_the_decoder()
        {
            // One structure containing every type EncodeField supports. Because
            // the encoding is positional, decoding the last field correctly
            // proves every reader before it consumed exactly the right number
            // of bytes.
            (uint DataType, string Name, object Value, string Expected)[] fields =
            [
                (DataTypes.Boolean, "AsBoolean", true, "True"),
                (DataTypes.SByte, "AsSByte", (sbyte)-8, "-8"),
                (DataTypes.Byte, "AsByte", (byte)200, "200"),
                (DataTypes.Int16, "AsInt16", (short)-1234, "-1234"),
                (DataTypes.UInt16, "AsUInt16", (ushort)61234, "61234"),
                (DataTypes.Int32, "AsInt32", -123456, "-123456"),
                (DataTypes.UInt32, "AsUInt32", 4000000000u, "4000000000"),
                (DataTypes.Int64, "AsInt64", -1234567890123L, "-1234567890123"),
                (DataTypes.UInt64, "AsUInt64", 12345678901234567890UL, "12345678901234567890"),
                (DataTypes.Float, "AsFloat", 1.25f, "1.25"),
                (DataTypes.Double, "AsDouble", -2.5d, "-2.5"),
                (DataTypes.String, "AsString", "the-last-field", "the-last-field")
            ];

            NodeId nodeId = SeedStructure("AllFieldTypes", fields);

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);

            Assert.Null(detail.Error);
            Assert.Equal(fields.Length, detail.Fields.Count);

            for (int i = 0; i < fields.Length; i++)
            {
                Assert.Equal(fields[i].Name, detail.Fields[i].Name);
                Assert.Equal(fields[i].Expected, detail.Fields[i].Value);
            }
        }

        [Fact]
        public async Task Numeric_fields_are_formatted_invariantly()
        {
            // Guards against a locale-dependent decimal separator leaking into
            // the UI on non-English hosts.
            NodeId nodeId = SeedStructure("InvariantFormatting",
            [
                (DataTypes.Double, "Value", 1234.5d, "1234.5")
            ]);

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);

            Assert.Equal("1234.5", Assert.Single(detail.Fields).Value);
        }

        [Fact]
        public async Task Empty_string_field_decodes_to_an_empty_value()
        {
            NodeId nodeId = SeedStructure("EmptyString",
            [
                (DataTypes.String, "Empty", string.Empty, string.Empty),
                (DataTypes.Int32, "After", 7, "7")
            ]);

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);

            Assert.Equal(2, detail.Fields.Count);
            Assert.Equal(string.Empty, detail.Fields[0].Value);

            // The field after an empty string must still be aligned.
            Assert.Equal("7", detail.Fields[1].Value);
        }

        [Fact]
        public async Task Structure_summary_truncates_a_wide_structure()
        {
            // The inline tree summary caps the number of fields it renders so a
            // wide UDT cannot blow out the row.
            (uint, string, object, string)[] many = Enumerable.Range(0, 10)
                .Select(i => (DataTypes.Int32, "F" + i, (object)i, i.ToString()))
                .ToArray();

            NodeId nodeId = SeedStructure("WideStruct", many);

            IReadOnlyDictionary<string, string> values =
                await _service.ReadValuesAsync([nodeId.ToString()]).ConfigureAwait(false);

            string summary = Assert.Single(values).Value;

            Assert.Contains("10 fields", summary, System.StringComparison.Ordinal);
            Assert.DoesNotContain("F9 =", summary, System.StringComparison.Ordinal);
        }

        [Fact]
        public async Task Structure_with_no_fields_falls_back_to_the_raw_value()
        {
            // A StructureDefinition with an empty field list cannot be decoded;
            // the service must fall back rather than report zero fields as a
            // successful decode.
            NodeId nodeId = SeedStructure("NoFields", []);

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);

            Assert.Empty(detail.Fields);
            Assert.NotEmpty(detail.ValueAttributes);
        }

        /// <summary>
        /// Creates a DataType carrying a StructureDefinition plus a Variable
        /// holding a binary-encoded instance, exactly as UANodeManager does.
        /// </summary>
        private NodeId SeedStructure(string typeName, (uint DataType, string Name, object Value, string Expected)[] fields)
        {
            UANodeManager manager = UANodeManager.Instance;

            StructureDefinition definition = new()
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = new StructureFieldCollection(fields.Select(f => new StructureField
                {
                    Name = f.Name,
                    DataType = new NodeId(f.DataType),
                    ValueRank = ValueRanks.Scalar
                }))
            };

            NodeId dataTypeId = new(typeName + "Type", _namespaceIndex);

            manager.AddPredefinedNodePublic(new DataTypeState
            {
                NodeId = dataTypeId,
                BrowseName = new QualifiedName(typeName + "Type", _namespaceIndex),
                DisplayName = typeName + "Type",
                SuperTypeId = DataTypeIds.Structure,
                DataTypeDefinition = new ExtensionObject(definition)
            });

            using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry)
            {
                NamespaceUris = manager.Server.NamespaceUris,
                Factory = manager.Server.Factory
            });

            foreach ((uint dataType, string name, object value, _) in fields)
            {
                switch (dataType)
                {
                    case DataTypes.Boolean: encoder.WriteBoolean(name, (bool)value); break;
                    case DataTypes.SByte: encoder.WriteSByte(name, (sbyte)value); break;
                    case DataTypes.Byte: encoder.WriteByte(name, (byte)value); break;
                    case DataTypes.Int16: encoder.WriteInt16(name, (short)value); break;
                    case DataTypes.UInt16: encoder.WriteUInt16(name, (ushort)value); break;
                    case DataTypes.Int32: encoder.WriteInt32(name, (int)value); break;
                    case DataTypes.UInt32: encoder.WriteUInt32(name, (uint)value); break;
                    case DataTypes.Int64: encoder.WriteInt64(name, (long)value); break;
                    case DataTypes.UInt64: encoder.WriteUInt64(name, (ulong)value); break;
                    case DataTypes.Float: encoder.WriteFloat(name, (float)value); break;
                    case DataTypes.Double: encoder.WriteDouble(name, (double)value); break;
                    case DataTypes.String: encoder.WriteString(name, (string)value); break;
                    default: throw new System.NotSupportedException(dataType.ToString());
                }
            }

            NodeId nodeId = new(typeName + "Value", _namespaceIndex);

            manager.AddPredefinedNodePublic(new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName(typeName + "Value", _namespaceIndex),
                DisplayName = typeName + "Value",
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = dataTypeId,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = new ExtensionObject(dataTypeId, encoder.CloseAndReturnBuffer())
            });

            return nodeId;
        }
    }
}
