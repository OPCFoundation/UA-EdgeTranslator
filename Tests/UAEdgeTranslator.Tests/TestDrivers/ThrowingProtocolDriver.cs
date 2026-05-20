namespace Opc.Ua.Edge.Translator.Tests.TestDrivers
{
    using System;
    using System.Collections.Generic;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;

    /// <summary>
    /// Test-only IProtocolDriver implementation whose constructor always
    /// throws. Used by <c>DriverLoadContextExtraBranchesTests</c> to drive
    /// the catch branch in <c>DriverLoadContext.LoadProtocolDrivers</c>.
    /// </summary>
    public sealed class ThrowingProtocolDriver : IProtocolDriver
    {
        public ThrowingProtocolDriver()
        {
            throw new InvalidOperationException("ThrowingProtocolDriver always throws on construction.");
        }

        public string Scheme => "throwing";

        public string WoTBindingUri => "https://example/throwing";

        public IEnumerable<string> Discover() => throw new NotSupportedException();

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
            => throw new NotSupportedException();

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
            => throw new NotSupportedException();

        public AssetTag CreateTag(
            ThingDescription td,
            object form,
            string assetId,
            byte unitId,
            string variableId,
            string mappedUAExpandedNodeId,
            string mappedUAFieldPath)
            => throw new NotSupportedException();
    }
}
