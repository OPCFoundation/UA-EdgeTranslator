using WotOpcUaMapper.Models;
using WotOpcUaMapper.UAClientLib;

namespace WotOpcUaMapper.Services
{
    /// <summary>
    /// Holds the loaded Mapper state (Thing Model/Description and the OPC UA nodeset browse tree)
    /// for the lifetime of the user's Blazor circuit. Registered as scoped so the loaded files
    /// are retained when the user navigates between the Mapper and Settings pages and back.
    /// </summary>
    public class MapperState
    {
        // WoT Thing Model / Thing Description
        public ThingModel? ThingModel { get; set; }

        // OPC UA nodeset browse tree
        public List<NodesetViewerNode> TreeNodes { get; } = new();
        public NodesetViewerNode? SelectedNode { get; set; }
        public IList<NodesetViewerNode> ExpandedNodes { get; set; } = new List<NodesetViewerNode>();

        public Dictionary<string, Tuple<string, string>> LoadedNamespaces { get; set; } = new();
        public List<string> MissingNamespaces { get; set; } = new();
        public string? LoadedNamespaceUri { get; set; }

        /// <summary>Display name of the loaded nodeset (file name or Cloud Library title).</summary>
        public string? NodesetName { get; set; }

        /// <summary>True once a nodeset has been loaded into the shared UAClient session.</summary>
        public bool NodesetLoaded { get; set; }
    }
}
