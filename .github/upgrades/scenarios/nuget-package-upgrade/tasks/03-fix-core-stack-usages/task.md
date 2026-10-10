# 03-fix-core-stack-usages: Fix remaining core Opc.Ua build breaks

The core package diff reported no source-breaking public API changes, but a major version bump may still surface behavioral or implicit breaks (obsoletions, changed defaults, nullable annotations) in `UAServer`, `UA-WoTGenerator`, `UA-WoTMapper`, and the protocol drivers. Resolve any remaining compile errors and all new warnings across the solution.

**Done when**: The full solution builds with zero errors and zero warnings.
