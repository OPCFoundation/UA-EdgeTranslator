namespace Opc.Ua.Edge.Translator.Tests.Integration
{
    using Newtonsoft.Json;
    using Opc.Ua;
    using Opc.Ua.Client;
    using Opc.Ua.Configuration;
    using Opc.Ua.Edge.Translator;
    using Opc.Ua.Edge.Translator.Models;
    using Opc.Ua.Edge.Translator.ProtocolDrivers;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Boots the live OPC UA server with a pre-seeded WoT TD jsonld file under
    /// <c>settings/</c>. This drives the <see cref="UANodeManager"/>'s
    /// <c>LoadLocalWoTFilesAsync</c> path and the full
    /// <c>OnboardAssetFromWoTFileAsync</c> pipeline (Properties, Actions,
    /// AddNodeForWoTForm, AddTag, ReadAssetTags), which dramatically increases
    /// branch coverage of UANodeManager without any Cloud Library round-trip.
    /// </summary>
    public sealed class OpcUaServerSeededTdIntegrationTests : IAsyncLifetime, IDisposable
    {
        private const string _envOpcUaUsername = "OPCUA_USERNAME";
        private const string _envOpcUaPassword = "OPCUA_PASSWORD";
        private const string _envDisableConnectionTest = "DISABLE_ASSET_CONNECTION_TEST";
        private const string _envIgnoreProvisioningMode = "IGNORE_PROVISIONING_MODE";
        private const string _envUaCloudLibraryUrl = "UACLURL";

        private string _previousWorkingDirectory;
        private string _previousUsername;
        private string _previousPassword;
        private string _previousDisableConnectionTest;
        private string _previousIgnoreProvisioningMode;
        private string _previousUaCloudLibraryUrl;
        private string _workingDirectory;
        private string _endpointUrl;
        private ApplicationInstance _app;
        private Opc.Ua.Cloud.ConsoleTelemetry _telemetry;
        private ISession _session;

        public async Task InitializeAsync()
        {
            _previousWorkingDirectory = Directory.GetCurrentDirectory();
            _previousUsername = Environment.GetEnvironmentVariable(_envOpcUaUsername);
            _previousPassword = Environment.GetEnvironmentVariable(_envOpcUaPassword);
            _previousDisableConnectionTest = Environment.GetEnvironmentVariable(_envDisableConnectionTest);
            _previousIgnoreProvisioningMode = Environment.GetEnvironmentVariable(_envIgnoreProvisioningMode);
            _previousUaCloudLibraryUrl = Environment.GetEnvironmentVariable(_envUaCloudLibraryUrl);

            _workingDirectory = Path.Combine(Path.GetTempPath(), "uaedge-seeded-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workingDirectory);
            Directory.CreateDirectory(Path.Combine(_workingDirectory, "settings"));
            Directory.CreateDirectory(Path.Combine(_workingDirectory, "nodesets"));
            Directory.CreateDirectory(Path.Combine(_workingDirectory, "drivers"));
            Directory.CreateDirectory(Path.Combine(_workingDirectory, "logs"));
            Directory.CreateDirectory(Path.Combine(_workingDirectory, "pki", "issuer", "certs"));

            // Pre-seed two TD files so LoadLocalWoTFilesAsync runs through the full
            // onboarding workflow at server start. One has a safe name, one is unsafe
            // and exercises the IsSafeAssetName rejection branch.
            File.WriteAllText(
                Path.Combine(_workingDirectory, "settings", "seededasset.jsonld"),
                BuildSampleTdJson("seededasset"));

            File.WriteAllText(
                Path.Combine(_workingDirectory, "settings", "with space.jsonld"),
                BuildSampleTdJson("with space"));

            Directory.SetCurrentDirectory(_workingDirectory);

            Environment.SetEnvironmentVariable(_envUaCloudLibraryUrl, null);
            Environment.SetEnvironmentVariable(_envOpcUaUsername, "integration-test");
            Environment.SetEnvironmentVariable(_envOpcUaPassword, "integration-test");
            Environment.SetEnvironmentVariable(_envDisableConnectionTest, "1");
            Environment.SetEnvironmentVariable(_envIgnoreProvisioningMode, "1");

            CopyEmbeddedNodeSet();

            int port = GetFreeTcpPort();
            _endpointUrl = $"opc.tcp://127.0.0.1:{port}/UA/UAEdgeTranslator";
            WriteConfigFile(port);

            _telemetry = new Opc.Ua.Cloud.ConsoleTelemetry();

            ResetProgramStatics();
            Program.Drivers.Register(new MockProtocolDriver());

            _app = new ApplicationInstance(_telemetry)
            {
                ApplicationType = ApplicationType.ClientAndServer,
                ConfigSectionName = "Ua.Edge.Translator"
            };

            await _app.LoadApplicationConfigurationAsync(false).ConfigureAwait(false);
            _app.ApplicationConfiguration.ApplicationName = "UAEdgeTranslatorTest";
            _app.ApplicationConfiguration.ApplicationUri = "urn:UAEdgeTranslatorTest";
            _app.ApplicationConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates = true;
            await _app.CheckApplicationInstanceCertificatesAsync(true, 0).ConfigureAwait(false);

            SetProgramProperty(nameof(Program.App), _app);
            SetProgramProperty(nameof(Program.Telemetry), _telemetry);
            SetProgramProperty(nameof(Program.OpcUaUsername), "integration-test");
            SetProgramProperty(nameof(Program.OpcUaPassword), "integration-test");

            await _app.StartAsync(new UAServer()).ConfigureAwait(false);

            DetachImpersonateUserHandler();

            _session = await CreateClientSessionAsync().ConfigureAwait(false);
        }

        public async Task DisposeAsync()
        {
            if (_session != null)
            {
                try { await _session.CloseAsync().ConfigureAwait(false); } catch { }
                _session.Dispose();
                _session = null;
            }

            try { if (_app != null) await _app.StopAsync().ConfigureAwait(false); } catch { }

            try { _telemetry?.Dispose(); } catch { }

            ResetProgramStatics();

            try { Directory.SetCurrentDirectory(_previousWorkingDirectory); } catch { }
            try { if (Directory.Exists(_workingDirectory)) Directory.Delete(_workingDirectory, recursive: true); } catch { }

            Environment.SetEnvironmentVariable(_envOpcUaUsername, _previousUsername);
            Environment.SetEnvironmentVariable(_envOpcUaPassword, _previousPassword);
            Environment.SetEnvironmentVariable(_envDisableConnectionTest, _previousDisableConnectionTest);
            Environment.SetEnvironmentVariable(_envIgnoreProvisioningMode, _previousIgnoreProvisioningMode);
            Environment.SetEnvironmentVariable(_envUaCloudLibraryUrl, _previousUaCloudLibraryUrl);
        }

        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

        [Fact]
        public async Task SeededAsset_is_visible_in_address_space_after_startup()
        {
            // The TD pre-placed under settings/ must drive UANodeManager's
            // LoadLocalWoTFilesAsync -> OnboardAssetFromWoTFileAsync pipeline at
            // startup. Browse AssetManagement and verify the asset is present.
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            ReferenceDescriptionCollection children = await BrowseChildrenAsync(assetManagement).ConfigureAwait(false);

            Assert.Contains(children, r => string.Equals(r.DisplayName.Text, "seededasset", StringComparison.Ordinal));
            // The unsafe-name file must NOT have produced an asset.
            Assert.DoesNotContain(children, r => string.Equals(r.DisplayName.Text, "with space", StringComparison.Ordinal));
        }

        [Fact]
        public async Task SeededAsset_temperature_node_can_be_browsed()
        {
            NodeId assetManagement = await ResolveAssetManagementNodeIdAsync().ConfigureAwait(false);
            ReferenceDescriptionCollection children = await BrowseChildrenAsync(assetManagement).ConfigureAwait(false);
            ReferenceDescription assetRef = children.First(r =>
                string.Equals(r.DisplayName.Text, "seededasset", StringComparison.Ordinal));

            NodeId assetNodeId = ExpandedNodeId.ToNodeId(assetRef.NodeId, _session.NamespaceUris);
            ReferenceDescriptionCollection assetChildren = await BrowseChildrenAsync(assetNodeId).ConfigureAwait(false);

            Assert.Contains(assetChildren, r => string.Equals(r.BrowseName.Name, "temperature", StringComparison.Ordinal));
            Assert.Contains(assetChildren, r => string.Equals(r.BrowseName.Name, "reset", StringComparison.Ordinal));
        }

        [Fact]
        public async Task SeededAsset_namespace_uri_is_published()
        {
            DataValue value = await _session.ReadValueAsync(VariableIds.Server_NamespaceArray).ConfigureAwait(false);
            string[] namespaces = Assert.IsType<string[]>(value.Value);
            Assert.Contains("http://opcfoundation.org/UA/seededasset/", namespaces);
        }

        // ----------------- helpers -----------------

        private static string BuildSampleTdJson(string name)
        {
            ThingDescription td = new()
            {
                Context = new object[] { "https://www.w3.org/2022/wot/td/v1.1" },
                Id = "urn:" + name,
                SecurityDefinitions = new SecurityDefinitions { NosecSc = new NosecSc { Scheme = "nosec" } },
                Security = new[] { "nosec_sc" },
                Type = new[] { "Thing" },
                Name = name,
                Base = $"{MockProtocolDriver.MockScheme}://device-seeded:1502/1",
                Title = name,
                Description = "Pre-seeded TD for integration coverage.",
                Properties = new Dictionary<string, Property>
                {
                    ["temperature"] = new Property
                    {
                        Type = TypeEnum.Number,
                        ReadOnly = true,
                        Forms = new object[]
                        {
                            new
                            {
                                href = "/temperature",
                                type = "xsd:float",
                                pollingTime = 1000L
                            }
                        }
                    },
                    ["constantString"] = new Property
                    {
                        Type = TypeEnum.String,
                        Const = "factory-default"
                    },
                    ["constantInt"] = new Property
                    {
                        Type = TypeEnum.Integer,
                        Const = 7
                    },
                    ["constantBool"] = new Property
                    {
                        Type = TypeEnum.Boolean,
                        Const = true
                    },
                    ["constantNumber"] = new Property
                    {
                        Type = TypeEnum.Number,
                        Const = 1.5
                    }
                },
                Actions = new Dictionary<string, TDAction>
                {
                    ["reset"] = new TDAction
                    {
                        Forms = new object[]
                        {
                            new { href = "/reset", type = "xsd:string" }
                        }
                    },
                    ["compute"] = new TDAction
                    {
                        Input = new TDArguments
                        {
                            Type = TypeEnum.Object,
                            Properties = new Dictionary<string, Property>
                            {
                                ["x"] = new Property { Type = TypeEnum.Integer },
                                ["y"] = new Property { Type = TypeEnum.Number },
                                ["flag"] = new Property { Type = TypeEnum.Boolean },
                                ["payload"] = new Property { Type = TypeEnum.Object }
                            }
                        },
                        Output = new TDArguments
                        {
                            Type = TypeEnum.Object,
                            Properties = new Dictionary<string, Property>
                            {
                                ["sum"] = new Property { Type = TypeEnum.Number },
                                ["msg"] = new Property { Type = TypeEnum.String }
                            }
                        },
                        Forms = new object[]
                        {
                            new { href = "/compute", type = "xsd:string" }
                        }
                    }
                }
            };

            return JsonConvert.SerializeObject(td);
        }

        private static int GetFreeTcpPort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            try { return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }

        private void CopyEmbeddedNodeSet()
        {
            string assemblyDir = Path.GetDirectoryName(typeof(OpcUaServerSeededTdIntegrationTests).Assembly.Location);
            string sourceFile = ResolveRepositoryFile(assemblyDir, Path.Combine("UAServer", "Opc.Ua.WotCon.NodeSet2.xml"));
            if (sourceFile == null || !File.Exists(sourceFile))
            {
                throw new FileNotFoundException("Could not locate Opc.Ua.WotCon.NodeSet2.xml in the source tree.");
            }
            File.Copy(sourceFile, Path.Combine(_workingDirectory, "Opc.Ua.WotCon.NodeSet2.xml"), overwrite: true);
        }

        private static string ResolveRepositoryFile(string startDir, string relativePath)
        {
            DirectoryInfo dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        private void WriteConfigFile(int port)
        {
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
    <TrustedIssuerCertificates><StoreType>Directory</StoreType><StorePath>./pki/issuer</StorePath></TrustedIssuerCertificates>
    <TrustedPeerCertificates><StoreType>Directory</StoreType><StorePath>./pki/trusted</StorePath></TrustedPeerCertificates>
    <RejectedCertificateStore><StoreType>Directory</StoreType><StorePath>./pki/rejected</StorePath></RejectedCertificateStore>
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
    <BaseAddresses><ua:String>opc.tcp://127.0.0.1:{0}/UA/UAEdgeTranslator</ua:String></BaseAddresses>
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
      <ua:UserTokenPolicy><ua:PolicyId>Anonymous</ua:PolicyId><ua:TokenType>Anonymous_0</ua:TokenType></ua:UserTokenPolicy>
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
    <ServerProfileArray><ua:String>http://opcfoundation.org/UA-Profile/Server/StandardUA2017</ua:String></ServerProfileArray>
    <ShutdownDelay>0</ShutdownDelay>
    <ServerCapabilities><ua:String>DA</ua:String></ServerCapabilities>
    <SupportedPrivateKeyFormats><ua:String>PFX</ua:String><ua:String>PEM</ua:String></SupportedPrivateKeyFormats>
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

            File.WriteAllText(Path.Combine(_workingDirectory, "Ua.Edge.Translator.Config.xml"), config);
        }

        private static void SetProgramProperty(string propertyName, object value)
        {
            System.Reflection.PropertyInfo property = typeof(Program).GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            property.SetValue(null, value);
        }

        private static void ResetProgramStatics()
        {
            SetProgramProperty(nameof(Program.App), null);
            SetProgramProperty(nameof(Program.Telemetry), null);
            SetProgramProperty(nameof(Program.OpcUaUsername), null);
            SetProgramProperty(nameof(Program.OpcUaPassword), null);

            object registry = typeof(Program)
                .GetProperty(nameof(Program.Drivers), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .GetValue(null);
            if (registry == null) return;

            System.Reflection.FieldInfo driversField = registry.GetType().GetField(
                "_drivers",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (driversField?.GetValue(registry) is System.Collections.IDictionary driversMap)
            {
                driversMap.Clear();
            }
        }

        private void DetachImpersonateUserHandler()
        {
            try
            {
                var serverProperty = _app.Server.GetType().GetProperty(
                    "CurrentInstance",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

                object serverInternal = serverProperty?.GetValue(_app.Server) ?? _app.Server;
                object sessionManager = serverInternal?.GetType()
                    .GetProperty("SessionManager")?.GetValue(serverInternal);

                if (sessionManager == null) return;

                Type smType = sessionManager.GetType();
                while (smType != null)
                {
                    foreach (System.Reflection.FieldInfo field in smType.GetFields(
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
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
                // best-effort
            }
        }

        private async Task<ISession> CreateClientSessionAsync()
        {
            EndpointDescription selectedEndpoint = await CoreClientUtils.SelectEndpointAsync(
                _app.ApplicationConfiguration,
                _endpointUrl,
                useSecurity: false,
                _telemetry).ConfigureAwait(false);

            ConfiguredEndpoint configuredEndpoint = new ConfiguredEndpoint(
                null,
                selectedEndpoint,
                EndpointConfiguration.Create(_app.ApplicationConfiguration));

            return await new DefaultSessionFactory(_telemetry).CreateAsync(
                _app.ApplicationConfiguration,
                configuredEndpoint,
                updateBeforeConnect: true,
                checkDomain: false,
                "UAEdgeTranslator seeded TD integration tests",
                30_000,
                new UserIdentity(),
                null).ConfigureAwait(false);
        }

        private async Task<NodeId> ResolveAssetManagementNodeIdAsync()
        {
            ReferenceDescriptionCollection children = await BrowseChildrenAsync(ObjectIds.ObjectsFolder).ConfigureAwait(false);
            ReferenceDescription am = children.FirstOrDefault(
                r => string.Equals(r.BrowseName.Name, "WoTAssetConnectionManagement", StringComparison.Ordinal));
            Assert.NotNull(am);
            return ExpandedNodeId.ToNodeId(am.NodeId, _session.NamespaceUris);
        }

        private async Task<ReferenceDescriptionCollection> BrowseChildrenAsync(NodeId parent)
        {
            BrowseDescription browse = new BrowseDescription
            {
                NodeId = parent,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };

            BrowseResponse response = await _session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescriptionCollection { browse },
                System.Threading.CancellationToken.None).ConfigureAwait(false);

            Assert.NotEmpty(response.Results);
            return response.Results[0].References ?? new ReferenceDescriptionCollection();
        }
    }
}
