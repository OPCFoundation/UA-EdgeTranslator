// MatterDotNet Copyright (C) 2025 
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published by
// the Free Software Foundation, either version 3 of the License, or any later version.
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY, without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the GNU Affero General Public License for more details.
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
// WARNING: This file was auto-generated. Do not edit.

using Matter.Core.Sessions;
using MatterDotNet.Attributes;
using MatterDotNet.Messages.InteractionModel;
using MatterDotNet.Protocol.Parsers;
using MatterDotNet.Protocol.Payloads;
using MatterDotNet.Protocol.Subprotocols;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace MatterDotNet.Clusters.General
{
    /// <summary>
    /// This cluster is used to add or remove Operational Credentials on a Commissionee or Node, as well as manage the associated Fabrics.
    /// </summary>
    //[ClusterRevision(CLUSTER_ID, 1)]
    public class OperationalCredentials : ClusterBase
    {
        internal const uint CLUSTER_ID = 0x003E;

        /// <summary>
        /// This cluster is used to add or remove Operational Credentials on a Commissionee or Node, as well as manage the associated Fabrics.
        /// </summary>
        [SetsRequiredMembers]
        public OperationalCredentials(ushort endPoint) : this(CLUSTER_ID, endPoint) { }
        /// <inheritdoc />
        [SetsRequiredMembers]
        protected OperationalCredentials(uint cluster, ushort endPoint) : base(cluster, endPoint) {
            NOCs = new ReadAttribute<NOC[]>(cluster, endPoint, 0) {
                Deserialize = x => {
                    FieldReader reader = new FieldReader((IList<object>)x!);
                    NOC[] list = new NOC[reader.Count];
                    for (int i = 0; i < reader.Count; i++)
                        list[i] = new NOC(reader.GetStruct(i)!);
                    return list;
                }
            };
            Fabrics = new ReadAttribute<FabricDescriptor[]>(cluster, endPoint, 1) {
                Deserialize = x => {
                    FieldReader reader = new FieldReader((IList<object>)x!);
                    FabricDescriptor[] list = new FabricDescriptor[reader.Count];
                    for (int i = 0; i < reader.Count; i++)
                        list[i] = new FabricDescriptor(reader.GetStruct(i)!);
                    return list;
                }
            };
            SupportedFabrics = new ReadAttribute<byte>(cluster, endPoint, 2) {
                Deserialize = x => (byte)(dynamic)x!
            };
            CommissionedFabrics = new ReadAttribute<byte>(cluster, endPoint, 3) {
                Deserialize = x => (byte)(dynamic)x!
            };
            TrustedRootCertificates = new ReadAttribute<byte[][]>(cluster, endPoint, 4) {
                Deserialize = x => {
                    FieldReader reader = new FieldReader((IList<object>)x!);
                    byte[][] list = new byte[reader.Count][];
                    for (int i = 0; i < reader.Count; i++)
                        list[i] = reader.GetBytes(i, false)!;
                    return list;
                }
            };
            CurrentFabricIndex = new ReadAttribute<byte>(cluster, endPoint, 5) {
                Deserialize = x => (byte)(dynamic)x!
            };
        }

        #region Enums
        /// <summary>
        /// Node Operational Cert Status
        /// </summary>
        public enum NodeOperationalCertStatus : byte {
            /// <summary>
            /// OK, no error
            /// </summary>
            OK = 0x0,
            /// <summary>
            /// <see cref="InvalidPublicKey"/> Public Key in the NOC does not match the public key in the NOCSR
            /// </summary>
            InvalidPublicKey = 0x1,
            /// <summary>
            /// <see cref="InvalidOperationalId"/> The Node Operational ID in the NOC is not formatted correctly.
            /// </summary>
            InvalidNodeOpId = 0x2,
            /// <summary>
            /// <see cref="InvalidNoc"/> Any other validation error in NOC chain
            /// </summary>
            InvalidNOC = 0x3,
            /// <summary>
            /// <see cref="MissingCsr"/> No record of prior CSR for which this NOC could match
            /// </summary>
            MissingCsr = 0x4,
            /// <summary>
            /// <see cref="TableFull"/> NOCs table full, cannot add another one
            /// </summary>
            TableFull = 0x5,
            /// <summary>
            /// <see cref="InvalidAdminSubject"/> Invalid CaseAdminSubject field for an AddNOC command.
            /// </summary>
            InvalidAdminSubject = 0x6,
            /// <summary>
            /// <see cref="FabricConflict"/> Trying to AddNOC instead of UpdateNOC against an existing Fabric.
            /// </summary>
            FabricConflict = 0x9,
            /// <summary>
            /// <see cref="LabelConflict"/> Label already exists on another Fabric.
            /// </summary>
            LabelConflict = 0xA,
            /// <summary>
            /// <see cref="InvalidFabricIndex"/> FabricIndex argument is invalid.
            /// </summary>
            InvalidFabricIndex = 0xB,
        }

        /// <summary>
        /// Certificate Chain Type
        /// </summary>
        public enum CertificateChainType : byte {
            /// <summary>
            /// Request the DER-encoded DAC certificate
            /// </summary>
            DACCertificate = 1,
            /// <summary>
            /// Request the DER-encoded PAI certificate
            /// </summary>
            PAICertificate = 2,
        }
        #endregion Enums

        #region Records
        /// <summary>
        /// Fabric Descriptor
        /// </summary>
        public record FabricDescriptor : TLVPayload {
            /// <summary>
            /// Fabric Descriptor
            /// </summary>
            public FabricDescriptor() { }

            /// <summary>
            /// Fabric Descriptor
            /// </summary>
            [SetsRequiredMembers]
            public FabricDescriptor(object[] fields) {
                FieldReader reader = new FieldReader(fields);
                RootPublicKey = reader.GetBytes(1, false, 65)!;
                VendorID = reader.GetUShort(2)!.Value;
                FabricID = reader.GetULong(3)!.Value;
                NodeID = reader.GetULong(4)!.Value;
                Label = reader.GetString(5, false, 32)!;
            }
            public required byte[] RootPublicKey { get; set; }
            public required ushort VendorID { get; set; }
            public required ulong FabricID { get; set; }
            public required ulong NodeID { get; set; }
            public required string Label { get; set; }
            internal override void Serialize(TLVWriter writer, long structNumber = -1) {
                writer.StartStructure(structNumber);
                writer.WriteBytes(1, RootPublicKey, 65);
                writer.WriteUShort(2, VendorID);
                writer.WriteULong(3, FabricID);
                writer.WriteULong(4, NodeID);
                writer.WriteString(5, Label, 32);
                writer.EndContainer();
            }
        }

        /// <summary>
        /// NOC
        /// </summary>
        public record NOC : TLVPayload {
            /// <summary>
            /// NOC
            /// </summary>
            public NOC() { }

            /// <summary>
            /// NOC
            /// </summary>
            [SetsRequiredMembers]
            public NOC(object[] fields) {
                FieldReader reader = new FieldReader(fields);
                NOCField = reader.GetBytes(1, false)!;
                ICAC = reader.GetBytes(2, false)!;
            }
            public required byte[] NOCField { get; set; }
            public required byte[] ICAC { get; set; }
            internal override void Serialize(TLVWriter writer, long structNumber = -1) {
                writer.StartStructure(structNumber);
                writer.WriteBytes(1, NOCField);
                writer.WriteBytes(2, ICAC);
                writer.EndContainer();
            }
        }
        #endregion Records

        #region Payloads
        private record AttestationRequestPayload : TLVPayload {
            public required byte[] AttestationNonce { get; set; }
            internal override void Serialize(TLVWriter writer, long structNumber = -1) {
                writer.StartStructure(structNumber);
                writer.WriteBytes(0, AttestationNonce, 32);
                writer.EndContainer();
            }
        }

        /// <summary>
        /// Attestation Response - Reply from server
        /// </summary>
        public struct AttestationResponse() {
            public required byte[] AttestationElements { get; set; }
            public required byte[] AttestationSignature { get; set; }
        }

        private record CertificateChainRequestPayload : TLVPayload {
            public required CertificateChainType CertificateType { get; set; }
            internal override void Serialize(TLVWriter writer, long structNumber = -1) {
                writer.StartStructure(structNumber);
                writer.WriteUShort(0, (ushort)CertificateType);
                writer.EndContainer();
            }
        }

        /// <summary>
        /// Certificate Chain Response - Reply from server
        /// </summary>
        public struct CertificateChainResponse() {
            public required byte[] Certificate { get; set; }
        }

        private record CSRRequestPayload : TLVPayload {
            public required byte[] CSRNonce { get; set; }
            public bool? IsForUpdateNOC { get; set; }
            internal override void Serialize(TLVWriter writer, long structNumber = -1) {
                writer.StartStructure(structNumber);
                writer.WriteBytes(0, CSRNonce, 32);
                if (IsForUpdateNOC != null)
                    writer.WriteBool(1, IsForUpdateNOC);
                writer.EndContainer();
            }
        }

        /// <summary>
        /// CSR Response - Reply from server
        /// </summary>
        public struct CSRResponse() {
            public required byte[] NOCSRElements { get; set; }
            public required byte[] AttestationSignature { get; set; }
        }

        private record AddNOCPayload : TLVPayload {
            public required byte[] NOCValue { get; set; }
            public byte[] ICACValue { get; set; }
            public required byte[] IPKValue { get; set; }
            public required ulong CaseAdminSubject { get; set; }
            public required ushort AdminVendorId { get; set; }
            internal override void Serialize(TLVWriter writer, long structNumber = -1) {
                writer.StartStructure(structNumber);
                writer.WriteBytes(0, NOCValue, 400);
                if (ICACValue != null)
                    writer.WriteBytes(1, ICACValue, 400);
                writer.WriteBytes(2, IPKValue, 16);
                writer.WriteULong(3, CaseAdminSubject);
                writer.WriteUShort(4, AdminVendorId);
                writer.EndContainer();
            }
        }

        private record UpdateNOCPayload : TLVPayload {
            public required byte[] NOCValue { get; set; }
            public byte[] ICACValue { get; set; }
            internal override void Serialize(TLVWriter writer, long structNumber = -1) {
                writer.StartStructure(structNumber);
                writer.WriteBytes(0, NOCValue);
                if (ICACValue != null)
                    writer.WriteBytes(1, ICACValue);
                writer.EndContainer();
            }
        }

        /// <summary>
        /// NOC Response - Reply from server
        /// </summary>
        public struct NOCResponse() {
            public required NodeOperationalCertStatus StatusCode { get; set; }
            public byte? FabricIndex { get; set; }
            public string DebugText { get; set; }
        }

        private record UpdateFabricLabelPayload : TLVPayload {
            public required string Label { get; set; }
            internal override void Serialize(TLVWriter writer, long structNumber = -1) {
                writer.StartStructure(structNumber);
                writer.WriteString(0, Label, 32);
                writer.EndContainer();
            }
        }

        private record RemoveFabricPayload : TLVPayload {
            public required byte FabricIndex { get; set; }
            internal override void Serialize(TLVWriter writer, long structNumber = -1) {
                writer.StartStructure(structNumber);
                writer.WriteByte(0, FabricIndex);
                writer.EndContainer();
            }
        }

        private record AddTrustedRootCertificatePayload : TLVPayload {
            public required byte[] RootCACertificate { get; set; }
            internal override void Serialize(TLVWriter writer, long structNumber = -1) {
                writer.StartStructure(structNumber);
                writer.WriteBytes(0, RootCACertificate);
                writer.EndContainer();
            }
        }
        #endregion Payloads

        #region Commands
        /// <summary>
        /// Attestation Request
        /// </summary>
        public async Task<AttestationResponse?> AttestationRequest(SecureSession session, byte[] attestationNonce, CancellationToken token = default) {
            AttestationRequestPayload requestFields = new AttestationRequestPayload() {
                AttestationNonce = attestationNonce,
            };
            InvokeResponseIB resp = await InteractionManager.ExecCommand(session, endPoint, cluster, 0x00, requestFields, token);
            if (!ValidateResponse(resp))
                return null;
            return new AttestationResponse() {
                AttestationElements = (byte[])GetField(resp, 0),
                AttestationSignature = (byte[])GetField(resp, 1),
            };
        }

        /// <summary>
        /// Certificate Chain Request
        /// </summary>
        public async Task<CertificateChainResponse?> CertificateChainRequest(SecureSession session, CertificateChainType certificateType, CancellationToken token = default) {
            CertificateChainRequestPayload requestFields = new CertificateChainRequestPayload() {
                CertificateType = certificateType,
            };
            InvokeResponseIB resp = await InteractionManager.ExecCommand(session, endPoint, cluster, 0x02, requestFields, token);
            if (!ValidateResponse(resp))
                return null;
            return new CertificateChainResponse() {
                Certificate = (byte[])GetField(resp, 0),
            };
        }

        /// <summary>
        /// CSR Request
        /// </summary>
        public async Task<CSRResponse?> CSRRequest(SecureSession session, byte[] cSRNonce, bool? isForUpdateNOC, CancellationToken token = default) {
            CSRRequestPayload requestFields = new CSRRequestPayload() {
                CSRNonce = cSRNonce,
                IsForUpdateNOC = isForUpdateNOC,
            };
            InvokeResponseIB resp = await InteractionManager.ExecCommand(session, endPoint, cluster, 0x04, requestFields, token);
            if (!ValidateResponse(resp))
                return null;
            return new CSRResponse() {
                NOCSRElements = (byte[])GetField(resp, 0),
                AttestationSignature = (byte[])GetField(resp, 1),
            };
        }

        /// <summary>
        /// Add NOC
        /// </summary>
        public async Task<NOCResponse?> AddNOC(SecureSession session, byte[] nOCValue, byte[] iCACValue, byte[] iPKValue, ulong caseAdminSubject, ushort adminVendorId, CancellationToken token = default) {
            AddNOCPayload requestFields = new AddNOCPayload() {
                NOCValue = nOCValue,
                ICACValue = iCACValue,
                IPKValue = iPKValue,
                CaseAdminSubject = caseAdminSubject,
                AdminVendorId = adminVendorId,
            };
            InvokeResponseIB resp = await InteractionManager.ExecCommand(session, endPoint, cluster, 0x06, requestFields, token);
            if (!ValidateResponse(resp))
                return null;
            return new NOCResponse() {
                StatusCode = (NodeOperationalCertStatus)(byte)GetField(resp, 0),
                FabricIndex = (byte)GetOptionalField(resp, 1),
                DebugText = (string)GetOptionalField(resp, 2),
            };
        }

        /// <summary>
        /// Update NOC
        /// </summary>
        public async Task<NOCResponse?> UpdateNOC(SecureSession session, byte[] nOCValue, byte[] iCACValue, CancellationToken token = default) {
            UpdateNOCPayload requestFields = new UpdateNOCPayload() {
                NOCValue = nOCValue,
                ICACValue = iCACValue,
            };
            InvokeResponseIB resp = await InteractionManager.ExecCommand(session, endPoint, cluster, 0x07, requestFields, token);
            if (!ValidateResponse(resp))
                return null;
            return new NOCResponse() {
                StatusCode = (NodeOperationalCertStatus)(byte)GetField(resp, 0),
                FabricIndex = (byte)GetOptionalField(resp, 1),
                DebugText = (string)GetOptionalField(resp, 2),
            };
        }

        /// <summary>
        /// Update Fabric Label
        /// </summary>
        public async Task<NOCResponse?> UpdateFabricLabel(SecureSession session, string label, CancellationToken token = default) {
            UpdateFabricLabelPayload requestFields = new UpdateFabricLabelPayload() {
                Label = label,
            };
            InvokeResponseIB resp = await InteractionManager.ExecCommand(session, endPoint, cluster, 0x09, requestFields, token);
            if (!ValidateResponse(resp))
                return null;
            return new NOCResponse() {
                StatusCode = (NodeOperationalCertStatus)(byte)GetField(resp, 0),
                FabricIndex = (byte)GetOptionalField(resp, 1),
                DebugText = (string)GetOptionalField(resp, 2),
            };
        }

        /// <summary>
        /// Remove Fabric
        /// </summary>
        public async Task<NOCResponse?> RemoveFabric(SecureSession session, byte fabricIndex, CancellationToken token = default) {
            RemoveFabricPayload requestFields = new RemoveFabricPayload() {
                FabricIndex = fabricIndex,
            };
            InvokeResponseIB resp = await InteractionManager.ExecCommand(session, endPoint, cluster, 0x0a, requestFields, token);
            if (!ValidateResponse(resp))
                return null;
            return new NOCResponse() {
                StatusCode = (NodeOperationalCertStatus)(byte)GetField(resp, 0),
                FabricIndex = (byte)GetOptionalField(resp, 1),
                DebugText = (string)GetOptionalField(resp, 2),
            };
        }

        /// <summary>
        /// Add Trusted Root Certificate
        /// </summary>
        public async Task<bool> AddTrustedRootCertificate(SecureSession session, byte[] rootCACertificate, CancellationToken token = default) {
            AddTrustedRootCertificatePayload requestFields = new AddTrustedRootCertificatePayload() {
                RootCACertificate = rootCACertificate,
            };
            InvokeResponseIB resp = await InteractionManager.ExecCommand(session, endPoint, cluster, 0x0b, requestFields, token);
            return ValidateResponse(resp);
        }
        #endregion Commands

        #region Attributes
        /// <summary>
        /// NO Cs Attribute [Read Only]
        /// </summary>
        public required ReadAttribute<NOC[]> NOCs { get; init; }

        /// <summary>
        /// Fabrics Attribute [Read Only]
        /// </summary>
        public required ReadAttribute<FabricDescriptor[]> Fabrics { get; init; }

        /// <summary>
        /// Supported Fabrics Attribute [Read Only]
        /// </summary>
        public required ReadAttribute<byte> SupportedFabrics { get; init; }

        /// <summary>
        /// Commissioned Fabrics Attribute [Read Only]
        /// </summary>
        public required ReadAttribute<byte> CommissionedFabrics { get; init; }

        /// <summary>
        /// Trusted Root Certificates Attribute [Read Only]
        /// </summary>
        public required ReadAttribute<byte[][]> TrustedRootCertificates { get; init; }

        /// <summary>
        /// Current Fabric Index Attribute [Read Only]
        /// </summary>
        public required ReadAttribute<byte> CurrentFabricIndex { get; init; }
        #endregion Attributes

        /// <inheritdoc />
        public override string ToString() {
            return "Operational Credentials";
        }
    }
}
