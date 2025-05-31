namespace Opc.Ua.Edge.Translator.Interfaces
{
    using Opc.Ua.Edge.Translator.Models;
    using System.Collections.Generic;

    public interface IAsset
    {
        public List<string> Discover();

        public ThingDescription BrowseAndGenerateTD(string name, string endpoint);

        public void Connect(string ipAddress, int port);

        public void Disconnect();

        public string GetRemoteEndpoint();

        public object Read(AssetTag tag);

        public void Write(AssetTag tag, string value);

        public string ExecuteAction(string actionName, string[] inputArgs, string[] outputArgs);
    }
}
