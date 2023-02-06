namespace Opc.Ua.Edge.Translator.Interfaces
{
    using System.Threading.Tasks;

    public interface IAsset
    {
        public void Connect(string ipAddress, int port);

        public void Disconnect();

        public Task<byte[]> Read(byte unitID, string function, uint address, ushort count);

        public Task WriteBit(byte unitID, uint address, bool set);

        public Task Write(byte unitID, uint address, ushort[] values);
    }
}