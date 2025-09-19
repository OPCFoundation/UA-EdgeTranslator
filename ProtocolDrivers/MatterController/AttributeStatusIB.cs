using Matter.Core.TLV;

namespace Matter.Core.InteractionModel
{
    public class AttributeStatusIB
    {
        public AttributeStatusIB(int tag, MatterTLV payload)
        {
            payload.OpenStructure(tag);

            if (payload.IsNextTag(0))
            {
                Path = new AttributePathIB(0, payload);
            }

            if (payload.IsNextTag(1))
            {
                Status = new StatusIB(1, payload);
            }
        }

        public AttributePathIB Path { get; }

        public StatusIB Status { get; }
    }
}