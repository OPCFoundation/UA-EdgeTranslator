# 02.02-nodeid-value-types — Progress

## What changed

Opc.Ua 2.0.0 made `NodeId`, `ExpandedNodeId`, `QualifiedName`, `LocalizedText`, `DataValue`, `ExtensionObject`, `ArrayOf<T>` and `DateTimeUtc` value types. Each exposes a `.Null` sentinel and an `IsNull` **property** (previously `NodeId.IsNull(x)` static method).

### `UAServer/UANodeManager.cs`
- `null` args → `NodeId.Null` / `QualifiedName.Null` / `Ua.LocalizedText.Null` in `CreateBrowser` and `FileState.Create` calls.
- `FindAssetNodeIdByName` returns `NodeId.Null`; `ParseExpandedNodeId` returns `ExpandedNodeId.Null`.
- **Silent-bug fix**: `if (assetId == null)` in the WoT-file unload path → `assetId.IsNull`. `NodeId` defines `operator ==(NodeId, object)`, so the old check compiled without warning but could never be true — unloading a non-existent asset would have proceeded with a null id.
- 6× `new ServiceResult(code, "message")` → `new ServiceResult(code, new Ua.LocalizedText("message"))` (string overload removed).
- `LocalizedText` qualified as `Ua.LocalizedText` (ambiguous with `Opc.Ua.Export.LocalizedText`).

### `UAServer/Diagnostics/AddressSpaceService.cs`
- `ReadValueIdCollection` / `DataValueCollection` / `ReferenceDescriptionCollection` / `BrowseDescriptionCollection` / `BrowseResultCollection` (removed) → `ArrayOf<T>` (or `List<T>` builders, which convert implicitly).
- `results is null` / `?? []` on `ArrayOf<T>` → `.IsNull` / `ArrayOf<T>.Empty`.
- Tuple deconstruction with discard on `BrowseAsync` replaced with `.Item1` (CS8130/CS8183).
- `?.` on `DataValue` / `LocalizedText` / `QualifiedName` / `ExpandedNodeId` / `ExtensionObject` removed.
- `value?.Value is T x` → `value.WrappedValue.TryGetValue(out T x)` (also clears the `DataValue.Value` obsoletion).
- `ExtensionObject.Body is byte[]` → `TryGetAsBinary(out ByteString)`; `.Body as StructureDefinition` → `TryGetValue(out StructureDefinition, null)`.
- `NodeId.Identifier is uint` (obsolete) → `TryGetValue(out uint)`.
- `new ServiceMessageContext(telemetry) { Factory = ... }` → `ServiceMessageContext.Create(telemetry)`; `Factory` is now read-only and populated by `Create`.
- `FormatTimestamp(DateTime)` → `FormatTimestamp(DateTimeUtc)`.
- `OperationContext` ctor now requires a `RequestLifetime` → `RequestLifetime.None`.

### `UAServer/FileManager.cs`
- `ServerSystemContext.SessionId` is now `NodeId?` → `?? NodeId.Null`.
- `LocalizedText` → `Opc.Ua.LocalizedText` (ambiguity).

## Validation

`dotnet build UAServer -f net10.0`: **125 → 17** distinct errors. All value-type error classes (CS0037, CS1503, CS1750, CS0023, CS8130, CS8183, CS0019, CS1955, CS0246, CS0077) are cleared.

Remaining 17 errors are owned by later subtasks:
- 02.03 (Variant/DateTime): UANodeManager 922, 979, 986, 1953–1954, 2139, 2242
- 02.04 (handlers/types): FileManager 61–62, UANodeManager 763, 1261, 1581
- 02.05 (identity/obsoletions): UAServer.cs 12, 43, 48, 57, 64

## Issues encountered

- **Concurrent writer**: midway through, my `UANodeManager.cs` edits were reverted by an external process. After the user stopped it, I made a WIP checkpoint commit before re-applying, and confirmed the tree stayed stable for 20s.
- **Edit-tool truncation**: three replacements whose `oldString` began mid-line dropped the line prefix (lines 513, 723, 919 in UANodeManager; line 604 in AddressSpaceService). All four were detected by the build and restored. Going forward, replacements will always span whole lines.
