using Newtonsoft.Json;
using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Matter.Core
{
    // Converter for X509Certificate2
    public class X509Certificate2JsonConverter : JsonConverter<X509Certificate2>
    {
        public override void WriteJson(JsonWriter writer, X509Certificate2 value, JsonSerializer serializer)
        {
            writer.WriteValue(Convert.ToBase64String(value.Export(X509ContentType.Pkcs12, string.Empty)));
        }

        public override X509Certificate2 ReadJson(JsonReader reader, Type objectType, X509Certificate2 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            return X509CertificateLoader.LoadPkcs12(Convert.FromBase64String((string)reader.Value), string.Empty);
        }
    }

    // Converter for ECDsa
    public class ECDsaJsonConverter : JsonConverter<ECDsa>
    {
        private class KeyObj
        {
            public string Curve { get; set; }

            public string X { get; set; }

            public string Y { get; set; }

            public string D { get; set; }
        }

        public override void WriteJson(JsonWriter writer, ECDsa value, JsonSerializer serializer)
        {
            var parameters = value.ExportParameters(true);
            var keyObj = new
            {
                Curve = parameters.Curve.Oid.Value,
                X = Convert.ToBase64String(parameters.Q.X),
                Y = Convert.ToBase64String(parameters.Q.Y),
                D = parameters.D != null ? Convert.ToBase64String(parameters.D) : null
            };

            serializer.Serialize(writer, keyObj);
        }

        public override ECDsa ReadJson(JsonReader reader, Type objectType, ECDsa existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var keyObj = serializer.Deserialize<KeyObj>(reader);
            var parameters = new ECParameters
            {
                Curve = ECCurve.CreateFromOid(new Oid(keyObj.Curve)),
                Q = new ECPoint
                {
                    X = Convert.FromBase64String(keyObj.X),
                    Y = Convert.FromBase64String(keyObj.Y)
                },
                D = keyObj.D != null ? Convert.FromBase64String(keyObj.D) : null
            };

            return ECDsa.Create(parameters);
        }
    }
}
