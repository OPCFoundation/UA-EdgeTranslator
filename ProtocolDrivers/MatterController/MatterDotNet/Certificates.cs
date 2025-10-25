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

namespace MatterDotNet.DCL
{
    #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    #pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public class Certificates
    {

        public Approvedcertificate[] approvedCertificates { get; set; }
        public Pagination pagination { get; set; }
    }

    public class Pagination
    {
        public object next_key { get; set; }
        public string total { get; set; }
    }

    public class Approvedcertificate
    {
        public string subject { get; set; }
        public string subjectKeyId { get; set; }
        public Cert[] certs { get; set; }
        public int schemaVersion { get; set; }
    }

    public class Cert
    {
        public string pemCert { get; set; }
        public string serialNumber { get; set; }
        public string issuer { get; set; }
        public string authorityKeyId { get; set; }
        public string rootSubject { get; set; }
        public string rootSubjectKeyId { get; set; }
        public bool isRoot { get; set; }
        public string owner { get; set; }
        public string subject { get; set; }
        public string subjectKeyId { get; set; }
        public Approval[] approvals { get; set; }
        public string subjectAsText { get; set; }
        public Reject[] rejects { get; set; }
        public int vid { get; set; }
        public string certificateType { get; set; }
        public int schemaVersion { get; set; }
    }

    public class Approval
    {
        public string address { get; set; }
        public string time { get; set; }
        public string info { get; set; }
        public int schemaVersion { get; set; }
    }

    public class Reject
    {
        public string address { get; set; }
        public string time { get; set; }
        public string info { get; set; }
        public int schemaVersion { get; set; }
    }
    #pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    #pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
}
