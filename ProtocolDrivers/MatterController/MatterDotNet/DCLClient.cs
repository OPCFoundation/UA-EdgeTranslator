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

using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace MatterDotNet.DCL
{
    /// <summary>
    /// Client to query the distributed common ledger
    /// </summary>
    public class DCLClient
    {
        const string DefaultTestEndPoint = "https://on.test-net.dcl.csa-iot.org";
        const string DefaultProductionEndPoint = "https://on.dcl.csa-iot.org/";

        /// <summary>
        /// Matter PAA Trust Store
        /// </summary>
        public X509Certificate2Collection TrustStore { get; init; }

        /// <summary>
        /// CHIP Test PAA
        /// </summary>
        public X509Certificate2 CHIPTestPAA { get; init; }

        /// <summary>
        /// Create a new client loading data from the file system or web if not available
        /// </summary>
        public DCLClient()
        {
            CHIPTestPAA = X509Certificate2.CreateFromPem("-----BEGIN CERTIFICATE-----\nMIIBkTCCATegAwIBAgIHC4+6qN2G7jAKBggqhkjOPQQDAjAaMRgwFgYDVQQDDA9NYXR0ZXIgVGVzdCBQQUEwIBcNMjEwNjI4MTQyMzQzWhgPOTk5OTEyMzEyMzU5NTlaMBoxGDAWBgNVBAMMD01hdHRlciBUZXN0IFBBQTBZMBMGByqGSM49AgEGCCqGSM49AwEHA0IABBDvAqgah7aBIfuo0xl4+AejF+UKqKgoRGgokUuTPejt1KXDnJ/3GkzjZH/X9iZTt9JJX8ukwPR/h2iAA54HIEqjZjBkMBIGA1UdEwEB/wQIMAYBAf8CAQEwDgYDVR0PAQH/BAQDAgEGMB0GA1UdDgQWBBR4XOcFuGuPTm/Hk6pgy0PqaWiC1TAfBgNVHSMEGDAWgBR4XOcFuGuPTm/Hk6pgy0PqaWiC1TAKBggqhkjOPQQDAgNIADBFAiEAue/bPqBqUuwL8B5h2u0sLRVt22zwFBAdq3mPrAX6R+UCIGAGHT411g2dSw1Eja12EvfoXFguP8MS3Bh5TdNzcV5d\n-----END CERTIFICATE-----");
            if (File.Exists("paa.truststore"))
            {
                #if NET9_0_OR_GREATER
                    TrustStore = X509CertificateLoader.LoadPkcs12CollectionFromFile("paa.truststore", null);
                #else
                    TrustStore = new X509Certificate2Collection();
                    TrustStore.Import("paa.truststore", null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);
                #endif
            }
            else
            {
                Task<X509Certificate2Collection> load = GetCertificates();
                load.Wait();
                TrustStore = load.Result;
                File.WriteAllBytes("paa.truststore", TrustStore.Export(X509ContentType.Pkcs12)!);
            }
        }

        private static async Task<X509Certificate2Collection> GetCertificates(CancellationToken token = default)
        {
            HttpClient client = new HttpClient();
            Certificates certs = (Certificates)await client.GetFromJsonAsync(DefaultProductionEndPoint + "/dcl/pki/certificates", typeof(Certificates), token);
            X509Certificate2Collection collection = new X509Certificate2Collection();
            if (certs == null)
                return collection;
            foreach (Approvedcertificate approvedCert in certs.approvedCertificates)
            {
                foreach (Cert cert in approvedCert.certs)
                    collection.Add(X509Certificate2.CreateFromPem(cert.pemCert));
            }
            return collection;
        }
    }
}
