# OPC Classic Alarms & Events

The `opc.ae` driver projects local OPC Classic Alarms & Events conditions into the translator's OPC UA address space. Version one is read-only: OPC UA Acknowledge and Confirm requests are not sent to the Classic A&E server.

## Prerequisites

- Run the native `win-x86` translator build. Containers and remote DCOM hosts are unsupported.
- Run the A&E server and translator on the same Windows machine.
- Install the 32-bit OPC Foundation Classic Core Components redistributable.
- Keep the complete published `drivers\opcae` directory. It contains the required official OPC Foundation managed assemblies: `OpcComRcw.dll`, `OpcNetApi.dll`, and `OpcNetApi.Com.dll`.

The endpoint must use a local host and a registered ProgID:

```text
opc.ae://localhost/<ProgID>
```

`localhost`, `127.0.0.1`, `::1`, and the local machine name are accepted. Remote DCOM endpoints are rejected.

## Configure an Asset

Copy [Matrikon OPC A&E Simulation Server.td.jsonld](../../Samples/Matrikon%20OPC%20A%26E%20Simulation%20Server.td.jsonld) into the deployment's `settings` directory and restart the translator. For the Matrikon Simulation Server used during validation, the registered A&E-capable ProgID is `Matrikon.OPC.Simulation.1`.

The event form supports these optional filters:

- `categories`: integer event-category IDs
- `areas`: process-area IDs
- `sources`: source IDs
- `eventTypes`, `lowSeverity`, `highSeverity`, `bufferTime`, and `maxEvents`

On successful onboarding, the translator logs both `Loaded protocol driver: opc.ae` and `Successfully parsed WoT file for asset: MatrikonAe`.

## Consume Events

In an OPC UA client such as UAExpert, create an event monitored item on the asset's `Alarms` object. Select fields including SourceName, ConditionName, Severity, Message, Time, ActiveState, AckedState, EnabledState, and Retain. Trigger a condition in the Classic server to receive the corresponding OPC UA alarm event.

Some servers, including Matrikon Simulation Server, return `E_FAIL` for the optional initial condition refresh. The driver logs this as a warning and continues receiving new live events.