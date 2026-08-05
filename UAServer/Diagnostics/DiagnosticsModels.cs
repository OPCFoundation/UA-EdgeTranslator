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

    /// <summary>The raw public certificate file served as a browser download.</summary>
    public sealed record CertificateFile(byte[] Content, string FileName, string ContentType);

    /// <summary>A namespace registered in the server's namespace table.</summary>
    public sealed record NamespaceInfo(int Index, string Uri);

    /// <summary>A single attribute row shown in the node detail panel.</summary>
    public sealed record NodeAttribute(string Name, string Value);

    /// <summary>
    /// A node in the address space tree. <see cref="HasChildren"/> is a cheap
    /// hint used to decide whether to render an expander; the actual children
    /// are fetched lazily when the node is expanded.
    /// </summary>
    public sealed class AddressSpaceNode
    {
        /// <summary>The node id in string form, used as the stable tree key.</summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>Text rendered in the tree (display name, falling back to browse name).</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>NodeClass name, e.g. Object, Variable, Method.</summary>
        public string NodeClass { get; init; } = string.Empty;

        /// <summary>Namespace index of this node, used for the bold namespace highlight.</summary>
        public int NamespaceIndex { get; init; }

        /// <summary>
        /// True when the node exposes hierarchical children. Resolved by an
        /// explicit probe during the browse so the tree only renders an
        /// expander on nodes that really can be expanded.
        /// </summary>
        public bool HasChildren { get; set; }

        /// <summary>Lazily populated children; null until the node is expanded.</summary>
        public List<AddressSpaceNode> Children { get; set; }

        // Tree identity is by node id so that expansion/selection survives the
        // list being rebuilt on refresh.
        public override bool Equals(object obj) => obj is AddressSpaceNode other && other.Id == Id;

        public override int GetHashCode() => Id?.GetHashCode(StringComparison.Ordinal) ?? 0;
    }

    /// <summary>Detailed attribute set for the node selected in the Explorer.</summary>
    public sealed class NodeDetail
    {
        public string NodeId { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public string NodeClass { get; init; } = string.Empty;

        public string NamespaceUri { get; init; } = string.Empty;

        /// <summary>Core attributes (browse name, description, etc.).</summary>
        public IReadOnlyList<NodeAttribute> Attributes { get; init; } = new List<NodeAttribute>();

        /// <summary>Value-related attributes; empty for non-Variable nodes.</summary>
        public IReadOnlyList<NodeAttribute> ValueAttributes { get; init; } = new List<NodeAttribute>();

        /// <summary>
        /// Decoded fields when the value is a structure (UDT). Empty for
        /// scalar values.
        /// </summary>
        public IReadOnlyList<StructureFieldValue> Fields { get; init; } = new List<StructureFieldValue>();

        /// <summary>Forward/inverse references of the node.</summary>
        public IReadOnlyList<NodeReferenceInfo> References { get; init; } = new List<NodeReferenceInfo>();

        /// <summary>Populated when the node could not be read.</summary>
        public string Error { get; init; }
    }

    /// <summary>A single reference shown in the node detail panel.</summary>
    public sealed record NodeReferenceInfo(string ReferenceType, string Direction, string TargetName, string TargetNodeId);

    /// <summary>One decoded field of a structure (UDT) value.</summary>
    public sealed record StructureFieldValue(string Name, string DataType, string Value);

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

        // True when the IGNORE_PROVISIONING_MODE escape hatch is set. It unblocks
        // asset-tag access AND suppresses provisioning-mode auto-accept of untrusted
        // client certificates (they are rejected and must be trusted manually).
        public bool IgnoreProvisioningMode { get; init; }

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

        // True when IGNORE_PROVISIONING_MODE is set: provisioning auto-accept of
        // untrusted client certificates is suppressed, so they are rejected and
        // must be trusted manually.
        public bool IgnoreProvisioningMode { get; init; }

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
