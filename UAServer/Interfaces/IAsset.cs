namespace Opc.Ua.Edge.Translator.Interfaces
{
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Data-plane interface implemented by a WoT protocol binding driver
    /// (Modbus, HTTP, OPC UA, ...). One instance is created per asset and lives
    /// for the lifetime of the corresponding OPC UA asset object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The I/O operations are asynchronous so that drivers no longer have to block
    /// on their own async transports (which previously forced
    /// <c>.GetAwaiter().GetResult()</c> / <c>.Result</c> calls and risked thread-pool
    /// starvation). This shape deliberately mirrors
    /// <c>Opc.Ua.WotCon.Server.IWotAssetProvider</c> from UA-.NETStandard so that a
    /// future migration to the upstream WoT Connectivity server library is an adapter
    /// rather than a rewrite.
    /// </para>
    /// <para>
    /// Implementations must be safe for concurrent use: the OPC UA stack does not
    /// serialise access. Long-running I/O must honour the supplied
    /// <see cref="CancellationToken"/>.
    /// </para>
    /// <para>
    /// <see cref="IsConnected"/> and <see cref="GetRemoteEndpoint"/> remain synchronous
    /// because they are pure in-memory state accessors and must never perform I/O.
    /// </para>
    /// </remarks>
    public interface IAsset : IAsyncDisposable
    {
        /// <summary>
        /// True while the driver holds a live connection to the asset.
        /// Must not perform I/O.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Returns the remote endpoint the driver is connected to (or would connect to).
        /// Must not perform I/O.
        /// </summary>
        string GetRemoteEndpoint();

        /// <summary>
        /// Establishes a connection to the asset.
        /// </summary>
        Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes the connection to the asset. Must not throw when already disconnected.
        /// </summary>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the current value of <paramref name="tag"/> from the asset.
        /// </summary>
        Task<object> ReadAsync(AssetTag tag, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes <paramref name="value"/> to <paramref name="tag"/> on the asset.
        /// </summary>
        Task WriteAsync(AssetTag tag, object value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes a WoT action on the asset.
        /// </summary>
        /// <returns>
        /// The action status and any output arguments. Drivers that produce no outputs
        /// may return <see cref="AssetActionResult.FromStatus"/>.
        /// </returns>
        Task<AssetActionResult> ExecuteActionAsync(
            MethodState method,
            IList<object> inputArgs,
            CancellationToken cancellationToken = default);
    }
}
