using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zring.Config;
using Zring.Dto;
using Zring.WpfExt;

// ReSharper disable IdentifierTypo
// ReSharper disable CommentTypo

namespace Zring.ViewModel
{
    /// <summary>
    /// View model backing <see cref="Views.AppFilterControl"/>.
    /// Exposes the currently running apps (grouped by <see cref="ButtonInfo.Group"/>) as a
    /// checkable list; toggling an item persists the selection into
    /// <see cref="UserSettings.FilteredAppKeys"/>. An empty selection means "show all".
    /// </summary>
    public partial class AppFilterViewModel : INotifyPropertyChanged
    {
        /// <summary>
        /// Reference to the main view model (needed to read <see cref="MainViewModel.ButtonManager"/>
        /// and to trigger an immediate refresh after a toggle).
        /// </summary>
        private MainViewModel Main { get; }

        /// <summary>
        /// Application settings (read-only view, but <see cref="IAppSettings.UserSettings"/>
        /// is writable and is what we persist into).
        /// </summary>
        public IAppSettings Settings { get; }

        /// <summary>
        /// Background data service — used for fresh InstalledApplication lookups so the popup
        /// can show friendly app names (e.g. "Google Chrome") even for windows whose WndInfo
        /// lookup happened before background data was ready.
        /// </summary>
        private IBackgroundDataService BackgroundDataService { get; }

        /// <summary>
        /// Snapshot of currently running apps (distinct by <see cref="ButtonInfo.Group"/>).
        /// Rebuilt every time the popup opens.
        /// </summary>
        public ObservableCollection<AppFilterItem> RunningApps { get; } = new();

