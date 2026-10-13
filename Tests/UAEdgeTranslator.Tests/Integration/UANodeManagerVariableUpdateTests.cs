namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua.Edge.Translator.Diagnostics;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Covers the southbound-to-address-space write path,
    /// <c>UANodeManager.UpdateUAServerVariable</c>, for structure (UDT) and
    /// scalar values. Each test seeds a variable, pushes a value the way a
    /// driver poll would, then decodes the result through
    /// <see cref="AddressSpaceService"/> so the encoder and decoder are checked
    /// against each other.
    /// </summary>
    [Collection(WorkingDirectoryCollection.Name)]
    public sealed class UANodeManagerVariableUpdateTests : IAsyncLifetime
    {
        private const BindingFlags _privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly (uint DataType, string Name, object Initial)[] _allFields =
        [
            (DataTypes.Boolean, "AsBoolean", false),
            (DataTypes.SByte, "AsSByte", (sbyte)1),
            (DataTypes.Byte, "AsByte", (byte)1),
            (DataTypes.Int16, "AsInt16", (short)1),
            (DataTypes.UInt16, "AsUInt16", (ushort)1),
            (DataTypes.Int32, "AsInt32", 1),
            (DataTypes.UInt32, "AsUInt32", 1u),
            (DataTypes.Int64, "AsInt64", 1L),
            (DataTypes.UInt64, "AsUInt64", 1UL),
            (DataTypes.Float, "AsFloat", 1f),
            (DataTypes.Double, "AsDouble", 1d),
            (DataTypes.String, "AsString", "initial")
        ];

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

        public static TheoryData<string, object, string> SingleFieldUpdates => new()
        {
            { "AsBoolean", true, "True" },
            { "AsSByte", (sbyte)-8, "-8" },
            { "AsByte", (byte)200, "200" },
            { "AsInt16", (short)-1234, "-1234" },
            { "AsUInt16", (ushort)61234, "61234" },
            { "AsInt32", -123456, "-123456" },
            { "AsUInt32", 4000000000u, "4000000000" },
            { "AsInt64", -1234567890123L, "-1234567890123" },
            { "AsUInt64", 12345678901234567890UL, "12345678901234567890" },
            { "AsFloat", 1.25f, "1.25" },
            { "AsDouble", -2.5d, "-2.5" },
            { "AsString", "updated", "updated" }
        };

        [Theory]
        [MemberData(nameof(SingleFieldUpdates))]
        public async Task A_single_field_update_changes_only_that_field(string fieldName, object value, string expected)
        {
            string tagName = "single-" + fieldName;
            NodeId nodeId = SeedStructure(tagName, _allFields);

            Update(new AssetTag { Name = tagName, MappedUAFieldPath = fieldName }, value, connected: true);

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);
            Assert.Null(detail.Error);

            foreach ((uint _, string name, object initial) in _allFields)
            {
                string actual = detail.Fields.Single(f => f.Name == name).Value;
                if (name == fieldName)
                {
                    Assert.Equal(expected, actual);
                }
                else
                {
                    // Untouched fields keep their decoded initial values, which
                    // also proves the positional re-encode stayed aligned.
                    Assert.Equal(Format(initial), actual);
                }
            }
        }

        [Fact]
        public async Task A_single_field_update_with_an_unconvertible_value_keeps_the_previous_value()
        {
            const string tagName = "mismatch";
            NodeId nodeId = SeedStructure(tagName, [(DataTypes.Int32, "Value", 5), (DataTypes.Double, "Other", 3.5d)]);

            // A value that cannot be represented must not overwrite good data
            // with a fake zero, nor disturb the other fields.
            Update(new AssetTag { Name = tagName, MappedUAFieldPath = "Value" }, "not-an-int", connected: true);

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);
            Assert.Equal("5", detail.Fields[0].Value);
            Assert.Equal("3.5", detail.Fields[1].Value);
        }

        [Fact]
        public async Task A_single_field_update_with_an_out_of_range_value_keeps_the_previous_value()
        {
            const string tagName = "overflow";
            NodeId nodeId = SeedStructure(tagName, [(DataTypes.Byte, "Small", (byte)7)]);

            Update(new AssetTag { Name = tagName, MappedUAFieldPath = "Small" }, 1000, connected: true);

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);
            Assert.Equal("7", Assert.Single(detail.Fields).Value);
        }

        public static TheoryData<string, uint, object, object, string> NeighbouringNumericTypes => new()
        {
            // Drivers commonly hand back a neighbouring numeric type; these used
            // to be written as 0.
            { "float-into-double", DataTypes.Double, 0d, 2.5f, "2.5" },
            { "int-into-short", DataTypes.Int16, (short)0, 1234, "1234" },
            { "int-into-long", DataTypes.Int64, 0L, 42, "42" },
            { "double-into-float", DataTypes.Float, 0f, 1.25d, "1.25" },
            { "short-into-int", DataTypes.Int32, 0, (short)-9, "-9" },
            { "string-into-int", DataTypes.Int32, 0, "77", "77" },
            { "int-into-bool", DataTypes.Boolean, false, 1, "True" }
        };

        [Theory]
        [MemberData(nameof(NeighbouringNumericTypes))]
        public async Task A_single_field_update_converts_neighbouring_types(string tagName, uint dataType, object initial, object value, string expected)
        {
            NodeId nodeId = SeedStructure(tagName, [(dataType, "Field", initial)]);

            Update(new AssetTag { Name = tagName, MappedUAFieldPath = "Field" }, value, connected: true);

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);
            Assert.Equal(expected, Assert.Single(detail.Fields).Value);
        }

        [Fact]
        public async Task A_whole_structure_update_converts_neighbouring_types()
        {
            const string tagName = "whole-convert";
            NodeId nodeId = SeedStructure(tagName, [(DataTypes.Double, "Level", 0d), (DataTypes.Int16, "Count", (short)0)]);

            Dictionary<string, object> values = new()
            {
                ["Level"] = 4.5f,
                ["Count"] = 12
            };

            Update(new AssetTag { Name = tagName }, values, connected: true);

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);
            Assert.Equal(["4.5", "12"], detail.Fields.Select(f => f.Value));
        }

        [Fact]
        public async Task A_whole_structure_update_encodes_every_supplied_field()
        {
            const string tagName = "whole";
            NodeId nodeId = SeedStructure(tagName, [(DataTypes.Int32, "Count", 1), (DataTypes.Double, "Level", 1d), (DataTypes.String, "Label", "a")]);

            Dictionary<string, object> values = new()
            {
                ["Count"] = 42,
                ["Level"] = 9.75d,
                ["Label"] = "tank-7"
            };

            Update(new AssetTag { Name = tagName }, values, connected: true);

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);
            Assert.Equal(["42", "9.75", "tank-7"], detail.Fields.Select(f => f.Value));
        }

        [Fact]
        public async Task A_whole_structure_update_ignores_a_non_dictionary_value()
        {
            const string tagName = "whole-ignored";
            NodeId nodeId = SeedStructure(tagName, [(DataTypes.Int32, "Count", 11)]);

            Update(new AssetTag { Name = tagName }, 999, connected: true);

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);
            Assert.Equal("11", Assert.Single(detail.Fields).Value);
        }

        [Fact]
        public void A_structure_variable_without_a_structure_definition_is_rejected()
        {
            const string tagName = "no-definition";
            NodeId dataTypeId = new("NoDefinitionType", _namespaceIndex);
            UANodeManager.Instance.AddPredefinedNodePublic(new DataTypeState
            {
                NodeId = dataTypeId,
                BrowseName = new QualifiedName("NoDefinitionType", _namespaceIndex),
                DisplayName = new LocalizedText("NoDefinitionType"),
                SuperTypeId = DataTypeIds.Structure
            });

            RegisterVariable(tagName, dataTypeId, Variant.From(new ExtensionObject(dataTypeId, new ByteString(new byte[] { 1, 2, 3, 4 }))));

            TargetInvocationException ex = Assert.Throws<TargetInvocationException>(
                () => Update(new AssetTag { Name = tagName, MappedUAFieldPath = "x" }, 1, connected: true));

            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        [Fact]
        public void Scalar_values_are_converted_and_status_reflects_connectivity()
        {
            BaseDataVariableState duration = RegisterVariable("duration", DataTypeIds.Duration, Variant.From(0d));
            BaseDataVariableState timestamp = RegisterVariable("timestamp", DataTypeIds.DateTime, Variant.From(DateTime.MinValue));
            BaseDataVariableState plain = RegisterVariable("plain", DataTypeIds.Int32, Variant.From(0));

            Update(new AssetTag { Name = "duration" }, TimeSpan.FromSeconds(1.5), connected: true);
            Update(new AssetTag { Name = "timestamp" }, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(2)), connected: true);
            Update(new AssetTag { Name = "plain" }, 17, connected: false);

            Assert.Equal(1500d, VariantConversion.ToClrValue(duration.Value));
            Assert.Equal(new DateTime(2026, 1, 2, 1, 4, 5, DateTimeKind.Utc), VariantConversion.ToClrValue(timestamp.Value));
            Assert.Equal(StatusCodes.Good, duration.StatusCode);

            Assert.Equal(17, VariantConversion.ToClrValue(plain.Value));
            Assert.Equal(StatusCodes.BadDataUnavailable, plain.StatusCode);
        }

        [Fact]
        public void An_unknown_tag_is_ignored()
        {
            Update(new AssetTag { Name = "does-not-exist" }, 1, connected: true);
        }

        [Fact]
        public async Task An_object_type_mapping_creates_a_correctly_typed_and_named_child()
        {
            // Mirrors a pac4200 mapping: EnergyMeasurementType from the PNEM
            // companion spec, with the tag mapped onto <MeasurementValue>.
            string nodesets = System.IO.Path.Combine(_fixture.WorkingDirectory, "nodesets");
            foreach (string name in new[] { "opcfoundation.org.UA.DI.nodeset2.xml", "opcfoundation.org.UA.PNEM.nodeset2.xml" })
            {
                string source = FindRepositoryFile(System.IO.Path.Combine("Tests", "UAEdgeTranslator.Tests", "TestData", "nodesets", name));
                System.IO.File.Copy(source, System.IO.Path.Combine(nodesets, name), overwrite: true);
            }

            const string td = """
                {
                  "@context": [ "https://www.w3.org/2022/wot/td/v1.1", { "uav": "http://opcfoundation.org/UA/PNEM/" } ],
                  "id": "urn:objtype",
                  "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                  "security": [ "nosec_sc" ],
                  "@type": [ "Thing" ],
                  "name": "objtype",
                  "base": "mock://127.0.0.1:1234/1",
                  "properties": {
                    "VoltageL1-L2": {
                      "type": "number",
                      "readOnly": true,
                      "uav:mapToNodeId": "s=VoltageL1-L2",
                      "uav:mapToType": "nsu=http://opcfoundation.org/UA/PNEM/;i=1006",
                      "uav:mapByFieldPath": "<MeasurementValue>",
                      "forms": [
                        { "href": "127.0.0.1/1?quantity=2", "op": [ "readproperty" ], "type": "xsd:float", "pollingTime": 1000 },
                        { "href": "127.0.0.1/1?quantity=2", "op": [ "observeproperty" ], "type": "xsd:float", "pollingTime": 1000 }
                      ]
                    }
                  }
                }
                """;

            await UANodeManager.Instance.ImportWoTFileAsync("objtype.jsonld", td).ConfigureAwait(false);

            FieldInfo field = typeof(UANodeManager).GetField("_uaVariables", _privateInstance);
            var variables = (ConcurrentDictionary<string, BaseDataVariableState>)field.GetValue(UANodeManager.Instance);
            BaseDataVariableState measurement = Assert.Single(variables, v => v.Key.StartsWith("objtype:", StringComparison.Ordinal)).Value;

            // Named after the declaration, without the parent prefix or brackets.
            Assert.Equal("MeasurementValue", measurement.DisplayName.Text);
            Assert.Equal("MeasurementValue", measurement.BrowseName.Name);

            // Typed like the declaration (Number), not like the HasComponent reference.
            Assert.Equal(DataTypeIds.Number, measurement.DataType);
            Assert.NotEqual(ReferenceTypeIds.HasComponent, measurement.DataType);

            // The instance also carries its mandatory PeObjectNumber property.
            NodeState instance = measurement.Parent;
            Assert.Equal("VoltageL1-L2", instance.DisplayName.Text);
            List<BaseInstanceState> children = [];
            instance.GetChildren(UANodeManager.Instance.SystemContext, children);
            Assert.Single(children, c => c.BrowseName.Name == "MeasurementValue");
            BaseInstanceState peObjectNumber = Assert.Single(children, c => c.BrowseName.Name == "PeObjectNumber");
            Assert.Equal(DataTypeIds.UInt16, ((BaseVariableState)peObjectNumber).DataType);
        }

        private static string FindRepositoryFile(string relativePath)
        {
            for (System.IO.DirectoryInfo dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, relativePath);
                if (System.IO.File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new System.IO.FileNotFoundException(relativePath);
        }

        [Fact]
        public async Task A_property_with_several_forms_creates_a_single_variable()
        {
            // One form per operation is normal in a Thing Description. Each form
            // used to create the variable again, which added a duplicate
            // HasWoTComponent reference and threw "Key already exists in dictionary".
            const string td = """
                {
                  "@context": [ "https://www.w3.org/2022/wot/td/v1.1" ],
                  "id": "urn:multiform",
                  "securityDefinitions": { "nosec_sc": { "scheme": "nosec" } },
                  "security": [ "nosec_sc" ],
                  "@type": [ "Thing" ],
                  "name": "multiform",
                  "title": "Multi Form Asset",
                  "base": "mock://127.0.0.1:1234/1",
                  "properties": {
                    "level": {
                      "type": "number",
                      "observable": true,
                      "forms": [
                        { "href": "127.0.0.1/1?quantity=2", "op": [ "readproperty" ], "type": "xsd:short", "pollingTime": 1000 },
                        { "href": "127.0.0.1/1?quantity=2", "op": [ "writeproperty" ], "type": "xsd:short" },
                        { "href": "127.0.0.1/1?quantity=2", "op": [ "observeproperty" ], "type": "xsd:short", "pollingTime": 1000 }
                      ]
                    }
                  }
                }
                """;

            await UANodeManager.Instance.ImportWoTFileAsync("multiform.jsonld", td).ConfigureAwait(false);

            FieldInfo field = typeof(UANodeManager).GetField("_uaVariables", _privateInstance);
            var variables = (ConcurrentDictionary<string, BaseDataVariableState>)field.GetValue(UANodeManager.Instance);
            BaseDataVariableState variable = Assert.Single(variables, v => v.Key.StartsWith("multiform:", StringComparison.Ordinal)).Value;

            // The import catches per-form failures, so assert on their side effect:
            // the old code added a duplicate child before the reference threw.
            NodeState asset = variable.Parent;
            List<BaseInstanceState> children = [];
            asset.GetChildren(UANodeManager.Instance.SystemContext, children);
            Assert.Single(children, c => c.NodeId == variable.NodeId);

            // ...and exactly one HasWoTComponent reference to it.
            List<IReference> references = [];
            asset.GetReferences(UANodeManager.Instance.SystemContext, references);
            Assert.Single(references, r => !r.IsInverse && r.TargetId == variable.NodeId && r.ReferenceTypeId != ReferenceTypeIds.Organizes && r.ReferenceTypeId != ReferenceTypeIds.HasComponent);
        }

        private static void Update(AssetTag tag, object value, bool connected)
        {
            MethodInfo method = typeof(UANodeManager).GetMethod("UpdateUAServerVariable", _privateInstance);
            Assert.NotNull(method);

            method.Invoke(UANodeManager.Instance, [tag, value, connected]);
        }

        private static string Format(object value) => value switch
        {
            bool b => b ? "True" : "False",
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture),
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            _ => value?.ToString()
        };

        private BaseDataVariableState RegisterVariable(string tagName, NodeId dataTypeId, Variant value)
        {
            BaseDataVariableState variable = new(null)
            {
                NodeId = new NodeId("update-" + tagName, _namespaceIndex),
                BrowseName = new QualifiedName("update-" + tagName, _namespaceIndex),
                DisplayName = new LocalizedText("update-" + tagName),
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                DataType = dataTypeId,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = value
            };

            UANodeManager.Instance.AddPredefinedNodePublic(variable);

            FieldInfo field = typeof(UANodeManager).GetField("_uaVariables", _privateInstance);
            Assert.NotNull(field);
            ((ConcurrentDictionary<string, BaseDataVariableState>)field.GetValue(UANodeManager.Instance))[tagName] = variable;

            return variable;
        }

        /// <summary>
        /// Creates a DataType carrying a StructureDefinition and a registered
        /// Variable holding a binary-encoded instance, as UANodeManager does.
        /// </summary>
        private NodeId SeedStructure(string tagName, (uint DataType, string Name, object Initial)[] fields)
        {
            UANodeManager manager = UANodeManager.Instance;
            string typeName = "Update" + tagName.Replace("-", string.Empty, StringComparison.Ordinal) + "Type";

            StructureDefinition definition = new()
            {
                BaseDataType = DataTypeIds.Structure,
                StructureType = StructureType.Structure,
                Fields = new ArrayOf<StructureField>(fields.Select(f => new StructureField
                {
                    Name = f.Name,
                    DataType = new NodeId(f.DataType),
                    ValueRank = ValueRanks.Scalar
                }).ToArray())
            };

            NodeId dataTypeId = new(typeName, _namespaceIndex);
            manager.AddPredefinedNodePublic(new DataTypeState
            {
                NodeId = dataTypeId,
                BrowseName = new QualifiedName(typeName, _namespaceIndex),
                DisplayName = new LocalizedText(typeName),
                SuperTypeId = DataTypeIds.Structure,
                DataTypeDefinition = new ExtensionObject(definition)
            });

            using BinaryEncoder encoder = new(new ServiceMessageContext(Program.Telemetry, manager.Server.Factory)
            {
                NamespaceUris = manager.Server.NamespaceUris
            });

            foreach ((uint dataType, string name, object value) in fields)
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
                    default: throw new NotSupportedException(dataType.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            BaseDataVariableState variable = RegisterVariable(
                tagName,
                dataTypeId,
                Variant.From(new ExtensionObject(dataTypeId, new ByteString(encoder.CloseAndReturnBuffer()))));

            return variable.NodeId;
        }
    }
}
