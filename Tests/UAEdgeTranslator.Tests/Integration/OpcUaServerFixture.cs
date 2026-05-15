namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Opc.Ua;
    using Opc.Ua.Cloud;
    using Opc.Ua.Configuration;
    using Opc.Ua.Edge.Translator;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Reflection;
    using System.Threading.Tasks;

    /// <summary>
    /// Boots a real <see cref="UAServer"/> instance in-process against a per-fixture
    /// isolated working directory so an OPC UA client (running inside the same test
    /// host) can exercise the full WoT-Con address space.
    ///
    /// The fixture deliberately does NOT call <c>Program.Main</c> because Main blocks
    /// until SIGINT/SIGTERM. Instead it reproduces just the parts of Main that the
    /// running server depends on: validate env vars, set the <c>Program</c> statics,
    /// register a protocol driver, copy the WoT-Con nodeset and a test-friendly
    /// configuration file into <c>cwd</c>, and start the application.
    ///
    /// The Cloud Library is "mocked" by leaving <c>UACLURL</c> unset, which makes
    /// <see cref="UACloudLibraryClient.DownloadNodesetAsync"/> short-circuit to local
    /// nodeset lookup only — no HTTP is ever issued from the running server during a
    /// test. Tests that need a specific nodeset to be "downloadable" pre-place the
    /// XML in <c>nodesets/</c> under <see cref="WorkingDirectory"/>.
    /// </summary>
    public sealed class OpcUaServerFixture : IDisposable, IAsyncDisposable
    {
        private const string _envOpcUaUsername = "OPCUA_USERNAME";
        private const string _envOpcUaPassword = "OPCUA_PASSWORD";
        private const string _envDisableConnectionTest = "DISABLE_ASSET_CONNECTION_TEST";
        private const string _envIgnoreProvisioningMode = "IGNORE_PROVISIONING_MODE";
        private const string _envUaCloudLibraryUrl = "UACLURL";

        private readonly string _previousWorkingDirectory;
        private readonly string _previousUsername;
        private readonly string _previousPassword;
        private readonly string _previousDisableConnectionTest;
        private readonly string _previousIgnoreProvisioningMode;
        private readonly string _previousUaCloudLibraryUrl;
        private bool _disposed;

        public OpcUaServerFixture()
        {
            _previousWorkingDirectory = Directory.GetCurrentDirectory();
            _previousUsername = Environment.GetEnvironmentVariable(_envOpcUaUsername);
            _previousPassword = Environment.GetEnvironmentVariable(_envOpcUaPassword);
            _previousDisableConnectionTest = Environment.GetEnvironmentVariable(_envDisableConnectionTest);
            _previousIgnoreProvisioningMode = Environment.GetEnvironmentVariable(_envIgnoreProvisioningMode);
            _previousUaCloudLibraryUrl = Environment.GetEnvironmentVariable(_envUaCloudLibraryUrl);

            WorkingDirectory = Path.Combine(
                Path.GetTempPath(),
                "uaedge-integration-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(WorkingDirectory);
            Directory.CreateDirectory(Path.Combine(WorkingDirectory, "settings"));
            Directory.CreateDirectory(Path.Combine(WorkingDirectory, "nodesets"));
            Directory.CreateDirectory(Path.Combine(WorkingDirectory, "drivers"));
            Directory.CreateDirectory(Path.Combine(WorkingDirectory, "logs"));
            // UANodeManager.OnReadValue evaluates pki/issuer/certs eagerly to
            // detect provisioning mode (before IGNORE_PROVISIONING_MODE is even
            // consulted). Pre-create the directory so Directory.EnumerateFiles
            // returns an empty enumerable instead of throwing
            // DirectoryNotFoundException → BadUnexpectedError on every read.
            Directory.CreateDirectory(Path.Combine(WorkingDirectory, "pki", "issuer", "certs"));

            Directory.SetCurrentDirectory(WorkingDirectory);

            // Leave UACLURL unset — that is the "mock cloud library" contract:
            // the production client must short-circuit and never issue HTTP.
            Environment.SetEnvironmentVariable(_envUaCloudLibraryUrl, null);

            // Production credentials are required by Program.ValidateRequiredEnvironment;
            // values are arbitrary because this fixture serves anonymous client traffic only.
            Environment.SetEnvironmentVariable(_envOpcUaUsername, "integration-test");
            Environment.SetEnvironmentVariable(_envOpcUaPassword, "integration-test");

            // Skip TCP probes for asset endpoints — MockProtocolDriver doesn't
            // listen anywhere and we don't want false negatives when a sandboxed
            // CI runner can't reach arbitrary addresses.
            Environment.SetEnvironmentVariable(_envDisableConnectionTest, "1");

            // Provisioning mode would otherwise block all reads via OnReadValue;
            // set the bypass so the integration tests can read the SupportedWoTBindings
            // and SupportedOPCUAInfoModels properties.
            Environment.SetEnvironmentVariable(_envIgnoreProvisioningMode, "1");

            CopyEmbeddedNodeSet();

            int port = GetFreeTcpPort();
            EndpointUrl = $"opc.tcp://127.0.0.1:{port}/UA/UAEdgeTranslator";
            WriteConfigFile(port);

            Telemetry = new ConsoleTelemetry();

            // Register a deterministic, in-process protocol driver so the
            // CreateAssetForEndpoint / DiscoverAssets / OnboardAssetFromWoTFileAsync
            // paths have a real driver to exercise without any network I/O.
            // The registry is a process-global; reset its state so reruns are clean.
            ResetProgramStatics();
            Program.Drivers.Register(new MockProtocolDriver());

            App = new ApplicationInstance(Telemetry)
            {
                ApplicationType = ApplicationType.ClientAndServer,
                ConfigSectionName = "Ua.Edge.Translator"
            };

            App.LoadApplicationConfigurationAsync(false).GetAwaiter().GetResult();

            App.ApplicationConfiguration.ApplicationName = "UAEdgeTranslatorTest";
            App.ApplicationConfiguration.ApplicationUri = "urn:UAEdgeTranslatorTest";

            App.ApplicationConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;
            App.CheckApplicationInstanceCertificatesAsync(true, 0).GetAwaiter().GetResult();

            // Wire up the Program statics that UANodeManager / UAServer rely on
            // (Program.App, Program.Telemetry, Program.OpcUaUsername, Program.OpcUaPassword)
            // without invoking Program.Main (which would block on Task.Delay forever).
            SetProgramProperty(nameof(Program.App), App);
            SetProgramProperty(nameof(Program.Telemetry), Telemetry);
            SetProgramProperty(nameof(Program.OpcUaUsername), "integration-test");
            SetProgramProperty(nameof(Program.OpcUaPassword), "integration-test");

            App.StartAsync(new UAServer()).GetAwaiter().GetResult();

            // Detach the production ImpersonateUser hook installed by
            // UAServer.OnServerStarted. It only accepts UserNameIdentityToken
            // credentials hashed against env vars, which is the right contract
            // for production but blocks the integration suite from using
            // anonymous sessions to exercise the address space directly.
            // The fixture pins the server behind a per-fixture loopback port
            // and a private PKI directory, so detaching this hook is safe here.
            DetachImpersonateUserHandler();
        }

        private void DetachImpersonateUserHandler()
        {
            // CurrentInstance is the IServerInternal exposed by ApplicationInstance.
            // SessionManager.ImpersonateUser is a non-null event whose backing
            // delegate field is generated by the compiler. The field naming
            // convention in the OPC UA stack is `m_<EventName>` (private), but
            // some SDK versions use the event name directly. Walk every instance
            // field on SessionManager and clear any that holds an
            // ImpersonateEventHandler delegate so the production handler is
            // detached regardless of naming.
            try
            {
                var serverProperty = App.Server.GetType().GetProperty(
                    "CurrentInstance",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                object serverInternal = serverProperty?.GetValue(App.Server) ?? App.Server;
                object sessionManager = serverInternal?.GetType()
                    .GetProperty("SessionManager")?.GetValue(serverInternal);

                if (sessionManager == null)
                {
                    return;
                }

                Type smType = sessionManager.GetType();
                while (smType != null)
                {
                    foreach (FieldInfo field in smType.GetFields(
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (typeof(Delegate).IsAssignableFrom(field.FieldType))
                        {
                            object value = field.GetValue(sessionManager);
                            if (value is Opc.Ua.Server.ImpersonateEventHandler)
                            {
                                field.SetValue(sessionManager, null);
                            }
                        }
                    }

                    smType = smType.BaseType;
                }
            }
            catch
            {
                // best-effort: if the SDK shape changes, the integration tests
                // will fail with the same BadIdentityTokenInvalid as before and
                // make the breakage obvious.
            }
        }

        public string WorkingDirectory { get; }

        public string EndpointUrl { get; }

        public string Username { get; } = "integration-test";

        public string Password { get; } = "integration-test";

        public ApplicationInstance App { get; private set; }

        public ConsoleTelemetry Telemetry { get; private set; }

        public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                if (App != null)
                {
                    await App.StopAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // best-effort: the test must not fail because of a tear-down hiccup
            }

            try
            {
                Telemetry?.Dispose();
            }
            catch
            {
                // best-effort
            }

            ResetProgramStatics();

            try
            {
                Directory.SetCurrentDirectory(_previousWorkingDirectory);
            }
            catch
            {
                // best-effort
            }

            try
            {
                if (Directory.Exists(WorkingDirectory))
                {
                    Directory.Delete(WorkingDirectory, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup; the OS will reclaim eventually
            }

            Environment.SetEnvironmentVariable(_envOpcUaUsername, _previousUsername);
            Environment.SetEnvironmentVariable(_envOpcUaPassword, _previousPassword);
            Environment.SetEnvironmentVariable(_envDisableConnectionTest, _previousDisableConnectionTest);
            Environment.SetEnvironmentVariable(_envIgnoreProvisioningMode, _previousIgnoreProvisioningMode);
            Environment.SetEnvironmentVariable(_envUaCloudLibraryUrl, _previousUaCloudLibraryUrl);
        }

        private static int GetFreeTcpPort()
        {
            // Bind to port 0 to let the OS pick an unused ephemeral port, then
            // release it before the OPC UA server tries to claim it. There is a
            // tiny TOCTOU window but it's good enough for in-process integration tests.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private void CopyEmbeddedNodeSet()
        {
            // Locate Opc.Ua.WotCon.NodeSet2.xml in the source tree. The test
            // assembly's directory is .../Tests/UAEdgeTranslator.Tests/bin/<config>/<tfm>/,
            // and the nodeset file lives in UAServer/. Walk up until we find it.
            string assemblyDir = Path.GetDirectoryName(typeof(OpcUaServerFixture).Assembly.Location);
            string sourceFile = ResolveRepositoryFile(assemblyDir, Path.Combine("UAServer", "Opc.Ua.WotCon.NodeSet2.xml"));

            if (sourceFile == null || !File.Exists(sourceFile))
            {
                throw new FileNotFoundException(
                    "Could not locate Opc.Ua.WotCon.NodeSet2.xml in the source tree. " +
                    "OpcUaServerFixture walks up from " + assemblyDir + " looking for UAServer/Opc.Ua.WotCon.NodeSet2.xml.");
            }

            File.Copy(sourceFile, Path.Combine(WorkingDirectory, "Opc.Ua.WotCon.NodeSet2.xml"), overwrite: true);
        }

        private static string ResolveRepositoryFile(string startDir, string relativePath)
        {
            DirectoryInfo dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return null;
        }

        private void WriteConfigFile(int port)
        {
            // Minimal, integration-test-friendly application configuration. It
            // mirrors the production Ua.Edge.Translator.Config.xml but:
            //   - binds to a per-fixture loopback port so multiple fixtures cannot collide;
            //   - exposes SecurityPolicy#None and Anonymous tokens so the in-process
            //     test client can connect without a PKI dance;
            //   - uses isolated PKI store paths under the per-fixture working directory
            //     so cert generation cannot pollute the developer's repo.
            string config = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                @"<?xml version=""1.0"" encoding=""utf-8""?>
<ApplicationConfiguration
  xmlns:ua=""http://opcfoundation.org/UA/2008/02/Types.xsd""
  xmlns=""http://opcfoundation.org/UA/SDK/Configuration.xsd""
>
  <ApplicationName>UAEdgeTranslatorTest</ApplicationName>
  <ApplicationUri>urn:UAEdgeTranslatorTest</ApplicationUri>
  <ProductUri>http://opcfoundation.com/UA/EdgeTranslator/Tests</ProductUri>
  <ApplicationType>ClientAndServer_2</ApplicationType>

  <SecurityConfiguration>
    <ApplicationCertificate>
      <StoreType>Directory</StoreType>
      <StorePath>./pki/own</StorePath>
      <SubjectName>CN=UAEdgeTranslatorTest, C=US, S=Arizona, O=OPC Foundation</SubjectName>
    </ApplicationCertificate>
    <TrustedIssuerCertificates>
      <StoreType>Directory</StoreType>
      <StorePath>./pki/issuer</StorePath>
    </TrustedIssuerCertificates>
    <TrustedPeerCertificates>
      <StoreType>Directory</StoreType>
      <StorePath>./pki/trusted</StorePath>
    </TrustedPeerCertificates>
    <RejectedCertificateStore>
      <StoreType>Directory</StoreType>
      <StorePath>./pki/rejected</StorePath>
    </RejectedCertificateStore>
    <AutoAcceptUntrustedCertificates>true</AutoAcceptUntrustedCertificates>
    <RejectSHA1SignedCertificates>false</RejectSHA1SignedCertificates>
    <MinimumCertificateKeySize>2048</MinimumCertificateKeySize>
  </SecurityConfiguration>

  <TransportConfigurations/>

  <TransportQuotas>
    <OperationTimeout>120000</OperationTimeout>
    <MaxStringLength>1048576</MaxStringLength>
    <MaxByteStringLength>1048576</MaxByteStringLength>
    <MaxArrayLength>1048576</MaxArrayLength>
    <MaxMessageSize>4194304</MaxMessageSize>
    <MaxBufferSize>65535</MaxBufferSize>
    <ChannelLifetime>600000</ChannelLifetime>
    <SecurityTokenLifetime>3600000</SecurityTokenLifetime>
  </TransportQuotas>

  <ServerConfiguration>
    <BaseAddresses>
      <ua:String>opc.tcp://127.0.0.1:{0}/UA/UAEdgeTranslator</ua:String>
    </BaseAddresses>

    <SecurityPolicies>
      <ServerSecurityPolicy>
        <SecurityMode>None_1</SecurityMode>
        <SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#None</SecurityPolicyUri>
      </ServerSecurityPolicy>
    </SecurityPolicies>

    <MinRequestThreadCount>5</MinRequestThreadCount>
    <MaxRequestThreadCount>50</MaxRequestThreadCount>
    <MaxQueuedRequestCount>500</MaxQueuedRequestCount>

    <UserTokenPolicies>
      <ua:UserTokenPolicy>
        <ua:PolicyId>Anonymous</ua:PolicyId>
        <ua:TokenType>Anonymous_0</ua:TokenType>
      </ua:UserTokenPolicy>
      <ua:UserTokenPolicy>
        <ua:PolicyId>UserName</ua:PolicyId>
        <ua:TokenType>UserName_1</ua:TokenType>
        <ua:SecurityPolicyUri>http://opcfoundation.org/UA/SecurityPolicy#None</ua:SecurityPolicyUri>
      </ua:UserTokenPolicy>
    </UserTokenPolicies>

    <DiagnosticsEnabled>false</DiagnosticsEnabled>
    <MaxSessionCount>32</MaxSessionCount>
    <MinSessionTimeout>10000</MinSessionTimeout>
    <MaxSessionTimeout>3600000</MaxSessionTimeout>
    <MaxBrowseContinuationPoints>10</MaxBrowseContinuationPoints>
    <MaxQueryContinuationPoints>10</MaxQueryContinuationPoints>
    <MaxHistoryContinuationPoints>100</MaxHistoryContinuationPoints>
    <MaxRequestAge>1800000</MaxRequestAge>
    <MinPublishingInterval>100</MinPublishingInterval>
    <MaxPublishingInterval>3600000</MaxPublishingInterval>
    <PublishingResolution>50</PublishingResolution>
    <MaxSubscriptionLifetime>3600000</MaxSubscriptionLifetime>
    <MaxMessageQueueSize>100</MaxMessageQueueSize>
    <MaxNotificationQueueSize>100</MaxNotificationQueueSize>
    <MaxNotificationsPerPublish>1000</MaxNotificationsPerPublish>
    <MinMetadataSamplingInterval>1000</MinMetadataSamplingInterval>
    <AvailableSamplingRates />

    <MaxRegistrationInterval>0</MaxRegistrationInterval>
    <MinSubscriptionLifetime>10000</MinSubscriptionLifetime>
    <MaxPublishRequestCount>20</MaxPublishRequestCount>
    <MaxSubscriptionCount>100</MaxSubscriptionCount>
    <MaxEventQueueSize>10000</MaxEventQueueSize>

    <ServerProfileArray>
      <ua:String>http://opcfoundation.org/UA-Profile/Server/StandardUA2017</ua:String>
    </ServerProfileArray>

    <ShutdownDelay>0</ShutdownDelay>
    <ServerCapabilities>
      <ua:String>DA</ua:String>
    </ServerCapabilities>
    <SupportedPrivateKeyFormats>
      <ua:String>PFX</ua:String>
      <ua:String>PEM</ua:String>
    </SupportedPrivateKeyFormats>
    <MaxTrustListSize>5242880</MaxTrustListSize>
    <MultiCastDnsEnabled>false</MultiCastDnsEnabled>

    <OperationLimits>
      <MaxNodesPerRead>2500</MaxNodesPerRead>
      <MaxNodesPerHistoryReadData>1000</MaxNodesPerHistoryReadData>
      <MaxNodesPerWrite>2500</MaxNodesPerWrite>
      <MaxNodesPerMethodCall>2500</MaxNodesPerMethodCall>
      <MaxNodesPerBrowse>2500</MaxNodesPerBrowse>
      <MaxNodesPerRegisterNodes>2500</MaxNodesPerRegisterNodes>
      <MaxNodesPerTranslateBrowsePathsToNodeIds>2500</MaxNodesPerTranslateBrowsePathsToNodeIds>
      <MaxNodesPerNodeManagement>2500</MaxNodesPerNodeManagement>
      <MaxMonitoredItemsPerCall>2500</MaxMonitoredItemsPerCall>
    </OperationLimits>

    <AuditingEnabled>false</AuditingEnabled>
  </ServerConfiguration>

  <ClientConfiguration>
    <DefaultSessionTimeout>60000</DefaultSessionTimeout>
    <WellKnownDiscoveryUrls/>
    <DiscoveryServers/>
    <EndpointCacheFilePath/>
    <MinSubscriptionLifetime>10000</MinSubscriptionLifetime>
  </ClientConfiguration>

  <TraceConfiguration>
    <OutputFilePath>logs/Opc.Ua.EdgeTranslator.tests.log</OutputFilePath>
    <DeleteOnLoad>true</DeleteOnLoad>
    <TraceMasks>0</TraceMasks>
  </TraceConfiguration>

  <DisableHiResClock>true</DisableHiResClock>
</ApplicationConfiguration>",
                port);

            File.WriteAllText(Path.Combine(WorkingDirectory, "Ua.Edge.Translator.Config.xml"), config);
        }

        private static void SetProgramProperty(string propertyName, object value)
        {
            // The Program.* statics are public properties with private setters
            // (e.g. Program.App { get; private set; }). Reflection lets the
            // fixture initialize them without invoking Program.Main, which would
            // block on Task.Delay(Timeout.Infinite, ...) forever.
            PropertyInfo property = typeof(Program).GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"Program.{propertyName} not found.");

            property.SetValue(null, value);
        }

        private static void ResetProgramStatics()
        {
            SetProgramProperty(nameof(Program.App), null);
            SetProgramProperty(nameof(Program.Telemetry), null);
            SetProgramProperty(nameof(Program.OpcUaUsername), null);
            SetProgramProperty(nameof(Program.OpcUaPassword), null);

            // Program.Drivers is a get-only property whose value is a single
            // ProtocolDriverRegistry instance for the lifetime of the process.
            // Drain the registry by re-registering the existing entries with
            // throwaway scheme names is not feasible — but Register replaces
            // by scheme, so a simple "register over the top with an empty driver"
            // would still leak the test driver. Instead the registry exposes
            // AllDrivers; we use reflection to clear its internal collection.
            object registry = typeof(Program)
                .GetProperty(nameof(Program.Drivers), BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null);
            if (registry == null)
            {
                return;
            }

            FieldInfo driversField = registry.GetType().GetField(
                "_drivers",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (driversField?.GetValue(registry) is System.Collections.IDictionary driversMap)
            {
                driversMap.Clear();
            }
        }
    }
}
