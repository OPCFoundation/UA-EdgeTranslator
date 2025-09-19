namespace Matter.Core.Fabrics
{
    using System.Threading.Tasks;

    public interface IFabricStorageProvider
    {
        bool DoesFabricExist(string fabricName);

        Task<Fabric> LoadFabricAsync(string fabricName);

        Task SaveFabricAsync(Fabric fabric);
    }
}
