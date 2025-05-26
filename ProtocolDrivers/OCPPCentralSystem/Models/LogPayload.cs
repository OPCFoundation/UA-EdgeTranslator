/*
Copyright 2020 Cognizant
Copyright 2021 Microsoft Corporation
*/

namespace OCPPCentralSystem.Models
{
    using Newtonsoft.Json;

    public class LogPayload
    {
        public bool IsRequest { get; set; }

        public string Command { get; set; }
        
        public int StationChargerId { get; set; }
        
        public string Input { get; set; }

        public LogPayload(RequestPayload requestPayload,string chargepointName)
        {
            IsRequest=true;
            Command=requestPayload.Action;
            StationChargerId=int.Parse(chargepointName);
            Input= JsonConvert.SerializeObject(requestPayload.Payload);
        }

        public LogPayload(string command,ErrorPayload errorPayload,string chargepointName)
        {
            IsRequest=false;
            Command=command;
            StationChargerId=int.Parse(chargepointName);
            Input= JsonConvert.SerializeObject(errorPayload.Payload);
        }

        public LogPayload(string command,ResponsePayload responsePayload,string stationChargerId)
        {
            IsRequest=false;
            Command=command;
            StationChargerId=int.Parse(stationChargerId);
            Input=JsonConvert.SerializeObject(responsePayload.Payload);
        }
    }
}
