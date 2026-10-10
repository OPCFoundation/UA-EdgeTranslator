# 01-bump-package-versions: Bump OPC UA package versions centrally

Update both `PackageVersion` entries in `Directory.Packages.props` from `1.5.378.176` to `2.0.0`, then restore and capture the full build error surface. The assessment ran in quick mode, so the build is what pinpoints the exact breaking call sites. Record the resulting error list as the work inventory for the following tasks.

**Done when**: `Directory.Packages.props` references `2.0.0` for both packages, `dotnet restore` succeeds, and the complete set of compile errors is captured for triage.

## Research

- **Package management mode**: Central Package Management (CPM). `Directory.Packages.props` at repo root, `ManagePackageVersionsCentrally=true`, `CentralPackageTransitivePinningEnabled=true`. Versions must be changed **only** there; project files carry versionless `PackageReference`.
- **Lines to edit**: `Directory.Packages.props` lines 16–17 (under the `<!-- OPC UA SDK -->` comment).
- **Direct `PackageReference` consumers** (no version attribute, no change needed):
  - `UAServer/UAEdgeTranslator.csproj` — both packages
  - `UA-WoTMapper/UA-WoTMapper.csproj` — both packages
  - `UA-WoTGenerator/UA-WoTGenerator.csproj` — core package only
- **Transitive consumers**: all `ProtocolDrivers/*` reference `UAServer/UAEdgeTranslator.csproj`; transitive pinning is on, so the bump flows everywhere.
- **Expected break surface**: `apidiff/OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes.apidiff.md` — 10 types removed, 4 members removed, 8 signature changes. Core `Opc.Ua` apidiff reports no source-breaking changes.

## Steps

1. Edit both `PackageVersion` entries to `2.0.0`.
2. `dotnet restore UAEdgeTranslator.sln` — confirm 2.0.0 resolves for net10.0 and no NU conflicts.
3. Build the solution and capture the full error list as the inventory for tasks 02/03.
