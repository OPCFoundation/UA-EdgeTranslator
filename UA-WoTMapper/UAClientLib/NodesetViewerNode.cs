using System;
using System.Collections.Generic;

namespace WotOpcUaMapper.UAClientLib
{
    /// <summary>
    /// A node in the OPC UA browse tree shown in the right pane.
    /// </summary>
    public class NodesetViewerNode : IComparable<NodesetViewerNode>, IEquatable<NodesetViewerNode>
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string NodeClass { get; set; } = string.Empty;
        public string NamespaceUri { get; set; } = string.Empty;
        public List<NodesetViewerNode> Children { get; set; } = new();

        public int CompareTo(NodesetViewerNode? other)
        {
            return string.Compare(Text, other?.Text, StringComparison.OrdinalIgnoreCase);
        }

        public bool Equals(NodesetViewerNode? other)
        {
            return other != null && Id == other.Id;
        }

        public override bool Equals(object? obj) => Equals(obj as NodesetViewerNode);

        public override int GetHashCode() => Id.GetHashCode();
    }

    /// <summary>
    /// A single field of a complex (structure) OPC UA data type, offered as a mapping target.
    /// </summary>
    public class ComplexTypeField
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }
}
