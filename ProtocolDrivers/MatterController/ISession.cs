namespace Matter.Core
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface ISession
    {
        IConnection Connection { get; }

        ulong SourceNodeId { get; set; }

        ulong DestinationNodeId { get; set; }

        ushort SessionId { get; }

        ushort PeerSessionId { get; }

        bool UseMRP { get; }

        uint MessageCounter { get; set; }

        MessageExchange CreateExchange(ulong sourceNodeId, ulong destinationNodeId);

        byte[] Encode(MessageFrame message);

        MessageFrame Decode(byte[] messageFrameBytes);

        Task<byte[]> ReadAsync(CancellationToken token);

        Task SendAsync(byte[] payload);
    }
}
