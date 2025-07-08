
namespace Opc.Ua.Edge.Translator.Models
{
    public class AssetTag
    {
        public string Name { get; set; }

        public byte UnitID { get; set; }

        public string Address { get; set; }

        public string Type { get; set; }

        public bool IsBigEndian { get; set; } = false;

        public string BitMask { get; set; }

        public int PollingInterval { get; set; } = 1000;

        public string Entity { get; set; } = null;

        public string MappedUAExpandedNodeID { get; set; }

        public string MappedUAFieldPath { get; set; }
    }
}
