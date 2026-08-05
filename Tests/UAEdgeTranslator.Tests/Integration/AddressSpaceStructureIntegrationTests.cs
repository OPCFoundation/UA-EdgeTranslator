namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua.Edge.Translator.Diagnostics;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Covers how the Address Space Explorer renders complex (structure / UDT)
    /// values.
    /// <para>
    /// The translator stores structures as an <see cref="ExtensionObject"/>
    /// whose body is a binary-encoded <c>byte[]</c> (see
    /// <c>UANodeManager.EncodeField</c>). Without decoding, such a value shows
    /// up in the UI as an opaque byte string. These tests build a real
    /// structure DataType plus an instance in the live server and assert the
    /// Explorer decomposes it into named fields.
    /// </para>
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class AddressSpaceStructureIntegrationTests : IAsyncLifetime
    {
        private const string _structTypeName = "ExplorerTestStructType";

        private OpcUaServerFixture _fixture;
        private AddressSpaceService _service;
        private NodeId _variableNodeId;

        public Task InitializeAsync()
        {
            _fixture = new OpcUaServerFixture();
            _service = new AddressSpaceService();

            SeedStructureNode();

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

        /// <summary>
        /// Creates a DataType node carrying a StructureDefinition and a Variable
        /// holding a binary-encoded instance of it, mirroring exactly what
        /// UANodeManager produces for a UDT tag.
        /// </summary>
        private void SeedStructureNode()
        {
            UANodeManager manager = UANodeManager.Instance;
            ushort ns = (ushort)manager.Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/");

            StructureDefinition definition = new()
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField { Name = "Speed", DataType = DataTypeIds.Float, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Running", DataType = DataTypeIds.Boolean, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Count", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Label", DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar }
                ]
            };

            NodeId dataTypeId = new(_structTypeName, ns);

            DataTypeState dataType = new()
            {
                NodeId = dataTypeId,
                BrowseName = new QualifiedName(_structTypeName, ns),
                DisplayName = _structTypeName,
                SuperTypeId = DataTypeIds.Structure,
                DataTypeDefinition = new ExtensionObject(definition)
            };

            manager.AddPredefinedNodePublic(dataType);

            // Encode an instance the same way UANodeManager.EncodeField does:
            // positional binary encoding, fields in declaration order.
            using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry)
            {
                NamespaceUris = manager.Server.NamespaceUris,
                Factory = manager.Server.Factory
            });

            encoder.WriteFloat("Speed", 12.5f);
            encoder.WriteBoolean("Running", true);
            encoder.WriteInt32("Count", 42);
            encoder.WriteString("Label", "pump-1");

            _variableNodeId = new NodeId("ExplorerTestStructValue", ns);

            BaseDataVariableState variable = new(null)
            {
                NodeId = _variableNodeId,
                BrowseName = new QualifiedName("ExplorerTestStructValue", ns),
                DisplayName = "ExplorerTestStructValue",
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = dataTypeId,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = new ExtensionObject(dataTypeId, encoder.CloseAndReturnBuffer())
            };

            manager.AddPredefinedNodePublic(variable);
        }

        [Fact]
        public async Task Structure_value_is_decomposed_into_named_fields()
        {
            NodeDetail detail = await _service.GetNodeDetailAsync(_variableNodeId.ToString()).ConfigureAwait(false);

            Assert.Null(detail.Error);

            // The raw byte-string rendering must be gone.
            string value = detail.ValueAttributes.First(a => a.Name == "Value").Value;
            Assert.DoesNotContain("byte[", value, System.StringComparison.Ordinal);

            // Each field decoded with its declared name and value.
            Assert.Equal(4, detail.Fields.Count);
            Assert.Equal("Speed", detail.Fields[0].Name);
            Assert.Equal("12.5", detail.Fields[0].Value);
            Assert.Equal("Running", detail.Fields[1].Name);
            Assert.Equal("True", detail.Fields[1].Value);
            Assert.Equal("Count", detail.Fields[2].Name);
            Assert.Equal("42", detail.Fields[2].Value);
            Assert.Equal("Label", detail.Fields[3].Name);
            Assert.Equal("pump-1", detail.Fields[3].Value);
        }

        [Fact]
        public async Task Structure_field_types_are_reported()
        {
            NodeDetail detail = await _service.GetNodeDetailAsync(_variableNodeId.ToString()).ConfigureAwait(false);

            Assert.Equal("Float", detail.Fields[0].DataType);
            Assert.Equal("Boolean", detail.Fields[1].DataType);
            Assert.Equal("Int32", detail.Fields[2].DataType);
            Assert.Equal("String", detail.Fields[3].DataType);
        }

        [Fact]
        public async Task Structure_value_renders_a_readable_inline_summary()
        {
            // The tree shows a one-line summary rather than a byte string.
            IReadOnlyDictionary<string, string> values =
                await _service.ReadValuesAsync([_variableNodeId.ToString()]).ConfigureAwait(false);

            string summary = Assert.Single(values).Value;

            Assert.DoesNotContain("byte[", summary, System.StringComparison.Ordinal);
            Assert.Contains("Speed", summary, System.StringComparison.Ordinal);
            Assert.Contains("12.5", summary, System.StringComparison.Ordinal);
        }

        [Fact]
        public async Task Structure_node_does_not_report_a_bad_attribute_status()
        {
            NodeDetail detail = await _service.GetNodeDetailAsync(_variableNodeId.ToString()).ConfigureAwait(false);

            string status = detail.ValueAttributes.First(a => a.Name == "Status").Value;

            Assert.DoesNotContain("BadAttributeIdInvalid", status, System.StringComparison.Ordinal);
            Assert.Equal(nameof(NodeClass.Variable), detail.NodeClass);
        }

        [Fact]
        public async Task Structure_with_an_unsupported_field_type_degrades_gracefully()
        {
            // UANodeManager.EncodeField throws NotImplementedException for field
            // types outside its switch, so such a structure can never be encoded
            // correctly. The Explorer must still not throw, and must not present
            // misaligned values decoded past the unknown field.
            UANodeManager manager = UANodeManager.Instance;
            ushort ns = (ushort)manager.Server.NamespaceUris.GetIndex("http://opcfoundation.org/UA/EdgeTranslator/");

            StructureDefinition definition = new()
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields =
                [
                    new StructureField { Name = "Ok", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Weird", DataType = DataTypeIds.Guid, ValueRank = ValueRanks.Scalar },
                    new StructureField { Name = "Never", DataType = DataTypeIds.Int32, ValueRank = ValueRanks.Scalar }
                ]
            };

            NodeId dataTypeId = new("UnsupportedFieldStructType", ns);

            manager.AddPredefinedNodePublic(new DataTypeState
            {
                NodeId = dataTypeId,
                BrowseName = new QualifiedName("UnsupportedFieldStructType", ns),
                DisplayName = "UnsupportedFieldStructType",
                SuperTypeId = DataTypeIds.Structure,
                DataTypeDefinition = new ExtensionObject(definition)
            });

            using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry)
            {
                NamespaceUris = manager.Server.NamespaceUris,
                Factory = manager.Server.Factory
            });

            encoder.WriteInt32("Ok", 7);

            NodeId nodeId = new("UnsupportedFieldStructValue", ns);

            manager.AddPredefinedNodePublic(new BaseDataVariableState(null)
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName("UnsupportedFieldStructValue", ns),
                DisplayName = "UnsupportedFieldStructValue",
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = dataTypeId,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = new ExtensionObject(dataTypeId, encoder.CloseAndReturnBuffer())
            });

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);

            Assert.Null(detail.Error);

            // The supported prefix decodes; decoding stops at the unknown field
            // rather than emitting garbage for it and everything after it.
            Assert.Equal(2, detail.Fields.Count);
            Assert.Equal("Ok", detail.Fields[0].Name);
            Assert.Equal("7", detail.Fields[0].Value);
            Assert.Equal("Weird", detail.Fields[1].Name);
            Assert.Equal("(unsupported type)", detail.Fields[1].Value);
        }

        [Fact]
        public async Task Non_structure_values_are_unaffected_by_decoding()
        {
            // Guards the interleaved Value/DataType batch read: a scalar must
            // still resolve to its own value, not its neighbour's.
            IReadOnlyDictionary<string, string> values = await _service.ReadValuesAsync(
            [
                VariableIds.Server_ServerStatus_State.ToString(),
                _variableNodeId.ToString()
            ]).ConfigureAwait(false);

            Assert.Equal(2, values.Count);

            // The structure decodes to a field summary...
            Assert.Contains("Speed", values[_variableNodeId.ToString()], System.StringComparison.Ordinal);

            // ...and the scalar is untouched and correctly aligned.
            string state = values[VariableIds.Server_ServerStatus_State.ToString()];
            Assert.DoesNotContain("Speed", state, System.StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(state));
        }
    }
}
