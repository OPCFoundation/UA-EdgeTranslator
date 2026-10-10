# 02.02-nodeid-value-types: Fix NodeId/QualifiedName/LocalizedText value-type conversions

## Objective

Fix the errors caused by `NodeId`, `QualifiedName`, and `LocalizedText` becoming non-nullable value types in Opc.Ua 2.0.0.

## Error classes

- `CS0037`: cannot convert null to `NodeId` (non-nullable value type) — 10 sites
- `CS1503`: cannot convert `<null>` to `NodeId` / `QualifiedName` / `LocalizedText`; cannot convert `uint` to `NodeId` — 26 sites
- `CS1750`: null not valid as a default parameter value for `NodeId`
- `CS0023` / `CS8183` / `CS0019`: null-propagation and comparison operators no longer valid on these value types

## Steps

1. Replace `null` arguments/defaults with `NodeId.Null`, `QualifiedName.Null`, `LocalizedText.Null` as appropriate.
2. Wrap bare `uint` identifiers passed where a `NodeId` is expected (`new NodeId(id)`).
3. Replace `?.` / `?? ` / `== null` patterns on these types with `IsNull`-style checks or value comparisons.
4. Build `UAServer` and confirm these error codes are cleared.

Primary files: `UAServer/UANodeManager.cs`, `UAServer/Diagnostics/AddressSpaceService.cs`, `UAServer/NodeFactory.cs`.
