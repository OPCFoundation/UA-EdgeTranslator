namespace Opc.Ua.Edge.Translator.Interfaces
{
    using System.Collections.Generic;
    using System.Net;
    using System.Threading.Tasks;

    public interface IAsset
    {
        public List<string> Discover();

        public void Connect(string ipAddress, int port);

        public void Disconnect();

        public string GetRemoteEndpoint();

        public Task<byte[]> Read(string addressWithinAsset, byte unitID, string function, ushort count);

        public Task Write(string addressWithinAsset, byte unitID, string function, byte[] values, bool singleBitOnly);
    }
}
