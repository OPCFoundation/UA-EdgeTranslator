
namespace Opc.Ua.Edge.Translator.Models
{
    public class AssetTag
    {
        public string Name { get; set; }

        public string Address { get; set; }

        public string Type { get; set; }

        public int PollingInterval { get; set; }

        public string Entity { get; set; }

        public string MappedUAExpandedNodeID { get; set; }
    }
}
