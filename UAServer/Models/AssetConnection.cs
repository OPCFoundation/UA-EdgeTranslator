
namespace Opc.Ua.Edge.Translator.Models
{
    using Opc.Ua.Edge.Translator.Interfaces;

    /// <summary>
    /// Result of asynchronously creating and connecting an asset via
    /// <see cref="IProtocolDriver.CreateAndConnectAssetAsync"/>.
    /// </summary>
    /// <remarks>
    /// The synchronous factory returned the asset and reported the protocol unit id via an
    /// <c>out byte unitId</c> parameter. <c>out</c> parameters are not allowed on async
    /// methods, so both values are returned together instead.
    /// </remarks>
    public class AssetConnection
    {
        public AssetConnection(IAsset asset, byte unitId)
        {
            Asset = asset;
            UnitId = unitId;
        }

        /// <summary>
        /// The connected asset.
        /// </summary>
        public IAsset Asset { get; }

        /// <summary>
        /// Protocol-specific unit/slave id for the asset (defaults to 1 for protocols
        /// that have no such concept).
        /// </summary>
        public byte UnitId { get; }
    }
}
