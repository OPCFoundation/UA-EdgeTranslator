namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// In-memory IAsset implementation paired with <see cref="MockProtocolDriver"/>.
    /// Records every Connect / Disconnect / Read / Write / ExecuteAction call so
    /// tests can assert on the interaction sequence without any real I/O.
    ///
    /// All recorded state is exposed via thread-safe collections so tests can also
    /// exercise concurrent polling scenarios.
    /// </summary>
    public class MockAsset : IAsset
    {
        private readonly ConcurrentDictionary<string, object> _values = new(StringComparer.Ordinal);

        private readonly ConcurrentQueue<(string TagName, object Value)> _writes = new();

        private readonly ConcurrentQueue<string> _reads = new();

        private readonly ConcurrentQueue<(string Action, IList<object> Inputs)> _actions = new();

        private string _baseUrl = string.Empty;

        private int _connectCount;

        private int _disconnectCount;

        public bool IsConnected { get; private set; }

        public string LastBaseUrl => _baseUrl;

        public int ConnectCount => Volatile.Read(ref _connectCount);

        public int DisconnectCount => Volatile.Read(ref _disconnectCount);

        public IReadOnlyCollection<(string TagName, object Value)> Writes => _writes;

        public IReadOnlyCollection<string> Reads => _reads;

        public IReadOnlyCollection<(string Action, IList<object> Inputs)> Actions => _actions;

        /// <summary>
        /// Pre-seed a tag value. The value is returned by <see cref="Read"/> until
        /// a subsequent <see cref="Write"/> overrides it.
        /// </summary>
        public void Seed(string tagName, object value)
        {
            ArgumentException.ThrowIfNullOrEmpty(tagName);
            _values[tagName] = value;
        }

        public void Connect(string ipAddress, int port)
        {
            // Match the real driver behavior: missing host should be a hard failure
            // so a bad TD doesn't silently bring up a half-initialized asset.
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                throw new ArgumentException("Host must be provided.", nameof(ipAddress));
            }

            _baseUrl = port > 0
                ? $"{MockProtocolDriver.MockScheme}://{ipAddress}:{port}"
                : $"{MockProtocolDriver.MockScheme}://{ipAddress}";

            IsConnected = true;
            Interlocked.Increment(ref _connectCount);
        }

        public void Disconnect()
        {
            IsConnected = false;
            Interlocked.Increment(ref _disconnectCount);
        }

        public string GetRemoteEndpoint() => _baseUrl;

        public object Read(AssetTag tag)
        {
            ArgumentNullException.ThrowIfNull(tag);

            if (!IsConnected)
            {
                throw new InvalidOperationException("MockAsset is not connected.");
            }

            _reads.Enqueue(tag.Name);
            return _values.TryGetValue(tag.Name, out object value) ? value : null;
        }

        public void Write(AssetTag tag, object value)
        {
            ArgumentNullException.ThrowIfNull(tag);

            if (!IsConnected)
            {
                throw new InvalidOperationException("MockAsset is not connected.");
            }

            _values[tag.Name] = value;
            _writes.Enqueue((tag.Name, value));
        }

        public string ExecuteAction(MethodState method, IList<object> inputArgs, ref IList<object> outputArgs)
        {
            ArgumentNullException.ThrowIfNull(method);

            if (!IsConnected)
            {
                throw new InvalidOperationException("MockAsset is not connected.");
            }

            string actionName = method.BrowseName?.Name ?? string.Empty;
            var inputsCopy = inputArgs == null
                ? (IList<object>)new List<object>()
                : new List<object>(inputArgs);

            _actions.Enqueue((actionName, inputsCopy));
            outputArgs = new List<object> { $"mock:{actionName}:ok" };
            return $"mock:{actionName}:ok";
        }
    }
}
