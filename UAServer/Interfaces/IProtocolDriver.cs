namespace Opc.Ua.Edge.Translator.Interfaces
{
    using Opc.Ua.Edge.Translator.Models;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IProtocolDriver
    {
        public string Scheme { get; } // e.g. "modbus+tcp", etc.

        public string WoTBindingUri { get; }

        public IEnumerable<string> Discover();

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint);

        /// <summary>
        /// Creates the asset for the given Thing Description and connects it.
        /// </summary>
        /// <returns>The connected asset together with its protocol unit id.</returns>
        public Task<AssetConnection> CreateAndConnectAssetAsync(ThingDescription td, CancellationToken cancellationToken = default);

        public AssetTag CreateTag(
            ThingDescription td,
            object form,
            string assetId,
            byte unitId,
            string variableId,
            string mappedUAExpandedNodeId,
            string mappedUAFieldPath);

        /// <summary>
        /// Called after namespace setup but before property/variable creation.
        /// Allows protocol drivers to register custom OPC UA DataType nodes
        /// (e.g. StructureTypes for PLCs with UDTs) into the address space
        /// so that uav:mapToType references resolve during AddNodeForWoTForm().
        /// </summary>
        public void RegisterStructureTypes(ThingDescription td, UANodeManager nodeManager)
        {
            // Default implementation does nothing
        }
    }
}
