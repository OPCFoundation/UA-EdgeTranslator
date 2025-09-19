using Matter.Core.Commissioning;
using System.Threading.Tasks;

namespace Matter.Core
{
    public interface ICommissioner
    {
        int Id { get; }

        Task CommissionNodeAsync(CommissioningPayload payload);
    }
}
