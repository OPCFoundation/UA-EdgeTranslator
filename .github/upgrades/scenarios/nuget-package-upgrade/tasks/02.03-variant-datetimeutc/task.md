# 02.03-variant-datetimeutc: Migrate object->Variant and DateTime->DateTimeUtc

## Objective

Complete the migration from `object` values to `Opc.Ua.Variant` and from `DateTime` to `DateTimeUtc` across the server node handlers.

## Error classes

- `CS0266`: cannot implicitly convert `object` to `Opc.Ua.Variant` — wrap in `new Variant(...)`
- `CS0023` / `CS0185` / `CS8121`: operators and pattern matching no longer valid against `Variant`
- `DateTime` → `DateTimeUtc` in `OnReadValue` / `OnWriteValue` signatures

## Notes

Partial work is already present in the working tree (`OnReadValue` / `OnWriteValue` signatures updated, several `new Variant(...)` wraps applied). Continue from that state.

## Steps

1. Wrap remaining `object` assignments to `Variant`-typed targets.
2. Replace type checks / casts on `Variant` with `Variant.Value` access or `TryGetValue<T>`.
3. Convert `DateTime` timestamps to `DateTimeUtc`.
4. Build `UAServer` and confirm these error codes are cleared.

Primary files: `UAServer/UANodeManager.cs`, `UAServer/Diagnostics/AddressSpaceService.cs`.
