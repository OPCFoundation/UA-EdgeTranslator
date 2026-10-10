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

### 02-migrate-complextypes-usages: Migrate ComplexTypes API usages

Fix the code that consumes the removed and signature-changed ComplexTypes APIs. Known sites: `ProtocolDrivers/OPCUA/OPCUAAsset.cs` (uses `ComplexTypeSystem` with a `Dictionary<ISession, ComplexTypeSystem>` and `LoadAsync()`), and `UA-WoTMapper/UAClientLib/UAClient.cs` (constructs `ComplexTypeSystem(_session)` and calls `LoadTypeAsync`). Replace these with the 2.0.0 replacement APIs, adjusting for `object` → `Variant` and `System.Type` → `IType`/`IEncodeableType` return-type changes.

Migrate by default. Only accept a finding without change if the build proves it unaffected.

**Done when**: No references remain to removed ComplexTypes types/members, and the affected projects compile without errors or warnings.

---

### 03-fix-core-stack-usages: Fix remaining core Opc.Ua build breaks

The core package diff reported no source-breaking public API changes, but a major version bump may still surface behavioral or implicit breaks (obsoletions, changed defaults, nullable annotations) in `UAServer`, `UA-WoTGenerator`, `UA-WoTMapper`, and the protocol drivers. Resolve any remaining compile errors and all new warnings across the solution.

**Done when**: The full solution builds with zero errors and zero warnings.

---

### 04-validate: Validate build and tests

Run a clean solution build and execute `Tests/UAEdgeTranslator.Tests` to confirm no regressions from the stack upgrade.

**Done when**: Solution builds clean and all tests in `UAEdgeTranslator.Tests` pass.
