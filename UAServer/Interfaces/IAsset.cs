namespace Opc.Ua.Edge.Translator.Interfaces
{
    using Opc.Ua.Edge.Translator.Models;
    using System.Collections.Generic;

    public interface IAsset
    {
        public bool IsConnected { get; }

        public void Connect(string ipAddress, int port);

        public void Disconnect();

        public string GetRemoteEndpoint();

        public object Read(AssetTag tag);

        public void Write(AssetTag tag, string value);

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs);
    }
}
