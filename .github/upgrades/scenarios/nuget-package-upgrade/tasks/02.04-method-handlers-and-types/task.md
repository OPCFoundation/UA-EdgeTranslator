# 02.04-method-handlers-and-types: Fix method-call handlers, missing types and abstract state types

## Objective

Fix the remaining structural breaks in the server node manager and address-space service.

## Error classes

- `CS0123`: no overload matches `GenericMethodCalledEventHandler` — the method-handler delegate signature changed (8 handlers: `OnCreateAsset`, `OnDeleteAsset`, `OnDiscoverAssets`, `OnCreateAssetForEndpoint`, `OnConnectionTest`, ...)
- `CS0115`: `UANodeManager.Call` override — `IList<CallMethodRequest>` became `ArrayOf<CallMethodRequest>`
- `CS0246`: unresolved `DataValueCollection`, `ReadValueIdCollection`, `ReferenceDescriptionCollection`, `CertificateValidator`, `CertificateValidationEventArgs` — namespace moves or replacements
- `CS0144`: cannot instantiate abstract `PropertyState<ModelChangeStructureDataType[]>`
- `CS0200`: `ServiceMessageContext.Factory` and similar now read-only
- `CS0315` / `CS0453` / `CS1955` / `CS9342` / `CS8130`: generic constraint and invocation changes

## Steps

1. Use `get_type_info` / `get_member_info` against the 2.0.0 assemblies to find the replacement delegate signature and factory APIs.
2. Update handler signatures, the `Call` override, and construction of read-only contexts.
3. Resolve the moved collection/certificate types.
4. Build `UAServer` and confirm these error codes are cleared.

Primary files: `UAServer/UANodeManager.cs`, `UAServer/Diagnostics/AddressSpaceService.cs`, `UAServer/FileManager.cs`.
