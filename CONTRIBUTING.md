# Contributing to UA Edge Translator

Thanks for your interest! This document describes the minimum a contribution
needs to satisfy before it can be merged.

## Before you start

* Read the [README](README.md) to understand the architecture (server +
  protocol-driver plug-ins + WoT Thing Descriptions).
* For non-trivial changes, open an issue first so we can agree on scope before
  you spend time on a PR.
* Security-sensitive findings must follow [SECURITY.md](SECURITY.md) and must
  **not** be filed as public issues.

## Local prerequisites

* .NET SDK 10 (the `global.json` at the repo root pins the channel).
* Visual Studio 2026 / Rider / `dotnet` CLI all work.
* Docker Desktop or any container runtime for the F5 "Docker" launch profile.

Never commit secrets to `UAServer/Properties/launchSettings.json`. Local
overrides should use:

```pwsh
git update-index --skip-worktree UAServer/Properties/launchSettings.json
```

so personal credentials cannot be pushed by accident.

## Coding conventions

* C# follows the rules in `.editorconfig`. Run `dotnet format` before opening a
  PR if your editor doesn't.
* All NuGet versions live in [`Directory.Packages.props`](Directory.Packages.props).
  Add new packages there, then reference them with
  `<PackageReference Include="..." />` (no inline `Version=`).
* Prefer message templates (`Log.Logger.Information("...{Field}", value)`)
  over interpolated strings so Serilog can capture structured fields.
* New protocol drivers must implement `IAsset` and `IProtocolDriver` and ship
  as a separate csproj under `ProtocolDrivers/`.

## Pull request checklist

Before requesting review, please confirm:

- [ ] `dotnet build UAEdgeTranslator.sln -c Release` succeeds with no new warnings.
- [ ] Your change is covered by, or does not regress, existing logging /
	  diagnostics — operators should still be able to identify failures.
- [ ] User-visible behaviour, env vars, container flags, or config knobs are
	  documented in [README.md](README.md).
- [ ] No secrets, customer data, or production credentials appear in the diff.
- [ ] If you touched runtime security (auth, certs, validation), you explained
	  the threat model in the PR description.

## Reporting bugs

Use the GitHub issue tracker. Include:

* the container image tag or commit SHA you saw the bug in,
* the relevant log lines (with structured fields) at `Information` level or
  higher,
* the protocol driver and OPC UA client involved, if applicable.
