# 02.01-newtonsoft-refs: Verify explicit Newtonsoft.Json package references

## Objective

Ensure every project that uses `Newtonsoft.Json` declares an explicit versionless `<PackageReference Include="Newtonsoft.Json" />`, since Opc.Ua 2.0.0 dropped the transitive dependency that previously supplied it.

## Scope

All 21 projects with `using Newtonsoft` in their sources. Version 13.0.4 is already pinned in `Directory.Packages.props` (CPM) — do not add a `Version=` attribute.

## Steps

1. Audit each project for a declared `Newtonsoft.Json` reference vs. actual `using Newtonsoft` usage.
2. Add the versionless `PackageReference` wherever missing.
3. `dotnet restore` and confirm no CS0246 errors remain for `Newtonsoft`, `JsonProperty`, `JToken`, `NullValueHandling`.

## Status note

An audit at task start showed all 21 projects already declare the reference (working-tree edits present before this task began). Verify and confirm rather than re-add.
