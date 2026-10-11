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
        public async Task A_single_field_update_with_a_mismatched_type_resets_the_field()
        {
            const string tagName = "mismatch";
            NodeId nodeId = SeedStructure(tagName, [(DataTypes.Int32, "Value", 5), (DataTypes.Double, "Other", 3.5d)]);

            // A driver handing over the wrong CLR type must not corrupt the
            // structure; the field falls back to its default.
            Update(new AssetTag { Name = tagName, MappedUAFieldPath = "Value" }, "not-an-int", connected: true);

            NodeDetail detail = await _service.GetNodeDetailAsync(nodeId.ToString()).ConfigureAwait(false);
            Assert.Equal("0", detail.Fields[0].Value);
            Assert.Equal("3.5", detail.Fields[1].Value);
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
