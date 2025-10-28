using Newtonsoft.Json;
using System.IO;

namespace Matter.Core
{
    public class FabricDiskStorage
    {
        public bool FabricExists()
        {
            return Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric"));
        }

        public Fabric LoadFabric()
        {
            Fabric fabric = null;
            var allFiles = Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric"));

            foreach (var file in allFiles)
            {
                if (file.EndsWith("fabric.json"))
                {
                    fabric = JsonConvert.DeserializeObject<Fabric>(File.ReadAllText(file));
                }
                //else if (file.EndsWith("rootCertificate.pem"))
                //{
                //    PemReader pemReader = new PemReader(new StreamReader(file));
                //    fabric.RootCACertificate = pemReader.ReadObject() as X509Certificate;
                //}
                //else if (file.EndsWith("rootKeyPair.pem"))
                //{
                //    PemReader pemReader = new PemReader(new StreamReader(file));
                //    fabric.RootCAKeyPair = pemReader.ReadObject() as AsymmetricCipherKeyPair;
                //}
                //else if (file.EndsWith("operationalCertificate.pem"))
                //{
                //    PemReader pemReader = new PemReader(new StreamReader(file));
                //    fabric.OperationalCertificate = pemReader.ReadObject() as X509Certificate;
                //}
                //else if (file.EndsWith("operationalKeyPair.pem"))
                //{
                //    PemReader pemReader = new PemReader(new StreamReader(file));
                //    fabric.OperationalCertificateKeyPair = pemReader.ReadObject() as AsymmetricCipherKeyPair;
                //}
            }

            return fabric;
        }

        public void SaveFabric(Fabric fabric)
        {
            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric")))
            {
                //Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric"));

                //File.WriteAllTextAsync(Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric", "fabric.json"), JsonConvert.SerializeObject(fabric));

                //PemWriter pemWriter = new PemWriter(new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric", "rootCertificate.pem")));
                //pemWriter.WriteObject(fabric.RootCACertificate);
                //pemWriter.Writer.Flush();
                //pemWriter.Writer.Close();

                //pemWriter = new PemWriter(new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric", "rootKeyPair.pem")));
                //pemWriter.WriteObject(fabric.RootCAKeyPair);
                //pemWriter.Writer.Flush();
                //pemWriter.Writer.Close();

                //pemWriter = new PemWriter(new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric", "operationalCertificate.pem")));
                //pemWriter.WriteObject(fabric.OperationalCertificate);
                //pemWriter.Writer.Flush();
                //pemWriter.Writer.Close();

                //pemWriter = new PemWriter(new StreamWriter(Path.Combine(Directory.GetCurrentDirectory(), "pki/fabric", "operationalKeyPair.pem")));
                //pemWriter.WriteObject(fabric.OperationalCertificateKeyPair);
                //pemWriter.Writer.Flush();
                //pemWriter.Writer.Close();
            }
        }
    }
}
