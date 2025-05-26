/*
Copyright 2020 Cognizant
Copyright 2021 Microsoft Corporation
*/

namespace OCPPCentralSystem.Models
{
    using Newtonsoft.Json.Linq;

    public class RequestPayload : BasePayload
    {
        public string Action { get; set; } = string.Empty;

        public RequestPayload(JArray payload)
        {
            MessageTypeId = (int)(payload[0]);
            UniqueId = payload[1].ToString();
            Action = payload[2].ToString();
            Payload = (JObject)payload[3];
        }

        public RequestPayload(object payload)
        {
            JObject request = JObject.FromObject(payload);
            MessageTypeId = (int)request.GetValue("MessageTypeId");
            UniqueId = request.GetValue("UniqueId").ToString();
            Action = request.GetValue("Action").ToString();
            Payload = (JObject)request.GetValue("Payload");
        }
    }
}
