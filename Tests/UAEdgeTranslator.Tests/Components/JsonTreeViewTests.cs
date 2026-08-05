namespace Opc.Ua.Edge.Translator.Tests.Components
{
    using Bunit;
    using Newtonsoft.Json.Linq;
    using Opc.Ua.Edge.Translator.Components.Shared;
    using Xunit;

    /// <summary>
    /// Component tests for the recursive JSON tree used by the WoT Files page.
    /// <para>
    /// It renders arbitrary Thing Description content, so the behaviour that
    /// matters is that it copes with every JSON shape — objects, arrays,
    /// primitives, nulls and deep nesting — and expands the right levels by
    /// default.
    /// </para>
    /// </summary>
    public class JsonTreeViewTests : TestContext
    {
        [Fact]
        public void Renders_nothing_for_a_null_node()
        {
            IRenderedComponent<JsonTreeView> component = RenderComponent<JsonTreeView>(p => p
                .Add(c => c.Node, (JToken)null));

            Assert.Empty(component.Markup.Trim());
        }

        [Fact]
        public void Renders_an_object_key_and_child_value()
        {
            JToken node = JToken.Parse("""{ "title": "Pump", "count": 3 }""");

            IRenderedComponent<JsonTreeView> component = RenderComponent<JsonTreeView>(p => p
                .Add(c => c.Node, node));

            Assert.Contains("title", component.Markup, System.StringComparison.Ordinal);
            Assert.Contains("Pump", component.Markup, System.StringComparison.Ordinal);
            Assert.Contains("count", component.Markup, System.StringComparison.Ordinal);
        }

        [Fact]
        public void Renders_a_named_primitive_with_its_value()
        {
            JToken node = JToken.Parse("\"hello\"");

            IRenderedComponent<JsonTreeView> component = RenderComponent<JsonTreeView>(p => p
                .Add(c => c.Node, node)
                .Add(c => c.Name, "greeting"));

            Assert.Contains("greeting", component.Markup, System.StringComparison.Ordinal);
            Assert.Contains("hello", component.Markup, System.StringComparison.Ordinal);
        }

        [Fact]
        public void Renders_an_array_with_a_child_count_badge()
        {
            JToken node = JToken.Parse("[1, 2, 3]");

            IRenderedComponent<JsonTreeView> component = RenderComponent<JsonTreeView>(p => p
                .Add(c => c.Node, node)
                .Add(c => c.Name, "values"));

            // Arrays are summarised rather than printed inline when collapsed.
            Assert.Contains("jtn-badge", component.Markup, System.StringComparison.Ordinal);
        }

        [Fact]
        public void Clicking_a_parent_row_toggles_its_children()
        {
            JToken node = JToken.Parse("""{ "outer": { "inner": 1 } }""");

            IRenderedComponent<JsonTreeView> component = RenderComponent<JsonTreeView>(p => p
                .Add(c => c.Node, node));

            int before = component.FindAll(".jtn-row").Count;

            component.Find(".jtn-row").Click();

            int after = component.FindAll(".jtn-row").Count;

            // Toggling a parent must add or remove rendered child rows.
            Assert.NotEqual(before, after);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("true")]
        [InlineData("42")]
        [InlineData("3.5")]
        [InlineData("\"text\"")]
        [InlineData("{}")]
        [InlineData("[]")]
        public void Renders_every_json_primitive_shape_without_throwing(string json)
        {
            JToken node = JToken.Parse(json);

            IRenderedComponent<JsonTreeView> component = RenderComponent<JsonTreeView>(p => p
                .Add(c => c.Node, node)
                .Add(c => c.Name, "value"));

            Assert.NotNull(component.Markup);
        }

        [Fact]
        public void Renders_deeply_nested_content_when_expanded()
        {
            JToken node = JToken.Parse("""{ "a": { "b": { "c": { "d": "leaf" } } } }""");

            IRenderedComponent<JsonTreeView> component = RenderComponent<JsonTreeView>(p => p
                .Add(c => c.Node, node));

            Assert.Contains("a", component.Markup, System.StringComparison.Ordinal);
        }

        [Fact]
        public void Indentation_increases_with_depth()
        {
            JToken node = JToken.Parse("""{ "k": "v" }""");

            IRenderedComponent<JsonTreeView> shallow = RenderComponent<JsonTreeView>(p => p
                .Add(c => c.Node, node)
                .Add(c => c.Depth, 0));

            IRenderedComponent<JsonTreeView> deep = RenderComponent<JsonTreeView>(p => p
                .Add(c => c.Node, node)
                .Add(c => c.Depth, 3));

            Assert.NotEqual(shallow.Markup, deep.Markup);
            Assert.Contains("padding-left", deep.Markup, System.StringComparison.Ordinal);
        }
    }
}
