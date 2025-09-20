using Matter.Core.Fabrics;
using Matter.Core.Sessions;
using System;
using System.Threading.Tasks;

namespace Matter.Core
{
    public class MatterController
    {
        private readonly FabricManager _fabricManager;
        private readonly ISessionManager _sessionManager;

        public Fabric Fabric { get; private set; }

        public MatterController(IFabricStorageProvider fabricStorageProvider)
        {
            _fabricManager = new FabricManager(fabricStorageProvider);
            _sessionManager = new SessionManager();
        }

        public async Task InitAsync()
        {
            Fabric = await _fabricManager.GetAsync("Test");
        }

        public async Task RunAsync()
        {
            if (Fabric == null)
            {
                throw new InvalidOperationException($"Fabric not initialized. Call {nameof(InitAsync)}() first.");
            }

            await _sessionManager.Start(Fabric!);
        }
    }
}
