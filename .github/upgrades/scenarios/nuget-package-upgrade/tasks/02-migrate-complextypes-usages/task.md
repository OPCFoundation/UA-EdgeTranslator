# 02-migrate-complextypes-usages: Migrate ComplexTypes API usages

Fix the code that consumes the removed and signature-changed ComplexTypes APIs. Known sites: `ProtocolDrivers/OPCUA/OPCUAAsset.cs` (uses `ComplexTypeSystem` with a `Dictionary<ISession, ComplexTypeSystem>` and `LoadAsync()`), and `UA-WoTMapper/UAClientLib/UAClient.cs` (constructs `ComplexTypeSystem(_session)` and calls `LoadTypeAsync`). Replace these with the 2.0.0 replacement APIs, adjusting for `object` → `Variant` and `System.Type` → `IType`/`IEncodeableType` return-type changes.

Migrate by default. Only accept a finding without change if the build proves it unaffected.

**Done when**: No references remain to removed ComplexTypes types/members, and the affected projects compile without errors or warnings.
