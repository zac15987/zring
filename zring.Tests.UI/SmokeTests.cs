using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
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

        var exePath = TestPaths.FindZringExe();
        var psi = new ProcessStartInfo(exePath)
        {
            Arguments = "AppSettings:RefreshWindowInfosIntervalMs=10000",
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
        };

        var app = Application.Launch(psi);
        try
        {
            using var automation = new UIA3Automation();

            var window = Retry.WhileNull(() =>
            {
                var top = app.GetAllTopLevelWindows(automation);
                return top.FirstOrDefault(w => w.AutomationId == "Zring.MainWindow");
            }, timeout: TimeSpan.FromSeconds(10), interval: TimeSpan.FromMilliseconds(500)).Result;

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
            CloseSafely(app);
        }
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

    private static void CloseSafely(Application app)
    {
        // FlaUI's app.Close() has a hardcoded 5s timeout that races zring's App.OnExit
        // host.StopAsync(5s); drive WM_CLOSE via Process directly with a longer wait.
        Process? proc;
        try { proc = Process.GetProcessById(app.ProcessId); }
        catch (ArgumentException) { return; }

        using (proc)
        {
            if (proc.HasExited) return;

            proc.CloseMainWindow();
            if (!proc.WaitForExit(15_000))
            {
                // Kill skips ABM_REMOVE -> desktop work area may stay shrunk.
                Console.Error.WriteLine(
                    "WARNING: zring did not exit within 15s; killing. " +
                    "Desktop work area may be shrunk until next logon or explorer.exe restart.");
                proc.Kill();
            }
        }
    }
}

[CollectionDefinition("AppBar", DisableParallelization = true)]
public class AppBarCollection { }
