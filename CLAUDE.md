# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Documentation language

All repository documentation — this file and `README.md` — is written in **English**. Keep new or edited docs in English regardless of the conversation language. Code comments, log messages, and commit messages also stay in English. Chat with the user can be in whatever language they prefer.

## Build & Run

Single-project solution (`zring.sln`) targeting `net10.0-windows10.0.17763.0` (WPF, Windows-only). Root namespace is `Zring`. Project is forked from [AppSwitcherBar](https://github.com/adamecr/AppSwitcherBar) — application-switching codebase is inherited; clipboard ring is the planned next feature.

```powershell
# Restore + build (Debug / Release, AnyCPU or x64)
dotnet build zring.sln
dotnet build zring.sln -c Release

# Run from the project directory (output copies appsettings.json + language.*.json next to the exe)
dotnet run --project zring

# Publish a self-contained x64 build
dotnet publish zring -c Release -r win-x64 --self-contained
```

There is one UI test project (`zring.Tests.UI`, FlaUI + xUnit) and no linter configured. MSBuild in Visual Studio 2022 is the author's primary build path.

### Runtime configuration

- `appsettings.json` — full settings, copied to output on build.
- `appsettings.user.json` — persisted user runtime state (dock edge, size, auto-size). **Takes priority over command line.** Delete to reset.
- `language.{code}.json` — UI translations. Resolution walks from less to more specific (`en` → `en-US`).
- Language override from CLI uses `Language=cs` (no `AppSettings:` prefix — explicit exception in `App.xaml.cs` host configuration).
- Any other setting can be overridden via `AppSettings:Key=value` CLI arg or env var (standard .NET Configuration).

## Architecture

### Host + DI composition

`App.xaml.cs` builds a `Microsoft.Extensions.Hosting` host in `OnStartup`. Two-phase configuration:

1. **Host config** reads `appsettings.json` early so the `Language` key is available.
2. **App config** loads the resolved `language.*.json` files, then `appsettings.user.json` last (highest precedence).

`ConfigureServices` registers `IOptions<AppSettings>` + `IOptions<Language>` and wires services. Several services are **feature-flag-switched at registration**: `IJumpListService` (real v1 / v2 vs `DummyJumpListService`), `IStartupService`, `IAudioService`. When adding a new optional service follow the same dummy-implementation pattern so consumers don't need null checks.

All ViewModels (`MainViewModel`, `MenuPopupViewModel`, `AudioViewModel`, `ClockViewModel`, `AppFilterViewModel`) and `MainWindow` are singletons — matches their app-lifetime scope and keeps disposal straightforward.

### Appbar window mechanics

`AppBar/AppBarWindow.cs` is the base class for `MainWindow` and the reason this app is Windows-only.

- Registers as an Application Desktop Toolbar via `SHAppBarMessage(ABM_NEW/REMOVE/QUERYPOS/SETPOS/ACTIVATE/WINDOWPOSCHANGED)`.
- Hooks `WndProc` through `HwndSource` (WPF does not expose it directly) to (a) block non-appbar position/size changes by tweaking `WM_WINDOWPOSCHANGING`, and (b) receive the appbar notification callback message whose ID is registered application-side.
- Position flow is always query-then-set: `ABM_QUERYPOS` → Windows adjusts rect → `ABM_SETPOS` → apply the returned rect. Do not shortcut this.
- `OnDpiChanged` is overridden for per-monitor DPI. The `app.manifest` opts in to `PerMonitorV2` — do not remove.
- `ABM_REMOVE` must be sent on close or Windows keeps the desktop workspace shrunk.

### MainViewModel: window enumeration loop

`MainViewModel.RefreshAllWindowsCollection` runs on a `DispatcherTimer` (interval from `RefreshWindowInfosIntervalMs`). It:

1. `EnumWindows` + filter (visible, not cloaked, has caption, not tool window, not child, not the app itself).
2. Diffs against the current collection and applies changes **incrementally** to preserve ordering and avoid UI flicker.
3. Resolves icon via a fallback chain: `WM_GETICON` → `GCLP_HICONSM` → `GCLP_HICON`.
4. Resolves AppId (see below) for grouping.
5. Tracks the foreground window (`GetForegroundWindow`) to highlight the active button, while excluding transient focus on the appbar itself. After a user-initiated minimize, `MainViewModel.suppressForegroundUpdateUntil` holds off `lastForegroundWindow` updates for 500 ms — without it, Windows's next-in-Z-order pick (often the IDE) would briefly light up as "active" right after the user dismissed another app. The minimize branch also explicitly clears the just-minimized window's `IsForeground`, otherwise the appbar grabbing click focus leaves `lastForegroundWindow` stale and rapid re-clicks become a no-op (behavior amplified on .NET 10's WPF focus handling).
6. **App filter gate** at the callback tail: if `IAppSettings.IsAppFilterActive` (there is at least one checked app) and `wnd.Group` isn't in `UserSettings.FilteredAppKeys`, skip both `ButtonManager.Add` (new) and `MarkToKeep` (existing) so `EndUpdate` drops the window. Note: `WndInfo.OnPropertyChanged` silently flips `ChangeStatus` from `ToRemove` → `Changed` on any property set during the callback (Title/IsForeground/...), so the gate explicitly re-calls `MarkForRemoval()` before returning — without that, filtered existing windows would never be removed.

Every enumerated window (pre-filter) is also recorded in `MainViewModel.LastEnumeratedWindows` for the filter popup to list — `ButtonManager` is post-filter and would otherwise only show what's already checked.

`MainViewModel.logging.cs` is a partial file containing the `LoggerMessage.Define` source — keep logging declarations there, not inline.

### Application User Model ID (AppId) resolution

AppId is the grouping key and the handle for launching Store/UWP apps and retrieving JumpLists. Resolution order:

1. `IApplicationResolver.GetAppIDForWindow` (undocumented COM; gated by `FeatureFlags.UseApplicationResolver`).
2. Window Property Store → `PKEY_AppUserModel_ID`.
3. Kernel `GetApplicationUserModelId` at process level.
4. Map executable path (`QueryFullProcessImageName`) to the `InstalledApplications` collection built at startup from the `AppsFolder` shell namespace.
5. Hardcoded overrides from `AppSettings.AppIds` (explorer.exe is always there).
6. Executable path as last-resort fallback.

When something "doesn't group right," this pipeline is almost always the cause.

### App filter (in-UI whitelist)

`AppFilterViewModel` + `Views/AppFilterControl` implement the funnel-icon popup on the appbar. The popup lists every currently running app grouped by `ButtonInfo.Group`, sourced from `MainViewModel.LastEnumeratedWindows` (pre-filter) — reading `ButtonManager` instead would shrink the list to the already-checked set. Display names come from a fallback chain: `WndInfo.InstalledApplication?.Name` → fresh `IBackgroundDataService` lookup by AppId → by executable → cleaned exe filename → window title.

Selection is persisted to `appsettings.user.json` as `UserSettings.FilteredAppKeys` (lowercase AppId / executable / fallback, matching `ButtonInfo.Group`). `OnItemToggled` updates the list, calls `UserSettings.Save()`, and kicks `Main.RefreshAllWindowsCollection(false)` so the appbar updates within the next tick. Checked state for closed apps is preserved — re-launching an app automatically applies the existing filter. Pinned-app buttons are not considered by the filter, but `ShowPinnedApps` defaults to `false` anyway because pin-replacement in `AppButtonManager.EndUpdate` (lines ~175-184) would otherwise restore a pin button whenever a filtered window is removed, visually defeating the filter.

### Background data service

`BackgroundDataService` enumerates installed applications, taskbar pins (`IPinnedList3`), and Start pins (`IStartLayoutCmdlet` — Windows 10 XML / Windows 11 JSON) off the UI thread at startup. Retrieval takes seconds; UI shows a "busy" cursor via `MainWindow.IsBackgroundRefreshing` during this period. Search ranking also optionally reads `Windows\Prefetch` for run count / last-launch data (gated by `FeatureFlags.EnableRunInfoFromWindowsPrefetch`, default `false` because `C:\Windows\Prefetch` is ACL'd to Administrators and ordinary users get `UnauthorizedAccessException` per-app at startup, flooding the log).

### Thumbnails (DWM)

`WpfExt/ThumbnailPopup.cs` extends WPF `Popup` (which renders in its own HWND — note the visual-tree split). On open, it sends popup HWND + bounds to `MainViewModel.ShowThumbnail` which calls `DwmRegisterThumbnail` → `DwmQueryThumbnailSourceSize` → `DwmUpdateThumbnailProperties` (scale + center, keep aspect). Must call `DwmUnregisterThumbnail` on close.

### JumpLists

Two implementations selected by `FeatureFlags.JumpListSvcVersion` (default 2):

- `JumpListService2` — uses undocumented `IAutomaticDestinationList2` (mirrors Windows behavior most closely, inspired by Open-Shell-Menu).
- `JumpListService` (legacy, v1) — reads `.automaticDestinations-ms` as OLE Structured Storage (uses reflection on internal `System.IO.Packaging.StorageRoot` to reach the root storage) and parses `.customDestinations-ms` manually.

Both resolve the file name via `AppIdCrc64.Compute(Encoding.Unicode.GetBytes(appId.ToUpper()))` — that specific CRC variant and the uppercase-unicode input are load-bearing; do not "simplify."

Links inside either file format are read as `IShellLinkW` via `OleLoadFromStream`. `JumpListUseTempFiles` toggles temp-file vs in-memory property access (in-memory is slightly faster and gives image thumbnails; temp-file is the "reference" path).

### Win32 interop layout

Everything in `Win32/` is P/Invoke and COM interop. Organized by type category rather than by feature:

```
Win32/
├── NativeClasses/        COM coclass GUIDs / Type.GetTypeFromCLSID wrappers
├── NativeConstants/      Win32Consts (flags, message IDs, CLSIDs, IIDs)
├── NativeDelegates/      Callback delegate types (e.g. EnumWindowsProc)
├── NativeEnums/          DWM_*, ABM_*, WM_*, GWL_*, etc.
├── NativeInterfaces/     [ComImport] interfaces (IShellLinkW, IPinnedList3, IApplicationResolver, IStartLayoutCmdlet, …)
│   └── Extensions/       Managed helpers that wrap interface calls
├── NativeMethods/        DllImport classes split by DLL (User32, Shell32, Ole32, DwmApi, Kernel32, …)
├── NativeStructs/        APPBARDATA, RECT, POINT, PROPERTYKEY, STGM, …
└── Services/             Managed services built on top of the interop
    ├── Audio/  JumpLists/  Pins/  Shell/  Startup/
    └── Thumbnail.cs, Monitor.cs, WndAndApp.cs, Prefetch.cs, Package.cs, Resource.cs
```

When adding a new Win32 call, place declarations in the category folder (not inline) and prefer a typed wrapper in `Services/` for anything non-trivial.

### Logging

Uses `Microsoft.Extensions.Logging` with `LoggerMessage.Define` source-generated-style declarations. `AppBarWindow` and `MainViewModel` carve out event-ID ranges (appbar is 3xxx, errors within each range end in 9xx); `AppFilterViewModel` uses 7xxx. Keep new log messages in the corresponding `.logging.cs` partial and reuse the existing range convention.

### Feature flags

All optional / experimental behavior goes through `AppSettings.FeatureFlags` with a `FF_*` constant in `AppSettings.cs`. Treat these as the supported toggle surface — undocumented Win32 paths (AppId resolver, pinned list, start pins, context menu on thumbnail) should always have a flag so a user can disable them when a Windows update breaks the API.

### Themes & localization

WPF-UI (`Wpf.Ui`, package `WPF-UI` v4.2.0) provides theming. `StartupTheme` picks `System | Light | Dark`; `IThemeService` (moved to `Wpf.Ui` namespace in v4; was `Wpf.Ui.Mvvm.Contracts` in v2) handles runtime toggle. Theme application goes through `ApplicationThemeManager.Apply` (renamed from the v2 static `Theme.Apply`). `InvertWhiteIcons` / `InvertBlackIcons` exist because some apps ship icons that disappear on the opposite-brightness theme — the inversion heuristic runs on retrieved HICONs, not on the source.

Custom `ui:Button` `ControlTemplate`s in this project (CloseButtonStyle, NavButton, AppShortcutButtonStyle, AudioButtonStyle, PopupAudioButtonStyle) use v4's `ContentPresenter Content="{TemplateBinding Icon}"` + `TextElement.Foreground` inheritance pattern rather than the old v2 `SymbolIcon Symbol="{TemplateBinding Icon}"` binding — `ui:Button.Icon` is now `IconElement`, not `SymbolRegular`. Direct button usages write `Icon="{ui:SymbolIcon Xxx24}"` instead of a bare enum string. `AppButton` also explicitly overrides `VerticalAlignment="Stretch"` because v4's default is `Center` and would otherwise leave visible vertical gaps in the cell.

Translations live in `language.{code}.json` with a flat `Language.Translations` dictionary of known keys (see `Config/TranslationKeys.cs`). English is the implicit baseline; any missing key falls back through the chain.

## UI Automation tests

`zring.Tests.UI` (FlaUI 5 + xUnit) drives the built `zring.exe` via UI Automation for smoke / integration tests. The test project's `README.md` is the canonical reference for the WPF + UIA + FlaUI gotchas we hit; read it before adding tests or tagging more elements with `AutomationProperties.AutomationId`. Quick rules:

- **Panels and Decorators have no AutomationPeer.** `WrapPanel`, `Grid`, `Canvas`, `StackPanel`, `Border` are invisible to UIA — `AutomationProperties.AutomationId` set on them is silently useless. Tag a `Control`-derived descendant (Button, ToggleButton, UserControl) instead.
- **`MainWindow` is `WS_EX_TOOLWINDOW`.** `Process.MainWindowHandle` is `0`; use `Application.GetAllTopLevelWindows(automation)` filtered by AutomationId, not `Application.GetMainWindow()`.
- **`app.Close()` races `host.StopAsync(5s)`** and force-kills, skipping `ABM_REMOVE` — leaves the desktop work area shrunk. Tests drive `Process.CloseMainWindow()` + `WaitForExit(15s)` directly.
- **Only one appbar can register per edge.** Tests fail-fast if `zring.exe` is already running, and the project xUnit collection is non-parallel.

Existing landmark AutomationIds: `Zring.MainWindow`, `Zring.MenuToggle`, `Zring.AudioControl`, `Zring.AppFilter`, `Zring.Clock`, plus per-app `AppButton` instances bound to `ButtonInfo.Group`.
