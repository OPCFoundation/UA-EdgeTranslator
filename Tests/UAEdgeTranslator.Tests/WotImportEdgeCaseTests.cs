namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.WotCon;
    using System.Reflection;
    using System.Text;
    using Xunit;

    /// <summary>
    /// Edge cases of importing Thing Descriptions: documents that are not JSON
    /// objects, byte order marks, and the type helpers used to shape array variables.
    /// </summary>
    public sealed class WotImportEdgeCaseTests
    {
        [Fact]
        public void A_byte_order_mark_is_stripped_before_parsing()
        {
            byte[] withBom = Encoding.UTF8.GetPreamble();
            byte[] json = Encoding.UTF8.GetBytes("{\"title\":\"plc\"}");
            byte[] content = new byte[withBom.Length + json.Length];
            withBom.CopyTo(content, 0);
            json.CopyTo(content, withBom.Length);

            byte[] normalized = WotImportNormalizer.Normalize(content);

            Assert.NotEqual(0xEF, normalized[0]);
            Assert.Contains("\"title\"", Encoding.UTF8.GetString(normalized));
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("null")]
        [InlineData("[1,2]")]
        public void Content_that_is_not_a_json_object_is_left_for_the_registry_to_report(string text)
        {
            byte[] content = Encoding.UTF8.GetBytes(text);

            Assert.Equal(content, WotImportNormalizer.Normalize(content));
            Assert.Empty(WotImportNormalizer.MappingTypeNamespaces(content));
        }

        [Fact]
        public void A_document_without_properties_maps_no_type_namespaces()
        {
            Assert.Empty(WotImportNormalizer.MappingTypeNamespaces(Encoding.UTF8.GetBytes("{\"title\":\"plc\"}")));
        }
        [Theory]
        [InlineData(TypeEnum.Boolean, DataTypes.Boolean)]
        [InlineData(TypeEnum.Integer, DataTypes.Integer)]
        [InlineData(TypeEnum.Number, DataTypes.Number)]
        [InlineData(TypeEnum.String, DataTypes.String)]
        [InlineData(TypeEnum.Object, DataTypes.BaseDataType)]
        public void Array_elements_take_the_data_type_of_their_item_schema(TypeEnum itemType, uint dataType)
        {
            MethodInfo elementDataType = typeof(CreatingTargetVariableResolver).Assembly
                .GetType("Opc.Ua.Edge.Translator.WotCon.ObjectTypeMappingRuntimeFactory", throwOnError: true)
                .GetMethod("ElementDataType", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.Equal(new NodeId(dataType), elementDataType.Invoke(null, new object[] { itemType }));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("  ")]
        [InlineData("not a node id ;;")]
        public void Missing_or_unreadable_node_ids_resolve_to_null(string text)
        {
            MethodInfo toNodeId = typeof(CreatingTargetVariableResolver).Assembly
                .GetType("Opc.Ua.Edge.Translator.WotCon.ObjectTypeMappingRuntimeFactory", throwOnError: true)
                .GetMethod("ToNodeId", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.Equal(NodeId.Null, toNodeId.Invoke(null, new object[] { null, text }));
        }

        [Fact]
        public void A_creating_resolver_can_be_built_without_a_type_model()
        {
            CreatingTargetVariableResolver resolver = new();

            Assert.NotNull(resolver);
        }
    }
}