        /// <summary>
        /// Backing field for <see cref="IsPopupOpen"/>.
        /// </summary>
        private bool isPopupOpen;
        /// <summary>
        /// Two-way bound to the popup's IsOpen. Toggled by the funnel button.
        /// </summary>
        public bool IsPopupOpen
        {
            get => isPopupOpen;
            set
            {
                if (isPopupOpen == value) return;
                isPopupOpen = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Toggles <see cref="IsPopupOpen"/>. Bound to the funnel button.
        /// </summary>
        public ICommand TogglePopupCommand { get; }

        /// <summary>
        /// Clears all selections (resets the filter to "show all").
        /// </summary>
        public ICommand ClearAllCommand { get; }

        /// <summary>
        /// Internal CTOR (used both by DI and design-time).
        /// </summary>
        /// <param name="main">Main view model</param>
        /// <param name="settings">Application settings</param>
        /// <param name="logger">Logger</param>
        /// <param name="backgroundDataService">Background data service</param>
        internal AppFilterViewModel(MainViewModel main, IAppSettings settings, ILogger logger, IBackgroundDataService backgroundDataService)
        {
            Main = main;
            Settings = settings;
            this.logger = logger;
            BackgroundDataService = backgroundDataService;

            TogglePopupCommand = new RelayCommand(() => IsPopupOpen = !IsPopupOpen);
            ClearAllCommand = new RelayCommand(ClearAll);
        }

        /// <summary>
        /// DI CTOR
        /// </summary>
        // ReSharper disable once UnusedMember.Global
        public AppFilterViewModel(MainViewModel main, IOptions<AppSettings> options, ILogger<AppFilterViewModel> logger, IBackgroundDataService backgroundDataService)
            : this(main, options.Value, logger, backgroundDataService)
        {
        }

        /// <summary>
        /// Rebuild <see cref="RunningApps"/> from the current <see cref="MainViewModel.ButtonManager"/>
        /// state. Called every time the popup opens so the list reflects the live set of apps.
        /// </summary>
        public void OnPopupOpened()
        {
            var currentSelection = new HashSet<string>(
                Settings.UserSettings.FilteredAppKeys ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            //Read from the pre-filter snapshot on MainViewModel so the popup shows every running
            //app (Main.ButtonManager is post-filter and would hide everything not currently checked).
            var items = Main.LastEnumeratedWindows
                .Where(w => !string.IsNullOrEmpty(w.Group))
                .GroupBy(w => w.Group, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var representative = g.FirstOrDefault(w => w.BitmapSource != null) ?? g.First();
                    return new AppFilterItem(
                        key: g.Key,
                        displayName: ResolveDisplayName(g, representative),
                        icon: representative.BitmapSource,
                        windowCount: g.Count(),
                        isChecked: currentSelection.Contains(g.Key),
                        onToggle: OnItemToggled);
                })
                .OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            RunningApps.Clear();
            foreach (var item in items) RunningApps.Add(item);
        }

        /// <summary>
        /// Resolves a user-friendly app name for a group, preferring the installed-application
        /// name (from AppId or executable path) and falling back to the cleaned executable filename,
        /// then the window title as last resort.
        /// </summary>
        private string ResolveDisplayName(IEnumerable<WndInfo> group, WndInfo representative)
        {
            //1. Any wnd in the group that already has InstalledApplication populated wins.
            foreach (var w in group)
            {
                if (!string.IsNullOrWhiteSpace(w.InstalledApplication?.Name))
                    return w.InstalledApplication!.Name;
            }

            var installedApps = BackgroundDataService.InstalledApplications;

            //2. Fresh lookup by AppId (group key is lowercase AppId/executable per ButtonInfo.Group).
            if (!string.IsNullOrEmpty(representative.AppId))
            {
                var app = installedApps.GetInstalledApplicationFromAppId(representative.AppId);
                if (!string.IsNullOrWhiteSpace(app?.Name)) return app.Name;
            }

            //3. Fresh lookup by executable path.
            if (!string.IsNullOrEmpty(representative.Executable))
            {
                var app = installedApps.GetInstalledApplicationFromExecutable(representative.Executable);
                if (!string.IsNullOrWhiteSpace(app?.Name)) return app.Name;

                //4. Cleaned executable filename (e.g. "notepad++").
                var fileName = Path.GetFileNameWithoutExtension(representative.Executable);
                if (!string.IsNullOrWhiteSpace(fileName)) return fileName;
            }

            //5. Last resort: window title.
            return representative.Title;
        }

        /// <summary>
        /// Called by an item when its <see cref="AppFilterItem.IsChecked"/> value changes.
        /// Persists the new selection set and triggers an immediate refresh of the appbar.
        /// </summary>
        private void OnItemToggled(AppFilterItem item)
        {
            var current = Settings.UserSettings.FilteredAppKeys is { Count: > 0 }
                ? new List<string>(Settings.UserSettings.FilteredAppKeys)
                : new List<string>();

            var idx = current.FindIndex(k => string.Equals(k, item.Key, StringComparison.OrdinalIgnoreCase));
            if (item.IsChecked)
            {
                if (idx < 0) current.Add(item.Key);
            }
            else
            {
                if (idx >= 0) current.RemoveAt(idx);
            }

            Settings.UserSettings.FilteredAppKeys = current.Count == 0 ? null : current;
            Settings.UserSettings.Save();

            LogAppFilterChanged(item.Key, item.IsChecked, current.Count);

            //force an immediate refresh so the appbar reflects the change without waiting for the next tick
            Main.RefreshAllWindowsCollection(false);
        }

        /// <summary>
        /// Clears all selections and restores "show all" mode.
        /// </summary>
        private void ClearAll()
        {
            var previousCount = Settings.UserSettings.FilteredAppKeys?.Count ?? 0;
            if (previousCount == 0) return;

            Settings.UserSettings.FilteredAppKeys = null;
            Settings.UserSettings.Save();

            foreach (var item in RunningApps)
            {
                item.SetCheckedSilently(false);
            }

            LogAppFilterCleared(previousCount);

            Main.RefreshAllWindowsCollection(false);
        }

        /// <summary>
        /// Raised when a property value changes
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raise <see cref="PropertyChanged"/>
        /// </summary>
        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// One row in the <see cref="AppFilterViewModel.RunningApps"/> checkbox list.
    /// </summary>
    public class AppFilterItem : INotifyPropertyChanged
    {
        /// <summary>
        /// Lowercase group key (<see cref="ButtonInfo.Group"/>) — the identity used for persistence.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// User-facing name
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Icon (may be null)
        /// </summary>
        public BitmapSource? Icon { get; }

        /// <summary>
        /// Number of currently open windows belonging to this app
        /// </summary>
        public int WindowCount { get; }

        /// <summary>
        /// Callback invoked when <see cref="IsChecked"/> is flipped by the user.
        /// </summary>
        private readonly Action<AppFilterItem>? onToggle;

        /// <summary>
        /// Backing field for <see cref="IsChecked"/>.
        /// </summary>
        private bool isChecked;
        /// <summary>
        /// Two-way bound to the row's CheckBox
        /// </summary>
        public bool IsChecked
        {
            get => isChecked;
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                OnPropertyChanged();
                onToggle?.Invoke(this);
            }
        }

        /// <summary>
        /// Changes <see cref="IsChecked"/> without firing the <see cref="onToggle"/> callback.
        /// Used by "Clear all" which persists once instead of per-item.
        /// </summary>
        internal void SetCheckedSilently(bool value)
        {
            if (isChecked == value) return;
            isChecked = value;
            OnPropertyChanged(nameof(IsChecked));
        }

        /// <summary>
        /// CTOR
        /// </summary>
        public AppFilterItem(string key, string displayName, BitmapSource? icon, int windowCount, bool isChecked, Action<AppFilterItem>? onToggle)
        {
            Key = key;
            DisplayName = displayName;
            Icon = icon;
            WindowCount = windowCount;
            this.isChecked = isChecked;
            this.onToggle = onToggle;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
