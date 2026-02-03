namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Opc.Ua.Edge.Translator.Interfaces;
    using System;
    using System.Collections.Generic;

    public sealed class ProtocolDriverRegistry
    {
        private readonly Dictionary<string, IProtocolDriver> _drivers = new(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<IProtocolDriver> AllDrivers => _drivers.Values;

        public void Register(IProtocolDriver driver) => _drivers[driver.Scheme] = driver;

        public bool TryGetByUri(string uri, out IProtocolDriver driver)
        {
            driver = null;

            if (string.IsNullOrWhiteSpace(uri))
            {
                return false;
            }

            var idx = uri.IndexOf("://", StringComparison.Ordinal);
            if (idx <= 0)
            {
                return false;
            }

            var scheme = uri[..idx];
            return _drivers.TryGetValue(scheme, out driver);
        }
    }
}
