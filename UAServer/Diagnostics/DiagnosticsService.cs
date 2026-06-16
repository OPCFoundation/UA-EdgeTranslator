namespace Opc.Ua.Edge.Translator.Diagnostics
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using Opc.Ua;
    using Opc.Ua.Cloud;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using Serilog;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Security.Cryptography.X509Certificates;

    /// <summary>
    /// Read-only provider that produces point-in-time snapshots for the
    /// diagnostics UI. It deliberately holds no state: every call re-reads the
    /// live OPC UA <see cref="ApplicationConfiguration"/>, the on-disk settings
    /// and pki folders, and the active <see cref="UANodeManager"/>, so the
    /// dashboard always reflects the current state of the running translator.
    /// All reads are defensive — a single bad file or driver must never throw
    /// the page.
    /// </summary>
    public sealed class DiagnosticsService
    {
        private static readonly string[] _certExtensions = [".der", ".crt", ".cer", ".pem"];

        public ServerOverview GetServerOverview()
        {
            ApplicationConfiguration config = Program.App?.ApplicationConfiguration;
            IReadOnlyList<DeviceStatus> devices = GetConnectedDevices();
            bool provisioning = IsProvisioningMode(config?.SecurityConfiguration);

            ServerOverview overview = new()
            {
                ApplicationName = config?.ApplicationName ?? "UA Edge Translator",
                ApplicationUri = config?.ApplicationUri ?? string.Empty,
                ProductUri = config?.ProductUri ?? string.Empty,
                Version = GetVersion(),
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                HostName = Environment.MachineName,
                Endpoints = GetEndpoints(config),
                DriverCount = SafeDriverCount(),
                DeviceCount = devices.Count,
                ConnectedDeviceCount = devices.Count(d => d.IsConnected),
                WoTFileCount = CountWoTFiles(),
                ProvisioningMode = provisioning,
                TagAccessBlocked = provisioning && !IsIgnoreProvisioningModeSet(),
                Counters = GetTelemetryCounters()
            };

            try
            {
                using Process proc = Process.GetCurrentProcess();
                overview.StartTimeUtc = proc.StartTime.ToUniversalTime();
                overview.MemoryWorkingSetMB = (int)(proc.WorkingSet64 / (1024L * 1024L));
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to read process diagnostics.");
                overview.StartTimeUtc = DateTime.UtcNow;
            }

            return overview;
        }

        private static TelemetryCounters GetTelemetryCounters()
        {
            ConsoleTelemetry telemetry = Program.Telemetry;
            if (telemetry == null)
            {
                return new TelemetryCounters(0, 0, 0, 0, 0, 0);
            }

            return new TelemetryCounters(
                telemetry.TagReadCount,
                telemetry.TagReadErrorCount,
                telemetry.TagWriteCount,
                telemetry.TagWriteErrorCount,
                telemetry.AssetReconnectCount,
                telemetry.AssetReconnectFailureCount);
        }

        /// <summary>
        /// True when asset-tag reads and writes are currently blocked: the server is in
        /// provisioning mode and the IGNORE_PROVISIONING_MODE escape hatch is unset. This
        /// mirrors the gate enforced by UANodeManager.OnReadValue / OnWriteValue.
        /// </summary>
        public bool IsTagAccessBlocked()
        {
            ApplicationConfiguration config = Program.App?.ApplicationConfiguration;
            return IsProvisioningMode(config?.SecurityConfiguration) && !IsIgnoreProvisioningModeSet();
        }

        public OpcUaSettingsInfo GetOpcUaSettings()
        {
            ApplicationConfiguration config = Program.App?.ApplicationConfiguration;
            if (config == null)
            {
                return new OpcUaSettingsInfo();
            }

            ServerConfiguration server = config.ServerConfiguration;
            SecurityConfiguration security = config.SecurityConfiguration;
            TransportQuotas quotas = config.TransportQuotas;

            List<SecurityPolicyInfo> policies = new();
            if (server?.SecurityPolicies != null)
            {
                foreach (ServerSecurityPolicy policy in server.SecurityPolicies)
                {
                    policies.Add(new SecurityPolicyInfo(policy.SecurityMode.ToString(), ShortPolicy(policy.SecurityPolicyUri)));
                }
            }

            List<string> tokenPolicies = new();
            if (server?.UserTokenPolicies != null)
            {
                foreach (UserTokenPolicy token in server.UserTokenPolicies)
                {
                    tokenPolicies.Add(token.TokenType.ToString());
                }
            }

            List<SettingItem> sessionLimits = new();
            if (server != null)
            {
                sessionLimits.Add(new SettingItem("Max session count", Num(server.MaxSessionCount)));
                sessionLimits.Add(new SettingItem("Min session timeout (ms)", Num(server.MinSessionTimeout)));
                sessionLimits.Add(new SettingItem("Max session timeout (ms)", Num(server.MaxSessionTimeout)));
                sessionLimits.Add(new SettingItem("Min request threads", Num(server.MinRequestThreadCount)));
                sessionLimits.Add(new SettingItem("Max request threads", Num(server.MaxRequestThreadCount)));
                sessionLimits.Add(new SettingItem("Max queued requests", Num(server.MaxQueuedRequestCount)));
                sessionLimits.Add(new SettingItem("Diagnostics enabled", server.DiagnosticsEnabled.ToString()));
            }

            List<SettingItem> transport = new();
            if (quotas != null)
            {
                transport.Add(new SettingItem("Operation timeout (ms)", Num(quotas.OperationTimeout)));
                transport.Add(new SettingItem("Max string length", Num(quotas.MaxStringLength)));
                transport.Add(new SettingItem("Max byte-string length", Num(quotas.MaxByteStringLength)));
                transport.Add(new SettingItem("Max array length", Num(quotas.MaxArrayLength)));
                transport.Add(new SettingItem("Max message size", Num(quotas.MaxMessageSize)));
                transport.Add(new SettingItem("Max buffer size", Num(quotas.MaxBufferSize)));
                transport.Add(new SettingItem("Channel lifetime (ms)", Num(quotas.ChannelLifetime)));
                transport.Add(new SettingItem("Security token lifetime (ms)", Num(quotas.SecurityTokenLifetime)));
            }

            List<SettingItem> securitySettings = new();
            if (security != null)
            {
                securitySettings.Add(new SettingItem("Auto-accept untrusted certs", security.AutoAcceptUntrustedCertificates.ToString()));
                securitySettings.Add(new SettingItem("Reject SHA-1 signed certs", security.RejectSHA1SignedCertificates.ToString()));
                securitySettings.Add(new SettingItem("Minimum certificate key size", Num(security.MinimumCertificateKeySize)));
                securitySettings.Add(new SettingItem("Application certificate subject", security.ApplicationCertificate?.SubjectName ?? string.Empty));
            }

            return new OpcUaSettingsInfo
            {
                ApplicationName = config.ApplicationName ?? string.Empty,
                ApplicationUri = config.ApplicationUri ?? string.Empty,
                ProductUri = config.ProductUri ?? string.Empty,
                ApplicationType = config.ApplicationType.ToString(),
                Endpoints = GetEndpoints(config),
                SecurityPolicies = policies,
                UserTokenPolicies = tokenPolicies,
                SessionLimits = sessionLimits,
                TransportQuotas = transport,
                SecuritySettings = securitySettings
            };
        }

        public IReadOnlyList<DeviceStatus> GetConnectedDevices()
        {
            UANodeManager manager = UANodeManager.Instance;
            if (manager == null)
            {
                return [];
            }

            Dictionary<string, string> protocols = new(StringComparer.OrdinalIgnoreCase);
            foreach (WoTFileInfo wot in GetWoTFiles())
            {
                string key = !string.IsNullOrEmpty(wot.Name)
                    ? wot.Name
                    : Path.GetFileNameWithoutExtension(wot.FileName);

                protocols[key] = ParseScheme(wot.Base);
            }

            List<DeviceStatus> result = new();
            foreach (ConnectedAssetInfo asset in manager.GetConnectedAssets())
            {
                string protocol = protocols.TryGetValue(asset.Name, out string scheme) ? scheme : "unknown";
                result.Add(new DeviceStatus(asset.Name, protocol, asset.Endpoint, asset.IsConnected, asset.TagCount));
            }

            return result.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Snapshot of the protocol drivers currently registered in
        /// <see cref="Program.Drivers"/>. Reads are defensive so a single
        /// misbehaving driver can never throw the page.
        /// </summary>
        public IReadOnlyList<ProtocolDriverInfo> GetProtocolDrivers()
        {
            List<ProtocolDriverInfo> drivers = new();

            IEnumerable<IProtocolDriver> registered;
            try
            {
                registered = Program.Drivers?.AllDrivers ?? [];
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to enumerate protocol drivers.");
                return drivers;
            }

            foreach (IProtocolDriver driver in registered)
            {
                if (driver is null)
                {
                    continue;
                }

                try
                {
                    Type type = driver.GetType();
                    AssemblyName assembly = type.Assembly.GetName();

                    drivers.Add(new ProtocolDriverInfo(
                        driver.Scheme ?? string.Empty,
                        driver.WoTBindingUri ?? string.Empty,
                        type.FullName ?? type.Name,
                        assembly.Name ?? string.Empty,
                        assembly.Version?.ToString() ?? string.Empty));
                }
                catch (Exception ex)
                {
                    Log.Logger.Debug(ex, "Failed to describe protocol driver {DriverType}.", driver.GetType().FullName);
                }
            }

            return drivers.OrderBy(d => d.Scheme, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public IReadOnlyList<WoTFileInfo> GetWoTFiles()
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "settings");
            if (!Directory.Exists(directory))
            {
                return [];
            }

            List<WoTFileInfo> files = new();
            foreach (string file in Directory.EnumerateFiles(directory, "*.jsonld"))
            {
                try
                {
                    FileInfo info = new(file);
                    string raw = File.ReadAllText(file);

                    WoTFileInfo entry = new()
                    {
                        FileName = Path.GetFileName(file),
                        SizeBytes = info.Length,
                        LastModifiedUtc = info.LastWriteTimeUtc,
                        RawJson = raw,
                        PrettyJson = raw
                    };

                    try
                    {
                        ThingDescription td = JsonConvert.DeserializeObject<ThingDescription>(raw);
                        if (td != null)
                        {
                            entry.Title = td.Title;
                            entry.Name = td.Name;
                            entry.Base = td.Base;
                            entry.Description = td.Description;
                            entry.PropertyCount = td.Properties?.Count ?? 0;
                            entry.ActionCount = td.Actions?.Count ?? 0;
                        }

                        entry.PrettyJson = JToken.Parse(raw).ToString(Formatting.Indented);
                    }
                    catch (Exception ex)
                    {
                        entry.ParseError = ex.Message;
                    }

                    files.Add(entry);
                }
                catch (Exception ex)
                {
                    Log.Logger.Debug(ex, "Failed to read WoT file {File}", file);
                }
            }

            return files.OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public CertificateOverview GetCertificateOverview()
        {
            SecurityConfiguration security = Program.App?.ApplicationConfiguration?.SecurityConfiguration;

            string ownCerts = ResolveCertsDirectory(security?.ApplicationCertificate?.StorePath, "pki/own");
            string trustedCerts = ResolveCertsDirectory(security?.TrustedPeerCertificates?.StorePath, "pki/trusted");
            string issuerCerts = ResolveCertsDirectory(security?.TrustedIssuerCertificates?.StorePath, "pki/issuer");
            string rejectedCerts = ResolveCertsDirectory(security?.RejectedCertificateStore?.StorePath, "pki/rejected");

            return new CertificateOverview
            {
                ProvisioningMode = IsProvisioningMode(security),
                ApplicationCertificates = LoadCertificates(ownCerts, 10),
                TrustedCertificates = LoadCertificates(trustedCerts, 50),
                IssuerCertificates = LoadCertificates(issuerCerts, 50),
                RejectedCertificates = LoadCertificates(rejectedCerts, 50),
                TrustedCount = CountCertificates(trustedCerts),
                IssuerCount = CountCertificates(issuerCerts),
                RejectedCount = CountCertificates(rejectedCerts),
                OwnStorePath = ownCerts,
                TrustedStorePath = trustedCerts,
                IssuerStorePath = issuerCerts,
                RejectedStorePath = rejectedCerts
            };
        }

        /// <summary>
        /// Moves a certificate from the rejected store into the trusted peer store so
        /// the matching client is allowed to connect. The certificate is located by its
        /// thumbprint; only the public certificate file is moved, which is all the
        /// directory-based trust list requires. The running OPC UA stack re-reads the
        /// trusted folder on the next validation, so no restart is needed.
        /// </summary>
        public TrustCertificateResult TrustRejectedCertificate(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                return new TrustCertificateResult(false, "No certificate thumbprint was provided.");
            }

            try
            {
                SecurityConfiguration security = Program.App?.ApplicationConfiguration?.SecurityConfiguration;
                string rejectedCerts = ResolveCertsDirectory(security?.RejectedCertificateStore?.StorePath, "pki/rejected");
                string trustedCerts = ResolveCertsDirectory(security?.TrustedPeerCertificates?.StorePath, "pki/trusted");

                if (!Directory.Exists(rejectedCerts))
                {
                    return new TrustCertificateResult(false, "The rejected certificate store does not exist.");
                }

                string normalized = thumbprint.Replace(" ", string.Empty).Trim();
                string sourceFile = FindCertificateFileByThumbprint(rejectedCerts, normalized);
                if (sourceFile == null)
                {
                    return new TrustCertificateResult(false, "The certificate could not be found in the rejected store.");
                }

                Directory.CreateDirectory(trustedCerts);

                string destFile = Path.Combine(trustedCerts, Path.GetFileName(sourceFile));
                if (File.Exists(destFile))
                {
                    // An entry with the same file name is already trusted; drop the
                    // rejected copy so it stops showing up as rejected.
                    File.Delete(sourceFile);
                    return new TrustCertificateResult(true, "The certificate is already in the trusted store.");
                }

                File.Move(sourceFile, destFile);

                Log.Logger.Information("Moved rejected certificate {Thumbprint} to the trusted peer store.", normalized);
                return new TrustCertificateResult(true, "Certificate moved to the trusted store.");
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Failed to trust rejected certificate {Thumbprint}.", thumbprint);
                return new TrustCertificateResult(false, $"Failed to trust certificate: {ex.Message}");
            }
        }

        private static string FindCertificateFileByThumbprint(string certsDirectory, string thumbprint)
        {
            foreach (string file in Directory.EnumerateFiles(certsDirectory))
            {
                if (!IsCertificateFile(file))
                {
                    continue;
                }

                try
                {
                    using X509Certificate2 certificate = LoadCertificate(file);
                    if (certificate != null
                        && string.Equals(certificate.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Debug(ex, "Failed to read certificate {File}", file);
                }
            }

            return null;
        }

        private static IReadOnlyList<string> GetEndpoints(ApplicationConfiguration config)
        {
            List<string> endpoints = new();
            if (config?.ServerConfiguration?.BaseAddresses != null)
            {
                foreach (string address in config.ServerConfiguration.BaseAddresses)
                {
                    endpoints.Add(address);
                }
            }

            return endpoints;
        }

        private static int SafeDriverCount()
        {
            try
            {
                return Program.Drivers?.AllDrivers?.Count() ?? 0;
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to count protocol drivers.");
                return 0;
            }
        }

        private static int CountWoTFiles()
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "settings");
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            return Directory.EnumerateFiles(directory, "*.jsonld").Count();
        }

        private static bool IsProvisioningMode(SecurityConfiguration security)
        {
            try
            {
                string issuerCerts = ResolveCertsDirectory(security?.TrustedIssuerCertificates?.StorePath, "pki/issuer");
                return !Directory.Exists(issuerCerts) || !Directory.EnumerateFiles(issuerCerts).Any();
            }
            catch (Exception ex)
            {
                Log.Logger.Debug(ex, "Failed to determine provisioning mode.");
                return true;
            }
        }

        private static bool IsIgnoreProvisioningModeSet()
            => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IGNORE_PROVISIONING_MODE"));

        private static IReadOnlyList<CertificateInfo> LoadCertificates(string certsDirectory, int max)
        {
            List<CertificateInfo> certificates = new();
            if (!Directory.Exists(certsDirectory))
            {
                return certificates;
            }

            foreach (string file in Directory.EnumerateFiles(certsDirectory))
            {
                if (certificates.Count >= max)
                {
                    break;
                }

                if (!IsCertificateFile(file))
                {
                    continue;
                }

                try
                {
                    using X509Certificate2 certificate = LoadCertificate(file);
                    if (certificate != null)
                    {
                        certificates.Add(Describe(certificate, Path.GetFileName(file)));
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Debug(ex, "Failed to read certificate {File}", file);
                }
            }

            return certificates
                .OrderBy(c => c.Subject, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static X509Certificate2 LoadCertificate(string file)
        {
            if (string.Equals(Path.GetExtension(file), ".pem", StringComparison.OrdinalIgnoreCase))
            {
                return X509Certificate2.CreateFromPemFile(file);
            }

            return X509CertificateLoader.LoadCertificateFromFile(file);
        }

        private static CertificateInfo Describe(X509Certificate2 certificate, string fileName)
        {
            DateTime now = DateTime.Now;

            string status;
            if (now < certificate.NotBefore)
            {
                status = "Not yet valid";
            }
            else if (now > certificate.NotAfter)
            {
                status = "Expired";
            }
            else if ((certificate.NotAfter - now).TotalDays <= 30)
            {
                status = "Expiring soon";
            }
            else
            {
                status = "Valid";
            }

            int keySize = 0;
            try
            {
                using RSA rsa = certificate.GetRSAPublicKey();
                if (rsa != null)
                {
                    keySize = rsa.KeySize;
                }
                else
                {
                    using ECDsa ecdsa = certificate.GetECDsaPublicKey();
                    if (ecdsa != null)
                    {
                        keySize = ecdsa.KeySize;
                    }
                }
            }
            catch (CryptographicException ex)
            {
                Log.Logger.Debug(ex, "Failed to read public key for certificate {File}", fileName);
            }

            return new CertificateInfo
            {
                FileName = fileName,
                Subject = certificate.Subject,
                Issuer = certificate.Issuer,
                Thumbprint = certificate.Thumbprint,
                SerialNumber = certificate.SerialNumber,
                NotBefore = certificate.NotBefore,
                NotAfter = certificate.NotAfter,
                SignatureAlgorithm = certificate.SignatureAlgorithm?.FriendlyName ?? "unknown",
                KeySize = keySize,
                Status = status,
                DaysUntilExpiry = (int)Math.Floor((certificate.NotAfter - now).TotalDays),
                SelfSigned = string.Equals(certificate.Subject, certificate.Issuer, StringComparison.Ordinal)
            };
        }

        private static int CountCertificates(string certsDirectory)
        {
            if (!Directory.Exists(certsDirectory))
            {
                return 0;
            }

            return Directory.EnumerateFiles(certsDirectory).Count(IsCertificateFile);
        }

        private static bool IsCertificateFile(string file)
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            return _certExtensions.Contains(extension);
        }

        private static string ResolveCertsDirectory(string storePath, string fallbackRelative)
        {
            string baseDirectory = Directory.GetCurrentDirectory();

            string store = string.IsNullOrEmpty(storePath)
                ? Path.Combine(baseDirectory, fallbackRelative.Replace('/', Path.DirectorySeparatorChar))
                : Path.GetFullPath(storePath, baseDirectory);

            return Path.Combine(store, "certs");
        }

        private static string ParseScheme(string uri)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return "unknown";
            }

            int index = uri.IndexOf("://", StringComparison.Ordinal);
            return index > 0 ? uri[..index] : "unknown";
        }

        private static string ShortPolicy(string policyUri)
        {
            if (string.IsNullOrEmpty(policyUri))
            {
                return "None";
            }

            int index = policyUri.LastIndexOf('#');
            return index >= 0 && index < policyUri.Length - 1 ? policyUri[(index + 1)..] : policyUri;
        }

        private static string GetVersion()
        {
            Assembly assembly = typeof(Program).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "unknown";
        }

        private static string Num(IFormattable value)
        {
            return value.ToString(null, CultureInfo.InvariantCulture);
        }
    }
}
