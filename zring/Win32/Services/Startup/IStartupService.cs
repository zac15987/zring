namespace Zring.Win32.Services.Startup;

/// <summary>
/// Encapsulates the Run on Windows Startup functionality
/// </summary>
public interface IStartupService
{
    /// <summary>
    /// Creates the link to Zring in Windows Startup folder
    /// </summary>
    /// <param name="description">Application description</param>
    /// <returns>True when the link was successfully created otherwise false</returns>
    bool CreateAppStartupLink(string? description);

    /// <summary>
    /// Retrieves the information whether Zring has link Windows Startup folder
    /// </summary>
    /// <returns>True when Zring has link Windows Startup folder otherwise false</returns>
    bool HasAppStartupLink();

    /// <summary>
    /// Removes the link to Zring from Windows Startup folder
    /// </summary>
    /// <returns>True when the link was successfully removed otherwise false</returns>
    bool RemoveAppStartupLink();
}