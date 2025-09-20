using Matter.Core.Commissioning;
using Matter.Core.Fabrics;
using Matter.Core.Sessions;
using Opc.Ua.Edge.Translator.ProtocolDrivers.MatterController.Models;
using Org.BouncyCastle.Math;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Matter.Core
{
    public class MatterController : IMatterController
    {
        private readonly FabricManager _fabricManager;
        //private readonly mDNSService _mDNSService;
        //private readonly MulticastService _mDNSService;
        //private readonly ServiceDiscovery _serviceDiscovery;
        private readonly ISessionManager _sessionManager;
        private readonly INodeRegister _nodeRegister;

        private Fabric _fabric;
        private Dictionary<int, ICommissioner> _commissioners;

        //public event IMatterController.ReconnectedToNode ReconnectedToNodeEvent;
        public event IMatterController.CommissionableNodeDiscovered CommissionableNodeDiscoveredEvent;
        public event IMatterController.MatterNodeAddedToFabric MatterNodeAddedToFabricEvent;

        public MatterController(IFabricStorageProvider fabricStorageProvider)
        {
            _fabricManager = new FabricManager(fabricStorageProvider);
            _commissioners = new Dictionary<int, ICommissioner>();
            _nodeRegister = new NodeRegister();

            //_nodeRegister.CommissionableNodeDiscoveredEvent += (object sender, CommissionableNodeDiscoveredEventArgs args) =>
            //{
            //    CommissionableNodeDiscoveredEvent?.Invoke(this);
            //};

            _sessionManager = new SessionManager(_nodeRegister);

            //_mDNSService = new mDNSService(new NullLogger<mDNSService>());
            //_mDNSService = new MulticastService();
            //_serviceDiscovery = new ServiceDiscovery(_mDNSService);
        }

        public Task<ICommissioner> CreateCommissionerAsync()
        {
            if (_fabric == null)
            {
                throw new InvalidOperationException($"Fabric not initialized. Call {nameof(InitAsync)}() first.");
            }

            ICommissioner commissioner = new NetworkCommissioner(_fabric, _nodeRegister);

            _commissioners.Add(commissioner.Id, commissioner);

            return Task.FromResult(commissioner);
        }

        public async Task InitAsync()
        {
            // Start the mDNS service to discover nodes.
            //
            //_mDNSService.ServiceDiscovered += (sender, args) =>
            //_mDNSService.NetworkInterfaceDiscovered += (s, e) =>
            //{
            //    _mDNSService.SendQuery("_matterc._udp.local");
            //    _mDNSService.SendQuery("_matter._tcp.local");
            //};

            //_mDNSService.AnswerReceived += _mDNSService_AnswerReceived;
            //_serviceDiscovery.ServiceDiscovered += _serviceDiscovery_ServiceDiscovered;
            //_serviceDiscovery.ServiceInstanceDiscovered += _serviceDiscovery_ServiceInstanceDiscovered;

            _fabric = await _fabricManager.GetAsync("Test");
            _fabric.NodeAdded += OnNodeAddedToFabric;
        }

        //private void _mDNSService_AnswerReceived(object sender, MessageEventArgs e)
        //{
        //    var servers = e.Message.Answers.OfType<SRVRecord>();

        //    foreach (var server in servers)
        //    {
        //        var instanceName = server.Name.ToString();

        //        //Console.WriteLine($"Processing '{instanceName}'");

        //        if (instanceName.Contains("_matter._tcp.local"))
        //        {
        //            //Console.WriteLine($"Discovered Commissioned Node '{instanceName}'");

        //            var addresses = e.Message.AdditionalRecords.OfType<AddressRecord>();

        //            if (!addresses.Any())
        //            {
        //                continue;
        //            }

        //            _nodeRegister.AddCommissionedNode(instanceName.Replace("_matter._tcp.local", ""), server.Port, addresses.Select(a => a.Address.ToString()).ToArray());
        //        }
        //        else if (instanceName.Contains("_matterc._udp.local"))
        //        {
        //            //Console.WriteLine($"Discovered Commissionable Node '{instanceName}'");

        //            var txtRecords = e.Message.AdditionalRecords.OfType<TXTRecord>().Union(e.Message.Answers.OfType<TXTRecord>());

        //            var recordWithDiscriminator = txtRecords.FirstOrDefault(x => x.Strings.Any(y => y.StartsWith("D=")));

        //            ushort discriminator = 0;

        //            if (recordWithDiscriminator is not null)
        //            {
        //                var discriminatorString = recordWithDiscriminator.Strings.Single(x => x.StartsWith("D="));
        //                discriminator = ushort.Parse(discriminatorString.Substring(2)); // Remove "d=" prefix
        //            }

        //            var addresses = e.Message.AdditionalRecords.OfType<AddressRecord>().Union(e.Message.Answers.OfType<AddressRecord>())    ;

        //            if (discriminator == 0 || !addresses.Any())
        //            {
        //                Console.WriteLine($"Commissionable Node '{instanceName}' is missing data. Requesting more...");
        //                _mDNSService.SendQuery(server.Target, type: DnsType.A);
        //                _mDNSService.SendQuery(server.Target, type: DnsType.AAAA);
        //                _mDNSService.SendQuery(server.Target, type: DnsType.TXT);
        //                continue;
        //            }

        //            _nodeRegister.AddCommissionableNode(instanceName.Replace("_matterc._udp.local", ""), discriminator, server.Port, addresses.Select(a => a.Address.ToString()).ToArray());
        //        }
        //    }
        //}

        //private void _serviceDiscovery_ServiceInstanceDiscovered(object sender, ServiceInstanceDiscoveryEventArgs e)
        //{
        //    //Console.WriteLine($"Service Instance Discovered '{e.ServiceInstanceName}'");
        //    _mDNSService.SendQuery(e.ServiceInstanceName, type: DnsType.SRV);
        //}

        //private void _serviceDiscovery_ServiceDiscovered(object sender, DomainName serviceName)
        //{
        //    //Console.WriteLine($"Service Discovered '{serviceName}'");
        //    _mDNSService.SendQuery(serviceName, type: DnsType.PTR);
        //}

        public async Task RunAsync()
        {
            if (_fabric == null)
            {
                throw new InvalidOperationException($"Fabric not initialized. Call {nameof(InitAsync)}() first.");
            }

            // Start the mDNS service to discover commissionable and commissioned nodes.
            //
            //_mDNSService.Perform(new ServiceDiscovery("_matter._tcp.local.", "_matterc._udp.local."));
            //_mDNSService.Start();

            // Reconnect to the nodes we have already commissioned.
            //
            await _sessionManager.Start(_fabric!);
        }

        private void OnNodeAddedToFabric(object sender, NodeAddedToFabricEventArgs args)
        {
            MatterNodeAddedToFabricEvent?.Invoke(this, new MatterNodeAddedToFabricEventArgs()
            {

            });
        }

        public Task<IEnumerable<Node>> GetNodesAsync()
        {
            return Task.FromResult(_fabric.Nodes.AsEnumerable());
        }

        public Task<Node> GetNodeAsync(BigInteger nodeId)
        {
            return Task.FromResult(_fabric.Nodes.First(x => x.NodeId.ToString() == nodeId.ToString()));
        }
    }
}
