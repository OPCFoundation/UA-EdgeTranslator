/*
Copyright 2020 Cognizant
Copyright 2021 Microsoft Corporation
*/

namespace  OCPPCentralSystem.Models
{
    using Newtonsoft.Json.Linq;

    public class ErrorPayload : BasePayload
    {
        public string ErrorCode { get; set; }

        public string ErrorDescription { get; set; }

        public new JArray WrappedPayload => new JArray() { MessageTypeId, UniqueId, ErrorCode, ErrorDescription, Payload };

        public ErrorPayload(string uniqueId)
        {
            MessageTypeId = 4;
            UniqueId = uniqueId;
        }

        public ErrorPayload(string uniqueId,string errorCode)
        {
            MessageTypeId = 4;
            UniqueId = uniqueId;
            ErrorCode = errorCode;
            ErrorDescription = "";
            Payload = JObject.FromObject("");
        }

        public ErrorPayload(JArray payload)
        {
            MessageTypeId = int.Parse(payload[0].ToString());
            UniqueId = payload[1].ToString();
            ErrorCode = payload[2].ToString();
            ErrorDescription = payload[3].ToString();
            Payload = (JObject)payload[4];
        }
    }
}
