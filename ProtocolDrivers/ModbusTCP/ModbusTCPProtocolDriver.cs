namespace Opc.Ua.Edge.Translator.ProtocolDrivers
{
    using Newtonsoft.Json;
    using Opc.Ua.Edge.Translator.Interfaces;
    using Opc.Ua.Edge.Translator.Models;
    using System;
    using System.Collections.Generic;

    public class ModbusTCPProtocolDriver: IProtocolDriver
    {
        public string Scheme => "modbus+tcp";

        public string WoTBindingUri => "https://www.w3.org/2019/wot/modbus";

        public IEnumerable<string> Discover()
        {
            // ModbusTCP does not support discovery
            return new List<string>();
        }

        public ThingDescription BrowseAndGenerateTD(string assetName, string assetEndpoint)
        {
            return new ThingDescription()
            {
                Context = ["https://www.w3.org/2022/wot/td/v1.1"],
                Id = "urn:" + assetName,
                SecurityDefinitions = new() { NosecSc = new NosecSc() { Scheme = "nosec" } },
                Security = ["nosec_sc"],
                Type = ["Thing"],
                Name = assetName,
                Base = assetEndpoint,
                Title = assetName,
                Properties = new Dictionary<string, Property>(),
                Actions = new Dictionary<string, TDAction>()
            };
        }

        public IAsset CreateAndConnectAsset(ThingDescription td, out byte unitId)
        {
            string[] address = td.Base.Split([':', '/']);
            if ((address.Length != 6) || (address[0] != "modbus+tcp"))
            {
                throw new Exception("Expected Modbus server address in the format modbus+tcp://ipaddress:port/unitID!");
            }

            unitId = byte.Parse(address[5]);

            ModbusTCPAsset asset = new();

            // check if we can reach the Modbus asset
            asset.Connect(address[3], int.Parse(address[4]));

            return asset;
        }

        public AssetTag CreateTag(
            ThingDescription td,
            object form,
            string assetId,
            byte unitId,
            string variableId,
            string mappedUAExpandedNodeId,
            string mappedUAFieldPath)
        {
            ModbusForm modbusForm = JsonConvert.DeserializeObject<ModbusForm>(form.ToString());

            return new AssetTag()
            {
                Name = variableId,
                Address = modbusForm.Href,
                UnitID = unitId,
                Type = modbusForm.ModbusType.ToString(),
                PollingInterval = (int)modbusForm.ModbusPollingTime,
                Entity = modbusForm.ModbusEntity.ToString(),
                IsBigEndian = modbusForm.MostSignificantByte || modbusForm.MostSignificantWord,
                SwapPerWord = modbusForm.MostSignificantWord,
                MappedUAExpandedNodeID = mappedUAExpandedNodeId,
                MappedUAFieldPath = mappedUAFieldPath
            };
        }
    }
}
