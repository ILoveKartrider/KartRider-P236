# Third-party notices

## Launcher_V2

- Project: `yanygm/Launcher_V2`
- Source: <https://github.com/yanygm/Launcher_V2>
- Local base revision used during development: `f55fc87`
- License: Academic Free License 3.0

Selected protocol, packet, networking, and compatibility concepts originate
from or are modified from this AFL-3.0 project. The complete AFL-3.0 text is in
[`LICENSE.md`](LICENSE.md), and the modification notice is in
[`NOTICE.md`](NOTICE.md).

No source or binary from `Launcher_HF_5136` is redistributed by this
repository because that repository did not carry an explicit license when this
distribution was prepared.

## .NET

The application projects target .NET 8 and use framework libraries. Microsoft
runtime components are not stored in this source repository; normal
`dotnet publish` rules apply to release artifacts.

Published packages contain the .NET native application host and may contain a
self-contained .NET runtime. `scripts/Publish.ps1` copies the active SDK's
`LICENSE.txt` and `ThirdPartyNotices.txt` into each package as
`DOTNET-LICENSE.txt` and `DOTNET-THIRD-PARTY-NOTICES.txt`. See Microsoft's
[.NET redistribution licensing information](https://github.com/dotnet/core/blob/main/license-information.md#redistribution).

The source test project restores the following development-only packages. They
are not included in the connector or server publish directories:

- [Microsoft.NET.Test.Sdk 17.8.0](https://github.com/microsoft/vstest) — MIT License
- [xUnit.net 2.6.6](https://github.com/xunit/xunit) — Apache License 2.0
- [xunit.runner.visualstudio 2.5.6](https://github.com/xunit/visualstudio.xunit) — Apache License 2.0
