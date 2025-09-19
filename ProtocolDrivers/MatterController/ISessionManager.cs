using Matter.Core.Fabrics;
using System.Threading.Tasks;

namespace Matter.Core.Sessions
{
    public interface ISessionManager
    {
        Task Start(Fabric fabric);
    }
}
