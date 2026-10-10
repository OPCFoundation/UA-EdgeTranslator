# 02.06-protocol-drivers: Fix remaining protocol driver build breaks

## Objective

Once `UAServer` compiles, build the protocol drivers and fix any breaks that were previously masked by the upstream failure.

## Scope

All `ProtocolDrivers/*` projects. Excludes `ProtocolDrivers/OPCUA`'s `ComplexTypeSystem` usage, which is handled in task 03.

## Steps

1. Build the solution excluding the known task-03 ComplexTypes sites.
2. Triage and fix each error using the 2.0.0 replacement APIs.
3. Confirm zero errors and zero warnings across the drivers.
