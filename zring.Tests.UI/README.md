# zring.Tests.UI

FlaUI-driven UI Automation tests for zring's appbar.

## Running

```powershell
# Make sure no zring instance is on the dock — only one appbar can register per edge.
dotnet test zring.Tests.UI
```

The test fails fast with a clear message if `zring.exe` is already running. As a second line of defence, a running `zring.exe` also locks `zring.ico` and the build itself will fail before the test code executes.

To exercise the "already running" path on purpose (after a successful build), pass `--no-build`:

```powershell
dotnet test zring.Tests.UI --no-build
```

## Lessons learned (don't repeat these)

These are non-obvious facts about WPF / UI Automation / FlaUI that bit us during the smoke-test bring-up. Read this before adding more tests or tagging more elements with `AutomationProperties.AutomationId`.

### Panel- and Decorator-derived elements have no AutomationPeer

WPF's `Panel` (and its subclasses `WrapPanel`, `Grid`, `Canvas`, `StackPanel`) and `Decorator` (and `Border`) **do not implement `AutomationPeer`** by default. They are invisible to UI Automation regardless of any `AutomationProperties.AutomationId` you set on them.

Setting an AutomationId on a `WrapPanel` is **silently useless** — UIA Inspect / FlaUI will simply not see the element. We tried `Zring.MainPanel` on the WrapPanel and removed it after discovering this.

Anchor tests on a `Control`-derived descendant or the `Window` itself. Valid landmarks in zring today:

| AutomationId        | Element type        | Where                                    |
| ------------------- | ------------------- | ---------------------------------------- |
| `Zring.MainWindow`  | `Window`            | `MainWindow.xaml` root                   |
| `Zring.MenuToggle`  | `ToggleButton`      | `MainWindow.xaml` (left of the bar)      |
| `Zring.AudioControl`| `UserControl`       | `Views/AudioControl.xaml` instance       |
| `Zring.AppFilter`   | `UserControl`       | `Views/AppFilterControl.xaml` instance   |
| `Zring.Clock`       | `UserControl`       | `Views/ClockControl.xaml` instance       |
| _(per-app)_         | `wpfExt:AppButton`  | `{Binding Group}` — AppId / executable   |

If you need to find a region that has no Control hosting it, wrap it in a `ContentControl` and tag the wrapper. Or implement a custom `AutomationPeer` — but that is overkill for test-anchoring.

Reference: [UI Automation of a Custom Control – Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/ui-automation-of-a-wpf-custom-control)

### Tool-window quirks (`WS_EX_TOOLWINDOW`)

`MainWindow` carries `WS_EX_TOOLWINDOW` whenever `ShowInTaskbar=false` (default), set by `WndAndApp.HideFromTaskbar` (`zring/Win32/Services/WndAndApp.cs`). Two consequences:

- `Process.MainWindowHandle` returns `0` for tool windows. **Don't use `Application.GetMainWindow()`** in tests — under the hood it depends on `MainWindowHandle`.
- The window IS in the UIA tree (it has a Window peer); only the legacy `MainWindowHandle` API ignores it.

Use `Application.GetAllTopLevelWindows(automation)` and filter by `AutomationId == "Zring.MainWindow"` instead.

### `app.Close()` races zring's host shutdown — drive the Process directly

`App.OnExit` awaits `host.StopAsync(TimeSpan.FromSeconds(5))`. FlaUI's `Application.Close()` has a hardcoded 5-second wait then **kills the process**. Those two 5-second windows race, so `app.Close()` on a clean exit will frequently print `"Application failed to exit"` and force-kill the process — which skips `ABM_REMOVE` and **leaves the desktop work area shrunk until next logon or `explorer.exe` restart**.

Bypass FlaUI's close path:

```csharp
var proc = Process.GetProcessById(app.ProcessId);
proc.CloseMainWindow();              // WM_CLOSE -> AppBarRemove -> ABM_REMOVE
if (!proc.WaitForExit(15_000))       // generous; covers host.StopAsync(5s)
{
    Console.Error.WriteLine("WARNING: ...");
    proc.Kill();                      // last resort
}
```

Don't call `app.WaitWhileBusy()` after the process has exited — it calls `Process.WaitForInputIdle()` which throws `InvalidOperationException` on a dead process.

### Visual-tree population is asynchronous

When the `Window` element first appears in the UIA tree, its descendants are **not all there yet** — WPF data binding for items, templates, and child UserControls populates over the next frames. Wrap descendant `FindFirstDescendant` calls in `Retry.WhileNull` too, not only the window-discovery loop.

### Only one appbar per edge

Windows allows only one Application Desktop Toolbar registered per screen edge at a time. The smoke test:

- Detects pre-existing `zring.exe` and `throw`s with a clear message before launching.
- Runs in a serialized xUnit collection (`[CollectionDefinition("AppBar", DisableParallelization = true)]`) so future tests cannot race each other.
