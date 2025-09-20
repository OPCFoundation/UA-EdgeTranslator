namespace Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models
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