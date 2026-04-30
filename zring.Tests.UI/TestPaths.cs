namespace Zring.Tests.UI;

internal static class TestPaths
{
    public static string FindZringExe()
    {
        var cfg =
#if DEBUG
            "Debug";
#else
            "Release";
#endif

        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "zring", "bin", cfg, "net10.0-windows10.0.17763.0", "zring.exe"));

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"zring.exe not found.\n" +
                $"  Expected: {path}\n" +
                $"  Build the zring project first (dotnet build zring.sln).",
                path);

        return path;
    }
}
