# NuGet package upgrade assessment

_Mode: **quick assessment** — package API diffs only; no per-project source scan was run._

## Recommended versions

- **OPCFoundation.NetStandard.Opc.Ua**: **2.0.0** (unified across 3 project(s)).
- **OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes**: **2.0.0** (unified across 2 project(s)).

## Public API changes

> **Types moved (namespace changed) are not removals.** A moved type keeps its name and members;
> the fix is a `using`-directive change, not a rewrite. Do not treat a moved type as deleted.

- **OPCFoundation.NetStandard.Opc.Ua**: no source-breaking public API changes detected — see [`apidiff/OPCFoundation.NetStandard.Opc.Ua.apidiff.md`](apidiff/OPCFoundation.NetStandard.Opc.Ua.apidiff.md).
- **OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes**: 10 type(s) removed, 4 member(s) removed, 8 signature(s) changed — see [`apidiff/OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes.apidiff.md`](apidiff/OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes.apidiff.md).

## Breaking-change findings

- Version divergence findings (Pkg.0003): 0
- Requested-version-unsupported findings (Pkg.0002): 0

- Quick mode does not scan source, so there are no per-line `PkgApi` usage findings. Review the
  per-package API diffs above and rely on build errors during execution to pinpoint affected code.
- A full code scan can locate the exact source location of every breaking-change usage across the repo.
  It is opt-in and slower — re-run the assessment with `fullScan=true` only if the user requests it.

## Next steps

1. Proceed to planning to triage the API changes above and plan the code fixes (if any).

