# 02.05-obsoletions-and-identity: Replace obsolete APIs and migrate user identity handling

## Objective

Clear the 20 `CS0618` obsoletion diagnostics. `Directory.Build.props` sets `TreatWarningsAsErrors=true`, so these fail the build and cannot be left in place.

## Error classes

- `ServiceMessageContext(ITelemetryContext)` ctor → `ServiceMessageContext.Create(ITelemetryContext)`
- `NodeId.Identifier` → `TryGetValue<T>` or `IdentifierAsString`
- `ExtensionObject(ExpandedNodeId, object)` → typed-body constructor
- `ISessionManager.ImpersonateUser` → `IUserTokenAuthenticator` + `IServerIdentityRegistry` (register via `services.AddIdentityAuthenticator<T>()`)
- `ImpersonateEventArgs.NewIdentity` → `UserIdentityTokenHandler`
- `CS1061`: `UserNameIdentityToken.DecryptedPassword` removed
- `CS1729`: `ReverseConnectServer` no longer has a parameterless constructor
- `CS0535`: `ConsoleTelemetry` must implement `ITelemetryContext.CreateMeter(Assembly)` and `GetActivitySource(Assembly)`

## Steps

1. Replace each obsolete call with its documented successor.
2. Migrate the `UAServer.cs` impersonation flow to the authenticator/identity-registry model.
3. Implement the new `ITelemetryContext` members on `ConsoleTelemetry`.
4. Build `UAServer` and confirm zero errors and zero warnings.

Primary files: `UAServer/UAServer.cs`, `UAServer/UANodeManager.cs`, `UAServer/ConsoleTelemetry.cs`, `UAServer/Program.cs`.
