
namespace Matter.Core
{
    using System.Threading;
    using System.Threading.Tasks;

    public interface IConnection
    {
        void Close();

        IConnection OpenConnection();

        Task<byte[]> ReadAsync(CancellationToken token);

        Task SendAsync(byte[] message);
    }
}
