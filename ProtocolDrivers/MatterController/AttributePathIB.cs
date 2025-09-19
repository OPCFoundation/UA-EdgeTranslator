using Matter.Core.TLV;

namespace Matter.Core.InteractionModel
{
    public class AttributePathIB
    {
        public AttributePathIB(int tag, MatterTLV payload)
        {
            payload.OpenList(tag);

            while (!payload.IsEndContainerNext())
            {
                if (payload.IsNextTag(0))
                {
                    EnableTagCompression = payload.GetBoolean(0);
                }

                if (payload.IsNextTag(1))
                {
                    NodeId = payload.GetUnsignedInt(1);
                }

                if (payload.IsNextTag(2))
                {
                    EndpointId = (uint)payload.GetUnsignedInt(2);
                }

                if (payload.IsNextTag(3))
                {
                    ClusterId = (uint)payload.GetUnsignedInt(3);
                }

                if (payload.IsNextTag(4))
                {
                    AttributeId = (uint)payload.GetUnsignedInt(4);
                }

                if (payload.IsNextTag(5))
                {
                    ListIndex = (ushort)payload.GetUnsignedInt(5);
                }

                if (payload.IsNextTag(6))
                {
                    WildcardPathFlags = (uint)payload.GetUnsignedInt(6);
                }
            }

            payload.CloseContainer();
        }

        public bool EnableTagCompression { get; }

        public ulong NodeId { get; }

        public uint EndpointId { get; }

        public uint ClusterId { get; }

        public uint AttributeId { get; }

        public ushort ListIndex { get; }

        public uint WildcardPathFlags { get; }
    }
}