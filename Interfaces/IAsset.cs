namespace Opc.Ua.Edge.Translator.Interfaces
{
    using System.Threading.Tasks;

    public interface IAsset
    {
        public void Connect(string ipAddress, int port);

        public void Disconnect();

        public string GetRemoteEndpoint();

        public Task<byte[]> Read(byte unitID, string function, string address, ushort count);

        public Task Write(byte unitID, string address, byte[] values, bool singleBitOnly);
    }
}
