# 01-bump-package-versions — Progress

## What changed

- `Directory.Packages.props`: `OPCFoundation.NetStandard.Opc.Ua` and `OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes` bumped `1.5.378.176` → `2.0.0`.

## Validation

- `dotnet restore UAEdgeTranslator.sln --force` — **succeeded**, all 22 projects restored. No NU version conflicts; 2.0.0 is compatible with net10.0.
- `dotnet build UAEdgeTranslator.sln` — **fails** (expected). Error inventory captured below.

## Build error inventory (distinct)

The quick assessment understated the impact — the core `Opc.Ua` apidiff reported "no breaking changes", but 2.0.0 is a real major break. Distinct compiler errors:

**A. Newtonsoft.Json no longer flows transitively** (largest group)
- `CS0246: Newtonsoft`, `JsonProperty`, `JsonPropertyAttribute`, `JsonConverter`, `JsonConverterAttribute`, `JsonExtensionData`, `JsonExtensionDataAttribute`, `JToken`
- `CS0103: NullValueHandling does not exist in the current context`
- Opc.Ua 1.5 carried a transitive `Newtonsoft.Json` dependency; 2.0.0 dropped it. ~50 source files across `UAServer`, `Tests`, and most `ProtocolDrivers/*` have `using Newtonsoft.Json`, but only `UA-WoTGenerator` and `ProtocolDrivers/Mock` declare an explicit `PackageReference`.
- **Fix**: add explicit `<PackageReference Include="Newtonsoft.Json" />` to each consuming project (version already pinned at 13.0.4 in CPM).

**B. Core Opc.Ua types not resolving (namespace moves)**
- `CS0246: DataValueCollection`, `ReadValueIdCollection`, `ReferenceDescriptionCollection`, `CertificateValidator`, `CertificateValidationEventArgs`
- Sites: `UAServer/Diagnostics/AddressSpaceService.cs`, `UAServer/Program.cs`

**C. Contract changes in Opc.Ua base types**
- `CS0115: UANodeManager.Call(...)` — no suitable method to override
- `CS0535: ConsoleTelemetry` does not implement `ITelemetryContext.CreateMeter(Assembly)` and `ITelemetryContext.GetActivitySource(Assembly)`
- `CS1750: null` not valid as a default parameter for `NodeId` (NodeId became a struct / non-nullable)

**D. ComplexTypes API break** (per the apidiff artifact — not yet surfaced since the build stops in `UAServer`)
- `ComplexTypeSystem` removed — used in `ProtocolDrivers/OPCUA/OPCUAAsset.cs` and `UA-WoTMapper/UAClientLib/UAClient.cs`

## Outcome

Version bump complete and restoring cleanly. Plan requires revision: group A (Newtonsoft) is a new, large, previously unplanned work item and must be fixed before the rest of the error surface is visible.
