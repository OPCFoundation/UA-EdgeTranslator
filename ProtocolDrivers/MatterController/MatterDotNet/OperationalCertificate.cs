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

using MatterDotNet.DCL;
using MatterDotNet.Messages.Certificates;
using MatterDotNet.Protocol.Payloads;
using MatterDotNet.Util;
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MatterDotNet.PKI
{
    /// <summary>
    /// A Matter operational certificate (X509v3 with Matter DNs)
    /// </summary>
    public class OperationalCertificate
    {
        /// <summary>
        /// The underlying X509 certificate
        /// </summary>
        protected X509Certificate2 cert;

        internal const string OID_CommonName = "2.5.4.3";
        internal const string OID_Surname = "2.5.4.4";
        internal const string OID_SerialNum = "2.5.4.5";
        internal const string OID_CountryName = "2.5.4.6";
        internal const string OID_LocalityName = "2.5.4.7";
        internal const string OID_StateOrProvinceName = "2.5.4.8";
        internal const string OID_OrgName = "2.5.4.10";
        internal const string OID_OrgUnitName = "2.5.4.11";
        internal const string OID_Title = "2.5.4.12";
        internal const string OID_NodeId = "1.3.6.1.4.1.37244.1.1";
        internal const string OID_FirmwareSigning = "1.3.6.1.4.1.37244.1.2";
        internal const string OID_ICAC = "1.3.6.1.4.1.37244.1.3";
        internal const string OID_RCAC = "1.3.6.1.4.1.37244.1.4";
        internal const string OID_FabricID = "1.3.6.1.4.1.37244.1.5";
        internal const string OID_NOCCat = "1.3.6.1.4.1.37244.1.6";
        internal const string OID_VendorID = "1.3.6.1.4.1.37244.2.1";
        internal const string OID_ProductID = "1.3.6.1.4.1.37244.2.2";
        internal const string OID_ServerAuth = "1.3.6.1.5.5.7.3.1";
        internal const string OID_ClientAuth = "1.3.6.1.5.5.7.3.2";

        /// <summary>
        /// Create a new operational certificate (cert must be set)
        /// </summary>
        protected OperationalCertificate() { }

        /// <summary>
        /// Create a new operational certificate from a der encoded X509 certificate
        /// </summary>
        /// <param name="cert"></param>
        public OperationalCertificate(byte[] cert)
        {
            #if NET9_0_OR_GREATER
                this.cert = X509CertificateLoader.LoadCertificate(cert);
            #else
                this.cert = new X509Certificate2(cert);
            #endif
            ParseCert();
        }

        /// <summary>
        /// Create a new operational certificate from the provided X509 certificate
        /// </summary>
        /// <param name="cert"></param>
        internal OperationalCertificate(X509Certificate2 cert)
        {
            this.cert = cert;
            ParseCert();
        }

        /// <summary>
        /// Parse the DNs in the provided certificate
        /// </summary>
        protected void ParseCert()
        {
            foreach (X500RelativeDistinguishedName dn in cert.SubjectName.EnumerateRelativeDistinguishedNames(false))
            {
                switch (dn.GetSingleElementType().Value)
                {
                    case OID_CommonName:
                        CommonName = dn.GetSingleElementValue()!;
                        break;
                    case OID_Surname:
                        Surname = dn.GetSingleElementValue()!;
                        break;
                    case OID_SerialNum:
                        SerialNum = dn.GetSingleElementValue()!;
                        break;
                    case OID_CountryName:
                        CountryName = dn.GetSingleElementValue()!;
                        break;
                    case OID_LocalityName:
                        LocalityName = dn.GetSingleElementValue()!;
                        break;
                    case OID_StateOrProvinceName:
                        StateOrProvinceName = dn.GetSingleElementValue()!;
                        break;
                    case OID_OrgName:
                        OrgName = dn.GetSingleElementValue()!;
                        break;
                    case OID_OrgUnitName:
                        OrgUnitName = dn.GetSingleElementValue()!;
                        break;
                    case OID_Title:
                        Title = dn.GetSingleElementValue()!;
                        break;
                    case OID_NodeId:
                        if (ulong.TryParse(dn.GetSingleElementValue()!, NumberStyles.HexNumber, null, out ulong id))
                            NodeID = id;
                        break;
                    case OID_FirmwareSigning:
                        if (ulong.TryParse(dn.GetSingleElementValue()!, NumberStyles.HexNumber, null, out ulong firmware))
                            FirmwareSigningID = firmware;
                        break;
                    case OID_ICAC:
                        if (ulong.TryParse(dn.GetSingleElementValue()!, NumberStyles.HexNumber, null, out ulong icac))
                            ICAC = icac;
                        break;
                    case OID_RCAC:
                        if (ulong.TryParse(dn.GetSingleElementValue()!, NumberStyles.HexNumber, null, out ulong rcac))
                            RCAC = rcac;
                        break;
                    case OID_FabricID:
                        if (ulong.TryParse(dn.GetSingleElementValue()!, NumberStyles.HexNumber, null, out ulong fabric))
                            FabricID = fabric;
                        break;
                    case OID_NOCCat:
                        if (uint.TryParse(dn.GetSingleElementValue()!, NumberStyles.HexNumber, null, out uint cat))
                            Cats.Add(new CASEAuthenticatedTag(cat));
                        break;
                    case OID_VendorID:
                        if (uint.TryParse(dn.GetSingleElementValue()!, NumberStyles.HexNumber, null, out uint vid))
                            VendorID = vid;
                        break;
                    case OID_ProductID:
                        if (uint.TryParse(dn.GetSingleElementValue()!, NumberStyles.HexNumber, null, out uint pid))
                            ProductID = pid;
                        break;
                }
            }
            foreach (X500RelativeDistinguishedName dn in cert.IssuerName.EnumerateRelativeDistinguishedNames(false))
            {
                switch (dn.GetSingleElementType().Value)
                {
                    case OID_CommonName:
                            IssuerCommonName = dn.GetSingleElementValue()!;
                            break;
                }
            }
        }

        /// <summary>
        /// Verify the certificate chains to the provided intermediate certificate and root PAA
        /// </summary>
        /// <param name="intermediateCert"></param>
        /// <param name="dcl"></param>
        /// <param name="level"></param>
        /// <returns></returns>
        public bool VerifyChain(byte[] intermediateCert, DCLClient dcl, VerificationLevel level)
        {
            ArgumentNullException.ThrowIfNull(intermediateCert, nameof(intermediateCert));
            ArgumentNullException.ThrowIfNull(dcl, nameof(dcl));
            if (level == VerificationLevel.AnyDevice)
                return true;
            #if NET9_0_OR_GREATER
                return VerifyChain(X509CertificateLoader.LoadCertificate(intermediateCert), dcl, level);
            #else
                return VerifyChain(new X509Certificate2(intermediateCert), dcl, level);
            #endif
        }

        /// <summary>
        /// Verify the certificate chains to the provided intermediate certificate and root PAA
        /// </summary>
        /// <param name="intermediateCert"></param>
        /// <param name="dcl"></param>
        /// <param name="level"></param>
        /// <returns></returns>
        public bool VerifyChain(X509Certificate2 intermediateCert, DCLClient dcl, VerificationLevel level)
        {
            if (level == VerificationLevel.AnyDevice)
                return true;
            X509Chain chain = new X509Chain();
            chain.ChainPolicy.ExtraStore.Add(intermediateCert);
            chain.ChainPolicy.CustomTrustStore.AddRange(dcl.TrustStore);
            if (level == VerificationLevel.CertifiedDevicesOrCHIPTest)
                chain.ChainPolicy.CustomTrustStore.Add(dcl.CHIPTestPAA);
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            bool valid = chain.Build(cert);

            return valid;
        }

        /// <summary>
        /// Verify the certificate chains to the provided root certificate
        /// </summary>
        /// <param name="rootCert"></param>
        /// <returns></returns>
        public bool VerifyChain(OperationalCertificate rootCert)
        {
            X509Chain chain = new X509Chain();
            chain.ChainPolicy.CustomTrustStore.Add(rootCert.cert);
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(cert);
        }

        public void Export(string path)
        {
            File.WriteAllBytes(path, cert.Export(X509ContentType.Cert));
        }

        private byte[] GetSignature(AsnEncodingRules encodingRules = AsnEncodingRules.DER)
        {
            var signedData = cert.RawDataMemory;
            AsnDecoder.ReadSequence(signedData.Span, encodingRules, out var offset, out var length, out _);

            var certificateSpan = signedData.Span.Slice(offset, length);
            AsnDecoder.ReadSequence( certificateSpan, encodingRules, out var tbsOffset, out var tbsLength, out _);

            var algorithmSpan = certificateSpan.Slice(tbsOffset + tbsLength);
            AsnDecoder.ReadSequence(algorithmSpan, encodingRules, out var algOffset, out var algLength, out _);

            byte[] signatureSequence = AsnDecoder.ReadBitString(algorithmSpan.Slice(algOffset + algLength), encodingRules, out _, out _ );
            AsnDecoder.ReadSequence(signatureSequence, encodingRules, out var sigOffset, out int sigLength, out _);
            BigInteger part1 = AsnDecoder.ReadInteger(signatureSequence.AsSpan(sigOffset, sigLength), encodingRules, out var intLen);
            BigInteger part2 = AsnDecoder.ReadInteger(signatureSequence.AsSpan(sigOffset + intLen), encodingRules, out _);

            byte[] signature = new byte[64];
            byte[] part1bytes = part1.ToByteArray(true, true);
            Array.Copy(part1bytes, 0, signature, 32 - part1bytes.Length, part1bytes.Length);
            byte[] part2bytes = part2.ToByteArray(true, true);
            Array.Copy(part2bytes, 0, signature, 64 - part2bytes.Length, part2bytes.Length);
            return signature;
        }

        private ushort GetKeyUsage(X509KeyUsageFlags keyUsage)
        {
            ushort ret = 0x0;
            if ((keyUsage & X509KeyUsageFlags.EncipherOnly) != 0)
                ret |= 0x80;
            if ((keyUsage & X509KeyUsageFlags.CrlSign) != 0)
                ret |= 0x40;
            if ((keyUsage & X509KeyUsageFlags.KeyCertSign) != 0)
                ret |= 0x20;
            if ((keyUsage & X509KeyUsageFlags.KeyAgreement) != 0)
                ret |= 0x10;
            if ((keyUsage & X509KeyUsageFlags.DataEncipherment) != 0)
                ret |= 0x8;
            if ((keyUsage & X509KeyUsageFlags.KeyEncipherment) != 0)
                ret |= 0x4;
            if ((keyUsage & X509KeyUsageFlags.NonRepudiation) != 0)
                ret |= 0x2;
            if ((keyUsage & X509KeyUsageFlags.DigitalSignature) != 0)
                ret |= 0x1;
            if ((keyUsage & X509KeyUsageFlags.DecipherOnly) != 0)
                ret |= 0x100;
            return ret;
        }

        private uint[] GetExtendedKeyUsage(X509EnhancedKeyUsageExtension extended)
        {
            List<uint> extUsage = new List<uint>();
            foreach (Oid oid in extended.EnhancedKeyUsages)
            {
                if (uint.TryParse(oid.Value!.Split('.').Last(), out uint val))
                {
                    if (val < 5)
                        extUsage.Add(val);
                    else if (val == 8)
                        extUsage.Add(5);
                    else if (val == 9)
                        extUsage.Add(6);
                }
            }
            return extUsage.ToArray();
        }

        private static List<DnAttribute> GetDNs(X500DistinguishedName subject)
        {
            List<DnAttribute> attrs = new List<DnAttribute>();
            foreach (X500RelativeDistinguishedName dn in subject.EnumerateRelativeDistinguishedNames(false))
            {
                switch (dn.GetSingleElementType().Value)
                {
                    case OID_CommonName:
                        attrs.Add(new DnAttribute() { CommonName = dn.GetSingleElementValue() });
                        break;
                    case OID_Surname:
                        attrs.Add(new DnAttribute() { Surname = dn.GetSingleElementValue() });
                        break;
                    case OID_SerialNum:
                        attrs.Add(new DnAttribute() { SerialNum = dn.GetSingleElementValue() });
                        break;
                    case OID_CountryName:
                        attrs.Add(new DnAttribute() { CountryName = dn.GetSingleElementValue() });
                        break;
                    case OID_LocalityName:
                        attrs.Add(new DnAttribute() { LocalityName = dn.GetSingleElementValue() });
                        break;
                    case OID_StateOrProvinceName:
                        attrs.Add(new DnAttribute() { StateOrProvinceName = dn.GetSingleElementValue() });
                        break;
                    case OID_OrgName:
                        attrs.Add(new DnAttribute() { OrgName = dn.GetSingleElementValue() });
                        break;
                    case OID_OrgUnitName:
                        attrs.Add(new DnAttribute() { OrgUnitName = dn.GetSingleElementValue() });
                        break;
                    case OID_Title:
                        attrs.Add(new DnAttribute() { Title = dn.GetSingleElementValue() });
                        break;
                    case OID_NodeId:
                        if (ulong.TryParse(dn.GetSingleElementValue(), NumberStyles.HexNumber, null, out ulong id))
                            attrs.Add(new DnAttribute() { MatterNodeId = id });
                        break;
                    case OID_FirmwareSigning:
                        if (ulong.TryParse(dn.GetSingleElementValue(), NumberStyles.HexNumber, null, out ulong firmware))
                            attrs.Add(new DnAttribute() { MatterFirmwareSigningId = firmware });
                        break;
                    case OID_ICAC:
                        if (ulong.TryParse(dn.GetSingleElementValue(), NumberStyles.HexNumber, null, out ulong icac))
                            attrs.Add(new DnAttribute() { MatterIcacId = icac });
                        break;
                    case OID_RCAC:
                        if (ulong.TryParse(dn.GetSingleElementValue(), NumberStyles.HexNumber, null, out ulong rcac))
                            attrs.Add(new DnAttribute() { MatterRcacId = rcac });
                        break;
                    case OID_FabricID:
                        if (ulong.TryParse(dn.GetSingleElementValue(), NumberStyles.HexNumber, null, out ulong fabric))
                            attrs.Add(new DnAttribute() { MatterFabricId = fabric });
                        break;
                    case OID_NOCCat:
                        if (uint.TryParse(dn.GetSingleElementValue(), NumberStyles.HexNumber, null, out uint noc))
                            attrs.Add(new DnAttribute() { MatterNocCat = noc });
                        break;
                }
            }

            return attrs;
        }

        /// <summary>
        /// Convert an operational certificate into a matter certificate
        /// </summary>
        /// <returns></returns>
        public MatterCertificate ToMatterCertificate()
        {
            List<Extension> extensions = new List<Extension>();
            foreach (X509Extension ext in cert.Extensions)
            {
                if (ext is X509BasicConstraintsExtension basic)
                {
                    BasicConstraints constraint = new BasicConstraints() { IsCa = basic.CertificateAuthority };
                    if (basic.HasPathLengthConstraint)
                        constraint.PathLenConstraint = (byte)basic.PathLengthConstraint;
                    extensions.Add(new Extension() { BasicCnstr = constraint });
                }
                else if (ext is X509KeyUsageExtension keyUsage)
                    extensions.Add(new Extension() { KeyUsage = GetKeyUsage(keyUsage.KeyUsages) });
                else if (ext is X509EnhancedKeyUsageExtension extended)
                    extensions.Add(new Extension() { ExtendedKeyUsage = GetExtendedKeyUsage(extended) });
                else if (ext is X509SubjectKeyIdentifierExtension subKey)
                    extensions.Add(new Extension() { SubjectKeyId = subKey.SubjectKeyIdentifierBytes.ToArray() });
                else if (ext is X509AuthorityKeyIdentifierExtension authKey)
                    extensions.Add(new Extension() { AuthorityKeyId = authKey.KeyIdentifier!.Value.ToArray() });
            }
            return new MatterCertificate()
            {
                EcCurveId = 0x1,
                PubKeyAlgo = 0x1,
                SigAlgo = 0x1,
                EcPubKey = cert.GetPublicKey(),
                SerialNum = cert.SerialNumberBytes.ToArray(),
                NotBefore = TimeUtil.ToEpochSeconds(cert.NotBefore),
                NotAfter = TimeUtil.ToEpochSeconds(cert.NotAfter),
                Signature = GetSignature(),
                Extensions = extensions,
                Issuer = GetDNs(cert.IssuerName),
                Subject = GetDNs(cert.SubjectName)
            };
        }

        /// <summary>
        /// Convert an operational certificate into a matter certificate (in byte[] form)
        /// </summary>
        /// <returns></returns>
        public byte[] GetMatterCertBytes()
        {
            PayloadWriter payload = new PayloadWriter(600);
            ToMatterCertificate().Serialize(payload);
            return payload.GetPayload().ToArray();
        }

        internal X509Certificate2 GetRaw()
        {
            return cert;
        }

        /// <summary>
        /// Compute an ECDsa Signature
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public byte[] SignData(byte[] message)
        {
            if (!cert.HasPrivateKey)
                return null;
            return cert.GetECDsaPrivateKey()?.SignData(message, HashAlgorithmName.SHA256);
        }

        /// <summary>
        /// Verify an ECDsa Signature
        /// </summary>
        /// <param name="message"></param>
        /// <param name="signature"></param>
        /// <returns></returns>
        public bool VerifyData(byte[] message, byte[] signature)
        {
            return cert.GetECDsaPublicKey()?.VerifyData(message, signature, HashAlgorithmName.SHA256) ?? false;
        }

        public string IssuerCommonName { get; protected set; } = string.Empty;

        public string CommonName { get; protected set; } = string.Empty;

        public string Surname { get; private set; }

        public string LocalityName { get; private set; }

        public string CountryName { get; private set; }

        public string OrgName { get; private set; }

        public string OrgUnitName { get; private set; }

        public string Title { get; private set; }

        public string StateOrProvinceName { get; private set; }

        public string SerialNum { get; private set; }

        public ulong? NodeID { get; private set; }

        public ulong? FirmwareSigningID { get; private set; }

        public ulong? ICAC { get; private set; }

        public ulong? RCAC { get; protected set; }

        public ulong? FabricID { get; protected set; }

        public List<CASEAuthenticatedTag> Cats { get; private set; } = [];

        /// <summary>
        /// Node Vendor ID
        /// </summary>
        public uint VendorID { get; private set; }

        /// <summary>
        /// Node Product ID
        /// </summary>
        public uint ProductID { get; private set; }

        /// <summary>
        /// Public ECDsa Key
        /// </summary>
        public byte[] PublicKey { get { return cert.GetPublicKey(); } }
    }
}
