using Matter.Core.TLV;

namespace Matter.Core.InteractionModel
{
    public class AttributeDataIB
    {
        public AttributeDataIB(int tag, MatterTLV payload)
        {
            payload.OpenStructure(tag);

            DataVersion = payload.GetUnsignedInt32(0);

            Path = new AttributePathIB(1, payload);

            Data = payload.GetData(2);

            payload.CloseContainer();
        }

        public uint DataVersion { get; }

        public AttributePathIB Path { get; }

        public object Data { get; }
    }
}