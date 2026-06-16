namespace Opc.Ua.Edge.Translator.Diagnostics
{
    using System;
    using System.Collections.Generic;

    /// <summary>Live southbound connection state for a single onboarded asset.</summary>
    public sealed record ConnectedAssetInfo(string Name, bool IsConnected, string Endpoint, int TagCount);

    /// <summary>Connected device row shown on the Devices page.</summary>
    public sealed record DeviceStatus(string Name, string Protocol, string Endpoint, bool IsConnected, int TagCount);

    /// <summary>A loaded southbound protocol driver shown on the Drivers page.</summary>
    public sealed record ProtocolDriverInfo(string Scheme, string WoTBindingUri, string TypeName, string Assembly, string Version);

    /// <summary>A configured OPC UA server security policy.</summary>
    public sealed record SecurityPolicyInfo(string Mode, string Policy);

    /// <summary>A generic name/value row used for tabular settings.</summary>
    public sealed record SettingItem(string Name, string Value);

    /// <summary>Cumulative southbound activity counters since host start.</summary>
    public sealed record TelemetryCounters(
        long TagReads,
        long TagReadErrors,
        long TagWrites,
        long TagWriteErrors,
        long AssetReconnects,
        long AssetReconnectFailures);

    /// <summary>Outcome of moving a rejected certificate into the trusted store.</summary>
    public sealed record TrustCertificateResult(bool Success, string Message);

    /// <summary>High-level snapshot rendered on the Overview page.</summary>
    public sealed class ServerOverview
    {
        public string ApplicationName { get; init; } = string.Empty;

        public string ApplicationUri { get; init; } = string.Empty;

        public string ProductUri { get; init; } = string.Empty;

        public string Version { get; init; } = string.Empty;

        public string Runtime { get; init; } = string.Empty;

        public string HostName { get; init; } = string.Empty;

        public IReadOnlyList<string> Endpoints { get; init; } = [];

        public int DriverCount { get; init; }

        public int DeviceCount { get; init; }

        public int ConnectedDeviceCount { get; init; }

        public int WoTFileCount { get; init; }

        public bool ProvisioningMode { get; init; }

        // True when the server is in provisioning mode AND the IGNORE_PROVISIONING_MODE
        // escape hatch is not set, i.e. OnReadValue / OnWriteValue reject every asset-tag
        // read and write.
        public bool TagAccessBlocked { get; init; }

        public DateTime StartTimeUtc { get; set; }

        public int MemoryWorkingSetMB { get; set; }

        public TelemetryCounters Counters { get; init; } = new(0, 0, 0, 0, 0, 0);
    }

    /// <summary>The curated OPC UA configuration shown on the Settings page.</summary>
    public sealed class OpcUaSettingsInfo
    {
        public string ApplicationName { get; init; } = string.Empty;

        public string ApplicationUri { get; init; } = string.Empty;

        public string ProductUri { get; init; } = string.Empty;

        public string ApplicationType { get; init; } = string.Empty;

        public IReadOnlyList<string> Endpoints { get; init; } = [];

        public IReadOnlyList<SecurityPolicyInfo> SecurityPolicies { get; init; } = [];

        public IReadOnlyList<string> UserTokenPolicies { get; init; } = [];

        public IReadOnlyList<SettingItem> SessionLimits { get; init; } = [];

        public IReadOnlyList<SettingItem> TransportQuotas { get; init; } = [];

        public IReadOnlyList<SettingItem> SecuritySettings { get; init; } = [];
    }

    /// <summary>A loaded WoT Thing Description file and its parsed summary.</summary>
    public sealed class WoTFileInfo
    {
        public string FileName { get; init; } = string.Empty;

        public string Title { get; set; }

        public string Name { get; set; }

        public string Base { get; set; }

        public string Description { get; set; }

        public int PropertyCount { get; set; }

        public int ActionCount { get; set; }

        public long SizeBytes { get; init; }

        public DateTime LastModifiedUtc { get; init; }

        public string RawJson { get; init; } = string.Empty;

        public string PrettyJson { get; set; } = string.Empty;

        public string ParseError { get; set; }
    }

    /// <summary>Details of a single X.509 certificate in one of the pki stores.</summary>
    public sealed class CertificateInfo
    {
        public string FileName { get; init; } = string.Empty;

        public string Subject { get; init; } = string.Empty;

        public string Issuer { get; init; } = string.Empty;

        public string Thumbprint { get; init; } = string.Empty;

        public string SerialNumber { get; init; } = string.Empty;

        public DateTime NotBefore { get; init; }

        public DateTime NotAfter { get; init; }

        public string SignatureAlgorithm { get; init; } = string.Empty;

        public int KeySize { get; init; }

        public string Status { get; init; } = string.Empty;

        public int DaysUntilExpiry { get; init; }

        public bool SelfSigned { get; init; }
    }

    /// <summary>Aggregated certificate / pki state for the Certificates page.</summary>
    public sealed class CertificateOverview
    {
        public bool ProvisioningMode { get; init; }

        public IReadOnlyList<CertificateInfo> ApplicationCertificates { get; init; } = [];

        public IReadOnlyList<CertificateInfo> TrustedCertificates { get; init; } = [];

        public IReadOnlyList<CertificateInfo> IssuerCertificates { get; init; } = [];

        public IReadOnlyList<CertificateInfo> RejectedCertificates { get; init; } = [];

        public int TrustedCount { get; init; }

        public int IssuerCount { get; init; }

        public int RejectedCount { get; init; }

        public string OwnStorePath { get; init; } = string.Empty;

        public string TrustedStorePath { get; init; } = string.Empty;

        public string IssuerStorePath { get; init; } = string.Empty;

        public string RejectedStorePath { get; init; } = string.Empty;
    }
}
