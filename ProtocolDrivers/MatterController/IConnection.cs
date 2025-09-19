
namespace Matter.Core
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IConnection
    {
        event EventHandler ConnectionClosed;

        void Close();

        IConnection OpenConnection();

        Task<byte[]> ReadAsync(CancellationToken token);

        Task SendAsync(byte[] message);
    }
}
