# Copilot Instructions

## Project Guidelines
- Always write files using the repo's Git line-ending convention (.gitattributes eol=lf, LF only) and preserve each file's original UTF-8 BOM state; verify all touched files for consistent line endings after edits.
- Always publish protocol drivers as Debug builds, using each driver's FolderProfile.pubxml (Configuration=Debug) into UAServer\drivers\<name>; always pass `-c Debug` explicitly on the `dotnet publish` command line, as the FolderProfile.pubxml Configuration is not honored by `dotnet publish`. Verify the deployed DLL in UAServer\drivers\<name> matches the bin\Debug output.
- When publishing the Matter driver, which multi-targets net10.0 and net10.0-windows10.0.26100.0, also pass `-f net10.0` as the FolderProfile.pubxml TargetFramework is not honored by `dotnet publish`.