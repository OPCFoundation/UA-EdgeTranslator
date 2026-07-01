namespace Opc.Ua.Edge.Translator.Tests
{
    using Opc.Ua;
    using System;
    using System.Runtime.CompilerServices;
    using Xunit;

    /// <summary>
    /// Lightweight unit tests for <see cref="NodeFactory"/> validation
    /// branches that don't need a wired-up OPC UA address space.
    /// </summary>
    public class NodeFactoryTests
    {
        [Fact]
        public void Constructor_throws_when_manager_is_null()
        {
            Assert.Throws<ArgumentNullException>(() => new NodeFactory(null));
        }

        [Fact]
        public void CreateObject_throws_when_name_is_empty()
        {
            UANodeManager bareManager = (UANodeManager)RuntimeHelpers.GetUninitializedObject(typeof(UANodeManager));
            NodeFactory factory = new(bareManager);

            Assert.Throws<ArgumentNullException>(() =>
                factory.CreateObject(parent: null, name: string.Empty, type: new ExpandedNodeId(58u, 0), namespaceIndex: 0));
        }
    }
}
