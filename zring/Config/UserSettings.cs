using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Zring.AppBar;
// ReSharper disable IdentifierTypo
// ReSharper disable CommentTypo

namespace Zring.Config;

/// <summary>
/// Application settings that can be changed by user
/// </summary>
public class UserSettings
{
    public const string UserSettingsFile = "appsettings.user.json";

    /// <summary>
    /// Startup dock mode of the application (default is <see cref="AppBarDockMode.Bottom"/>
    /// </summary>
    private AppBarDockMode appBarDock;
    /// <summary>
    /// Startup dock mode of the application (default is <see cref="AppBarDockMode.Bottom"/>
    /// </summary>
    public AppBarDockMode AppBarDock
    {
        get => appBarDock;
        set
        {
            if (value == appBarDock) return;
            appBarDock = value;
            isDirty = true;
        }
    }

    /// <summary>
    /// Flag whether to auto-size the application bar (default is true)
    /// </summary>
    private bool appBarAutoSize;
    /// <summary>
    /// Flag whether to auto-size the application bar (default is true)
    /// </summary>
    public bool AppBarAutoSize
    {
        get => appBarAutoSize;
        set
        {
            if (value == appBarAutoSize) return;
            appBarAutoSize = value;
            isDirty = true;
        }
    }

    /// <summary>
    /// The width of application bar when docked vertically (default is 200)
    /// </summary>
    private int appBarDockedWidth;
    /// <summary>
    /// The width of application bar when docked vertically (default is 200)
    /// </summary>
    public int AppBarDockedWidth
    {
        get => appBarDockedWidth;
        set
        {
            if (value == appBarDockedWidth) return;
            appBarDockedWidth = value;
            isDirty = true;
        }
    }

    /// <summary>
    /// The height of application bar when docked horizontally (default is 100)
    /// </summary>
    private int appBarDockedHeight;
    /// <summary>
    /// The height of application bar when docked horizontally (default is 100)
    /// </summary>
    public int AppBarDockedHeight
    {
        get => appBarDockedHeight;
        set
        {
            if (value == appBarDockedHeight) return;
            appBarDockedHeight = value;
            isDirty = true;
        }
    }

    /// <summary>
    /// AppId / Group keys of apps the user has chosen to show via the in-UI app filter.
    /// Empty or null means no filter (show all running windows).
    /// Keys are lowercase, matching <see cref="Dto.ButtonInfo.Group"/>.
    /// </summary>
    private List<string>? filteredAppKeys;
    /// <summary>
    /// AppId / Group keys of apps the user has chosen to show via the in-UI app filter.
    /// Empty or null means no filter (show all running windows).
    /// </summary>
    public List<string>? FilteredAppKeys
    {
        get => filteredAppKeys;
        set
        {
            if (SequenceEqualsOrdinalIgnoreCase(filteredAppKeys, value)) return;
            filteredAppKeys = value;
            isDirty = true;
        }
    }

    private static bool SequenceEqualsOrdinalIgnoreCase(List<string>? a, List<string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        var countA = a?.Count ?? 0;
        var countB = b?.Count ?? 0;
        if (countA != countB) return false;
        if (countA == 0) return true;
        for (var i = 0; i < countA; i++)
        {
            if (!string.Equals(a![i], b![i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>
    /// Full path to user settings json file
    /// </summary>
    private readonly string? fileName;

    /// <summary>
    /// Flag whether the user settings have been changed since last save
    /// </summary>
    private bool isDirty;

    /// <summary>
    /// Flag whethere the user settings if used in design time - this will block saving them
    /// </summary>
    private readonly bool isDesignTime;

    /// <summary>
    /// CTOR
    /// </summary>
    /// <param name="appSettings">Application settings to be used as initial source of configuraiton</param>
    /// <param name="isDesignTime">Flag whethere the user settings if used in design time - this will block saving them</param>
    public UserSettings(IAppSettings appSettings,bool isDesignTime=false)
    {
        var executablePath = Environment.ProcessPath;
        var executableDir = executablePath != null ? Path.GetDirectoryName(executablePath) : null;
        fileName = executableDir != null ? Path.Combine(executableDir, UserSettingsFile) : null;

        AppBarDock = appSettings.AppBarDock;
        AppBarAutoSize = appSettings.AppBarAutoSize;
        AppBarDockedWidth = appSettings.AppBarDockedWidth;
        appBarDockedHeight = appSettings.AppBarDockedHeight;

        this.isDesignTime=isDesignTime;
        if(!isDesignTime) Save();
    }

    /// <summary>
    /// Saves user settings into the json file
    /// </summary>
    /// <returns>True when saved successfully otherwise false</returns>
    public bool Save()
    {
        if (isDesignTime || !isDirty || string.IsNullOrEmpty(fileName)) return false;

        try
        {
            var jsonString = JsonSerializer.Serialize(new { AppSettings = this }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fileName, jsonString);
            isDirty = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the string representation of the object
    /// </summary>
    /// <returns>String representation of the object</returns>
    public override string ToString()
    {
        return $"[IsDirty:{isDirty}] Dock: {AppBarDock}, AutoSize:{AppBarAutoSize}, DockedWidth:{AppBarDockedWidth}, DockedHeight:{AppBarDockedHeight}";
    }
}