using Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models;
using System.Collections.Generic;

namespace Matter.Core.InteractionModel
{
    public class ReportDataAction
    {
        public ReportDataAction(MatterTLV payload)
        {
            payload.OpenStructure();

            if (payload.IsNextTag(0))
            {
                SubscriptionId = payload.GetUnsignedInt32(0);
            }

            if (payload.IsNextTag(1))
            {
                payload.OpenArray(1);

                while (!payload.IsEndContainerNext())
                {
                    AttributeReports.Add(new AttributeReportIB(payload));
                }
            }

            payload.CloseContainer();
        }

        public uint SubscriptionId { get; set; }

        public List<AttributeReportIB> AttributeReports { get; set; } = [];

        public uint InteractionModelRevision { get; set; }
    }
}
