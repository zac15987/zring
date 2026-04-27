# zring

A Windows app-switcher / launcher bar with a planned clipboard ring feature.

Forked from [AppSwitcherBar](https://github.com/adamecr/AppSwitcherBar) by Radek Adamec (MIT). The application-switching codebase is inherited as-is; the clipboard ring is the planned next addition.

## Status

Pre-release. Documentation is being rewritten — see the source for behavior details until this README catches up.

## Requirements

- Windows 10 17763+ (1809) / Windows 11
- .NET 10 SDK (for building)

## Build & Run

```powershell
dotnet build zring.sln -c Release
dotnet run --project zring
```

Self-contained publish:

```powershell
dotnet publish zring -c Release -r win-x64 --self-contained
```

## License

MIT. See [LICENSE](LICENSE) — includes the upstream AppSwitcherBar copyright.
