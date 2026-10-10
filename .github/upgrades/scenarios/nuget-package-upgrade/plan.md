# NuGet Package Upgrade Plan — OPC UA Stack

## Overview

**Target**: `OPCFoundation.NetStandard.Opc.Ua` and `OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes` from `1.5.378.176` → `2.0.0`
**Scope**: Solution-wide via Central Package Management (`Directory.Packages.props`). Direct package consumers: `UAServer/UAEdgeTranslator.csproj`, `UA-WoTGenerator`, `UA-WoTMapper`. Transitive consumers include all protocol drivers, notably `ProtocolDrivers/OPCUA`.

The core `Opc.Ua` package reports no source-breaking public API changes. The ComplexTypes package is a major-version break: 10 public types removed (including `ComplexTypeSystem`, `DataDictionary`, `IComplexTypeBuilder`, `NodeCacheResolver`), 4 members removed, and 8 signature changes (notably `object` → `Opc.Ua.Variant` and `System.Type` → `Opc.Ua.IType`/`IEncodeableType`). See `apidiff/OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes.apidiff.md`.

## Tasks

### 01-bump-package-versions: Bump OPC UA package versions centrally

Update both `PackageVersion` entries in `Directory.Packages.props` from `1.5.378.176` to `2.0.0`, then restore and capture the full build error surface. The assessment ran in quick mode, so the build is what pinpoints the exact breaking call sites. Record the resulting error list as the work inventory for the following tasks.

**Done when**: `Directory.Packages.props` references `2.0.0` for both packages, `dotnet restore` succeeds, and the complete set of compile errors is captured for triage.

---

### 02-migrate-complextypes-usages: Restore Newtonsoft.Json refs and fix core Opc.Ua breaks

> Scope revised after task 01. This task was originally scoped to the ComplexTypes migration, but the build halts in `UAServer` long before those errors are reachable — the core-stack breaks must be cleared first. The ComplexTypes work moved to task 03.

Opc.Ua 1.5 carried

Then fix the genuine 2.0.0 core-stack breaks that the apidiff missed: unresolved collection and certificate types (`DataValueCollection`, `ReadValueIdCollection`, `ReferenceDescriptionCollection`, `CertificateValidator`, `CertificateValidationEventArgs` in `AddressSpaceService.cs` and `Program.cs`), the removed `UANodeManager.Call` override (CS0115), the new `ITelemetryContext.CreateMeter`/`GetActivitySource` members `ConsoleTelemetry` must implement (CS0535), and `NodeId` no longer accepting a `null` default parameter (CS1750).

**Done when**: `UAServer` and all protocol drivers compile, with no errors and no new warnings.

---

### 03-fix-core-stack-usages: Migrate ComplexTypes API usages

> Scope revised after task 01 — this task now carries the ComplexTypes migration originally planned as task 02.

Fix the code that consumes

Migrate by default. Only accept a finding without change if the build proves it unaffected.

**Done when**: No references remain to removed ComplexTypes types/members, and the affected projects compile without errors or warnings.

---

### 04-validate

Run a clean solution build and execute `Tests/UAEdgeTranslator.Tests` to confirm no regressions from the stack upgrade.

**Done when**: Solution builds clean and all tests in `UAEdgeTranslator.Tests` pass.
