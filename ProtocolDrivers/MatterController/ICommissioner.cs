using Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models;
using System.Threading.Tasks;

namespace Matter.Core
{
    public interface ICommissioner
    {
        int Id { get; }

        Task CommissionNodeAsync(CommissioningPayload payload);
    }
}
