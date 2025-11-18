
namespace Matter.Core
{
    using log4net.Core;
    using Opc.Ua.Edge.Translator.Models;
    using PacketDotNet.LLDP;
    using System;
    using System.Formats.Tar;
    using System.Threading.Tasks;

    [Flags]
    public enum ImStatus : byte
    {
        Success = 0x00,
        Failure = 0x01,
        InvalidSubscription = 0x7D,
        UnsupportedAccess = 0x7E,
        UnsupportedEndpoint = 0x7F,
        InvalidAction = 0x80,
        UnsupportedCommand = 0x81,
        Omitted = 0x82,
        InvalidCommand = 0x85,
        UnsupportedAttribute = 0x86,
        ConstraintError = 0x87,
        UnsupportedWrite = 0x88,
        ResourceExhausted = 0x89,
        NotFound = 0x8B,
        UnreportableAttribute = 0x8C,
        InvalidDataType = 0x8D,
        UnsupportedRead = 0x8E,
        DataVersionMismatch = 0x8F,
        Timeout = 0x90,
        Busy = 0x91,
        UnsupportedCluster = 0xC3
    }

    /// <summary>
    /// Parsed result of an Interaction Model StatusResponse.
    /// </summary>
    public class StatusResponseResult
    {
        public ImStatus ImStatus { get; init; }

        /// <summary>
        /// Optional, present when the device included a cluster-specific status (Matter cluster status code).
        /// </summary>
        public ushort? ClusterStatus { get; init; }

        /// <summary>
        /// Interaction Model revision, if present (tag 0xFF).
        /// </summary>
        public byte? InteractionModelRevision { get; init; }

        public bool IsSuccess => ImStatus == ImStatus.Success && (ClusterStatus is null || ClusterStatus == 0);

        private const byte TAG_STATUS = 0;                      // u8
        private const byte TAG_CLUSTER_STATUS = 1;              // u16 (optional)
        private const byte TAG_INTERACTION_MODEL_REV = 0xFF;    // u8

        /// <summary>
        /// Parse a StatusResponse TLV payload (top-level structure) into a typed result.
        /// Tolerant to extra/unknown fields.
        /// </summary>
        public static StatusResponseResult Parse(MatterTLV payload)
        {
            var result = new StatusResponseResult();
            bool sawStatus = false;

            payload.OpenStructure();

            while (!payload.IsEndContainerNext())
            {
                int? tag = payload.PeekTagNumber();
                byte elemType = payload.PeekElementType();

                // IM Revision (optional, anywhere)
                if (tag == TAG_INTERACTION_MODEL_REV)
                {
                    result = new StatusResponseResult {
                        ImStatus = result.ImStatus,
                        ClusterStatus = result.ClusterStatus,
                        InteractionModelRevision = (byte)payload.GetUnsignedInt(TAG_INTERACTION_MODEL_REV)
                    };

                    continue;
                }

                // Nested StatusIB case: a structure containing status/clusterStatus
                if (elemType == (byte)ElementType.Structure)
                {
                    // Enter the nested structure (tag could be 0 or vendor-specific)
                    payload.OpenStructure(tag);

                    while (!payload.IsEndContainerNext())
                    {
                        int? innerTag = payload.PeekTagNumber();
                        if (innerTag == TAG_STATUS)
                        {
                            result = new StatusResponseResult {
                                ImStatus = (ImStatus)payload.GetUnsignedInt(TAG_STATUS),
                                ClusterStatus = result.ClusterStatus,
                                InteractionModelRevision = result.InteractionModelRevision
                            };

                            sawStatus = true;
                        }
                        else if (innerTag == TAG_CLUSTER_STATUS)
                        {
                            result = new StatusResponseResult {
                                ImStatus = result.ImStatus,
                                ClusterStatus = checked((ushort)payload.GetUnsignedInt(TAG_CLUSTER_STATUS)),
                                InteractionModelRevision = result.InteractionModelRevision
                            };
                        }
                        else
                        {
                            // Skip unknown fields safely
                            payload.GetObject(innerTag);
                        }
                    }

                    payload.CloseContainer();
                    continue;
                }

                // Flattened (no nested structure): fields appear directly at top level
                if (tag == TAG_STATUS)
                {
                    result = new StatusResponseResult {
                        ImStatus = (ImStatus)payload.GetUnsignedInt(TAG_STATUS),
                        ClusterStatus = result.ClusterStatus,
                        InteractionModelRevision = result.InteractionModelRevision
                    };

                    sawStatus = true;
                }
                else if (tag == TAG_CLUSTER_STATUS)
                {
                    result = new StatusResponseResult {
                        ImStatus = result.ImStatus,
                        ClusterStatus = checked((ushort)payload.GetUnsignedInt(TAG_CLUSTER_STATUS)),
                        InteractionModelRevision = result.InteractionModelRevision
                    };
                }
                else
                {
                    // Unknown top-level field → skip
                    payload.GetObject(tag);
                }
            }

            payload.CloseContainer();

            if (!sawStatus)
            {
                Console.WriteLine("Status response: Required field 'status' (tag 0) not found.");
            }

            return result;
        }
    }
}
