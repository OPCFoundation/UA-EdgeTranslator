# 02.01-newtonsoft-refs — Progress

## What changed

Versionless `<PackageReference Include="Newtonsoft.Json" />` present in all 19 consuming projects that lacked it (these edits were already in the working tree when the task started; verified and committed here). `UA-WoTGenerator` and `ProtocolDrivers/Mock` already declared it on `main`. Version stays centrally pinned at 13.0.4 in `Directory.Packages.props`.

Projects: `UAServer/UAEdgeTranslator.csproj`, `Tests/UAEdgeTranslator.Tests`, and `ProtocolDrivers/{AvevaPI, BACNet, Beckhoff, HTTPClient, IEC61850, LoRaWAN, Matter, Mitsubishi, ModbusRTU, ModbusTCP, OCPP, OPCAE, OPCDA, OPCUA, Redfish, Rockwell, Siemens}`.

## Validation

- No `Version=` attributes added (CPM-compliant).
- `dotnet build UAServer` output: **0** errors matching `Newtonsoft|JsonProperty|JToken|NullValueHandling|JsonConverter|JsonExtensionData`. The JSON error group is fully cleared.
- UAServer still fails on the 2.0.0 API errors handled by 02.02–02.05 (expected).
