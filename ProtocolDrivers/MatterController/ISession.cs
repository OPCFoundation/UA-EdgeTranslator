namespace Matter.Core.Sessions
{
    using Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models;
    using System.Threading;
    using System.Threading.Tasks;

    public interface ISession
    {
        IConnection Connection { get; }

        ulong SourceNodeId { get; }

        ulong DestinationNodeId { get; }

        ushort SessionId { get; }

        ushort PeerSessionId { get; }

        bool UseMRP { get; }

        uint MessageCounter { get; }

        MessageExchange CreateExchange();

        byte[] Encode(MessageFrame message);

        MessageFrame Decode(MessageFrameParts messageFrameParts);

        Task<byte[]> ReadAsync(CancellationToken token);

        Task SendAsync(byte[] payload);

        void Close();

        IConnection CreateNewConnection();
    }
}
