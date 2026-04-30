using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Xunit;

namespace Zring.Tests.UI;

[Collection("AppBar")]
public class SmokeTests
{
    [Fact]
    public void AppBar_Launches_And_Exposes_Landmarks()
    {
        FailIfZringAlreadyRunning();

        var app = LaunchZring();
        Window? window = null;
        try
        {
            using var automation = new UIA3Automation();
            window = WaitForMainWindow(app, automation);
            Assert.NotNull(window);

            // WrapPanel/Grid/Border etc. don't get UIA peers in WPF, so we anchor on
            // a real Control inside the bar instead of the panel itself.
            var menuToggle = Retry.WhileNull(
                () => window!.FindFirstDescendant(cf => cf.ByAutomationId("Zring.MenuToggle")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500)).Result;
            Assert.NotNull(menuToggle);
        }
        finally
        {
            CloseSafely(app, window);
        }
    }

    [Fact]
    public void AppFilter_Toggle_Closes_Open_Popup()
    {
        // Regression: clicking the open filter toggle used to close-then-immediately-reopen the
        // popup because Popup.StaysOpen=False's auto-close ran before the toggle's click logic.
        // AppFilterControl now uses StaysOpen=True with manual outside-click dismissal so the order
        // is well-defined. UIA's TogglePattern.Toggle() bypasses the WM_LBUTTONDOWN pipeline that
        // triggered the original race, so this MUST use physical Mouse.LeftClick to reproduce it.
        FailIfZringAlreadyRunning();

        var app = LaunchZring();
        Window? window = null;
        try
        {
            using var automation = new UIA3Automation();
            window = WaitForMainWindow(app, automation);
            Assert.NotNull(window);

            var filterToggle = Retry.WhileNull(
                () => window!.FindFirstDescendant(cf => cf.ByAutomationId("Zring.AppFilterToggle")),
                timeout: TimeSpan.FromSeconds(10),
                interval: TimeSpan.FromMilliseconds(500)).Result;
            Assert.NotNull(filterToggle);

            var togglePattern = filterToggle!.AsToggleButton();
            Assert.Equal(ToggleState.Off, togglePattern.ToggleState);

            // Open via real mouse click.
            var clickPoint = filterToggle.GetClickablePoint();
            Mouse.LeftClick(clickPoint);

            var openResult = Retry.WhileFalse(
                () => togglePattern.ToggleState == ToggleState.On,
                timeout: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromMilliseconds(100));
            Assert.True(openResult.Result, "Popup did not open after first click.");

            // Close via second real mouse click on the same toggle. Without the fix, the popup
            // closes from outside-click detection then the toggle re-opens it (state ends On).
            Mouse.LeftClick(clickPoint);

            var closeResult = Retry.WhileFalse(
                () => togglePattern.ToggleState == ToggleState.Off,
                timeout: TimeSpan.FromSeconds(5),
                interval: TimeSpan.FromMilliseconds(100));
            Assert.True(closeResult.Result,
                "Popup did not close after clicking the toggle while it was open — close-then-reopen race regressed.");
        }
        finally
        {
            CloseSafely(app, window);
        }
    }

    private static Application LaunchZring()
    {
        var exePath = TestPaths.FindZringExe();
        var psi = new ProcessStartInfo(exePath)
        {
            Arguments = "AppSettings:RefreshWindowInfosIntervalMs=10000",
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
        };
        return Application.Launch(psi);
    }

    private static Window? WaitForMainWindow(Application app, UIA3Automation automation)
    {
        return Retry.WhileNull(() =>
        {
            var top = app.GetAllTopLevelWindows(automation);
            return top.FirstOrDefault(w => w.AutomationId == "Zring.MainWindow");
        }, timeout: TimeSpan.FromSeconds(10), interval: TimeSpan.FromMilliseconds(500)).Result;
    }

    private static void FailIfZringAlreadyRunning()
    {
        var existing = Process.GetProcessesByName("zring");
        if (existing.Length > 0)
        {
            foreach (var p in existing) p.Dispose();
            throw new InvalidOperationException(
                "An existing zring.exe process is running. " +
                "Close it before running UI tests — only one appbar can be registered per edge.");
        }
    }

    private const uint WM_CLOSE = 0x0010;

    private static void CloseSafely(Application app, Window? window)
    {
        // FlaUI's app.Close() has a hardcoded 5s timeout that races zring's App.OnExit
        // host.StopAsync(5s). Process.CloseMainWindow can't help either: zring's MainWindow
        // is WS_EX_TOOLWINDOW so Process.MainWindowHandle is 0 and CloseMainWindow returns
        // false without sending anything. Send WM_CLOSE directly to the UIA-discovered HWND
        // so App.OnExit runs and ABM_REMOVE is sent, restoring the desktop work area.
        Process? proc;
        try { proc = Process.GetProcessById(app.ProcessId); }
        catch (ArgumentException) { return; }

        using (proc)
        {
            if (proc.HasExited) return;

            if (window != null)
            {
                User32.SendMessage(window.Properties.NativeWindowHandle.Value, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            else
            {
                proc.CloseMainWindow();
            }

            if (!proc.WaitForExit(15_000))
            {
                // Kill skips ABM_REMOVE -> desktop work area may stay shrunk.
                Console.Error.WriteLine(
                    "WARNING: zring did not exit within 15s; killing. " +
                    "Desktop work area may be shrunk until next logon or explorer.exe restart.");
                proc.Kill();
                proc.WaitForExit(5_000);
            }
        }
    }
}

[CollectionDefinition("AppBar", DisableParallelization = true)]
public class AppBarCollection { }
