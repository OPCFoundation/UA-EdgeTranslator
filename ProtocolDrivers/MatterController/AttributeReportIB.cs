using Matter.Core.TLV;

namespace Matter.Core.InteractionModel
{
    public class AttributeReportIB
    {
        public AttributeReportIB(MatterTLV payload)
        {
            payload.OpenStructure();

            if (payload.IsNextTag(0))
            {
                AttributeStatus = new AttributeStatusIB(0, payload);
            }

            if (payload.IsNextTag(1))
            {
                AttributeData = new AttributeDataIB(1, payload);
            }

            payload.CloseContainer();
        }

        public AttributeStatusIB AttributeStatus { get; }

        public AttributeDataIB AttributeData { get; }
    }
}