# NuGet Package Upgrade — OPC UA Stack

## Strategy
Unified bump of both OPC UA packages to 2.0.0 centrally via CPM, then migrate the ComplexTypes breaking changes driven by build errors.

## Preferences
- **Flow Mode**: Automatic
- **Commit Strategy**: After Each Task
- **Chosen Version**: 2.0.0 for both OPC UA packages (Unified — no divergence)
- **Version Location**: `Directory.Packages.props` (Central Package Management)
- **Breaking-change handling**: Migrate (no pinning, no excluded projects)
- **Packages**: OPCFoundation.NetStandard.Opc.Ua, OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes
- **Current Version**: 1.5.378.176
- **Target Version**: Latest stable (no prerelease)
- **Scope**: Solution (UAEdgeTranslator.sln)
- **Breaking-change scan**: Quick (API diff only)
- **Package management**: Central Package Management via `Directory.Packages.props`

## Decisions
- Continue straight through all task-02 subtasks, including the 02.05 identity/authentication rewrite. No pause before the identity rewrite (user chose option 1).
- Pre-existing working-tree 2.0.0 migration edits (UAServer code + Newtonsoft.Json refs) are kept and built upon, not reverted.
- Obsolete APIs must be replaced, not suppressed (`TreatWarningsAsErrors=true`).

## Source Control
- **Source Branch**: main
- **Working Branch**: upgrade-opcua-stack
- **Commit Strategy**: After Each Task
- **Branch Sync**: Auto (Merge)
