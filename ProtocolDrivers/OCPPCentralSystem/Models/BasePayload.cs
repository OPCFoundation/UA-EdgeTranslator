/*
Copyright 2020 Cognizant
Copyright 2021 Microsoft Corporation
*/

namespace OCPPCentralSystem.Models
{
    using Newtonsoft.Json.Linq;

    public class BasePayload
    {
        public int MessageTypeId { get; set; }

        public string UniqueId { get; set; }

        public JObject Payload { get; set; }

        public JArray WrappedPayload { get; set; }
    }
}
