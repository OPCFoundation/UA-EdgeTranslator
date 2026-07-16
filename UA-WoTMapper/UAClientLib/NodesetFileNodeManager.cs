using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Opc.Ua;
using Opc.Ua.Export;
using Opc.Ua.Server;

namespace WotOpcUaMapper.UAClientLib
{
    /// <summary>
    /// Custom node manager that imports NodeSet2 XML files into the address space so that
    /// OPC UA types and instances can be browsed. Ported from the UA Cloud Library reference
    /// (DB/value-patching dependencies removed).
    /// </summary>
    public class NodesetFileNodeManager : CustomNodeManager2
    {
        public NodesetFileNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            NamespaceUris = new List<string>();
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out _))
            {
                externalReferences[ObjectIds.ObjectsFolder] = new List<IReference>();
            }
        }

        public void AddNamespace(string nodesetXml)
        {
            if (string.IsNullOrEmpty(nodesetXml))
            {
                return;
            }

            using Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(nodesetXml));
            UANodeSet nodeSet = UANodeSet.Read(stream);

            if ((nodeSet.NamespaceUris == null) || (nodeSet.NamespaceUris.Length == 0))
            {
                return;
            }

            List<string> newNamespaceUris = nodeSet.NamespaceUris.ToList();
            List<string> existingNamespaceUris = NamespaceUris.ToList();

            foreach (string ns in newNamespaceUris)
            {
                if (!existingNamespaceUris.Contains(ns))
                {
                    lock (Lock)
                    {
                        existingNamespaceUris.Add(ns);

                        // update the table used by this NodeManager
                        SetNamespaces(existingNamespaceUris.ToArray());

                        // register the new URI with the MasterNodeManager
                        Server.NodeManager.RegisterNamespaceManager(ns, this);
                    }
                }
            }
        }

        public void AddNodes(string nodesetXml)
        {
            if (string.IsNullOrEmpty(nodesetXml))
            {
                return;
            }

            using Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(nodesetXml));

            // import nodes
            UANodeSet nodeSet = UANodeSet.Read(stream);
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            nodeSet.Import(SystemContext, predefinedNodes);

            // add nodes
            for (int i = 0; i < predefinedNodes.Count; i++)
            {
                try
                {
                    AddPredefinedNode(SystemContext, predefinedNodes[i]);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message, ex);
                }
            }

            // add references for our top-level nodes to the objects folder
            Server.NodeManager.GetManagerHandle(ObjectIds.ObjectsFolder, out INodeManager objectsFolderNodeManager);
            string namespaceUri = nodeSet.NamespaceUris[0];

            foreach (UANode node in nodeSet.Items)
            {
                if (node is UAObject uAObject)
                {
                    if ((uAObject.ParentNodeId == ObjectIds.ObjectsFolder.ToString())
                     || (uAObject.References.Where(r => (r.ReferenceType == "Organizes") && (r.Value == ObjectIds.ObjectsFolder.ToString())).ToList().Count > 0))
                    {
                        if ((node.DisplayName != null) && (node.DisplayName.Length > 0))
                        {
                            List<IReference> references = new()
                            {
                                new NodeStateReference(
                                    ReferenceTypeIds.Organizes,
                                    false,
                                    new NodeId(NodeId.Parse(uAObject.NodeId).Identifier,
                                    (ushort)Server.NamespaceUris.GetIndex(namespaceUri))
                                )
                            };

                            Dictionary<NodeId, IList<IReference>> dictionary = new()
                            {
                                { ObjectIds.ObjectsFolder, references }
                            };

                            objectsFolderNodeManager.AddReferences(dictionary);
                        }
                    }
                }
            }
        }
    }
}
