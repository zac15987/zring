using Zring.Dto;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;
using Zring.Win32.Services.Startup;
using Zring.Config;
using Zring.WpfExt;
using Microsoft.Extensions.Options;
using Zring.AppBar;
using Application = System.Windows.Application;
using Microsoft.Extensions.Logging;

namespace Zring.ViewModel
{
    public class MenuPopupViewModel : INotifyPropertyChanged
    {
        #region Logging
        /// <summary>
        /// Logger used
        /// </summary>
        private readonly ILogger logger;


        //EventIds:
        // 1xx - Windows API "interactions"
        // 2xx - Application Window Button interactions
        // 3xx - Telemetry
        // 9xx - Errors/Exceptions
        // ----
        // 1xxx - JumpList Service (19xx Errors/Exceptions)
        // 2xxx - Startup Service (29xx Errors/Exceptions)
        // 3xxx - AppBarWindow (39xx Errors/Exceptions)
        // 4xxx - BackgroundData Service (49xx Errors/Exceptions)

        /// <summary>
        /// Log definition options
        /// </summary>
        private static readonly LogDefineOptions LogOptions = new() { SkipEnabledCheck = true };
        //----------------------------------------------
        // 901 CantStartApp
        //----------------------------------------------

        /// <summary>
        /// Logger message definition for LogCantStartApp
        /// </summary>
        // ReSharper disable once InconsistentNaming
        private static readonly Action<ILogger, string, Exception?> __LogCantStartAppDefinition =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                new EventId(901, nameof(LogCantStartApp)),
                "Cant's start application {name}",
                LogOptions);

        /// <summary>
        /// Logs record (Warning) of exception thrown when starting the app from context menu or search result
        /// </summary>
        /// <param name="name">Name of the application</param>
        /// <param name="ex">Exception thrown</param>
        private void LogCantStartApp(string name, Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                __LogCantStartAppDefinition(logger, name, ex);
            }
        }
        #endregion

        /// <summary>
        /// Reference to <see cref="MainWindow"/> view model
        /// </summary>
        private MainViewModel Main { get; }

        /// <summary>
        /// Startup service to be used
        /// </summary>
        private IStartupService StartupService { get; }

        /// <summary>
        /// Language  service to be used
        /// </summary>
        private ILanguageService LanguageService { get; }

        /// <summary>
        /// Background data service to be used
        /// </summary>
        private IBackgroundDataService BackgroundDataService { get; }

        /// <summary>
        /// Flag whether the app uses the dark theme
        /// </summary>
        private bool IsDarkTheme => Main.IsDarkTheme;


        /// <summary>
        /// Application settings
        /// </summary>
        public IAppSettings Settings { get; }

        /// <summary>
        /// Array of the screen edges the app-bar can be docked to
        /// </summary>
        public EdgeInfo[] Edges { get; }

        /// <summary>
        /// Array of the information about all monitors (displays)
        /// </summary>
        public MonitorInfo[] AllMonitors => Main.AllMonitors;

        /// <summary>
        /// Flag whether the option to set Run On Windows Startup is available
        /// </summary>
        public bool RunOnWinStartupAvailable => Settings.AllowRunOnWindowsStartup;

        /// <summary>
        /// Flag whether the Zring is set to run on Windows startup
        /// </summary>
        private bool runOnWinStartupSet;
        /// <summary>
        /// Flag whether the Zring is set to run on Windows startup
        /// </summary>
        public bool RunOnWinStartupSet
        {
            get => runOnWinStartupSet;
            set
            {
                if (runOnWinStartupSet != value)
                {
                    runOnWinStartupSet = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Flag whether the settings panel is active
        /// </summary>
        private bool isInSettings;

        /// <summary>
        /// Flag whether the settings panel is active
        /// </summary>
        public bool IsInSettings
        {
            get => isInSettings;
            set
            {
                if (isInSettings != value)
                {
                    isInSettings = value;
                    if (isInSettings)
                    {
                        //switch panels
                        IsInColors = false;
                    }
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Flag whether the colors panel is active
        /// </summary>
        private bool isInColors;

        /// <summary>
        /// Flag whether the colors panel is active
        /// </summary>
        public bool IsInColors
        {
            get => isInColors;
            set
            {
                if (isInColors != value)
                {
                    isInColors = value;
                    if (isInColors)
                    {
                        //switch panels
                        IsInSettings = false;
                    }
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Flag whether the menu popup is active
        /// </summary>
        private bool isInMenuPopup;
        /// <summary>
        /// Flag whether the menu popup is active
        /// </summary>
        public bool IsInMenuPopup
        {
            get => isInMenuPopup;
            set
            {
                if (isInMenuPopup != value)
                {
                    isInMenuPopup = value;
                    if (isInMenuPopup)
                    {
                        IsInSettings = true; //default panel on open
                    }
                    else
                    {
                        IsInSettings = false; //ensure to "hide" settings
                        IsInColors = false; //ensure to "hide" colors
                    }

                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Flag whether the colors panel is enabled
        /// </summary>
        public bool IsColorsEnabled { get; }

        /// <summary>
        /// Information about brushes in Light and Dark themes
        /// </summary>
        public ObservableCollection<BrushInfo> ThemeBrushes { get; } = new();

        /// <summary>
        /// Command requesting to show settings
        /// </summary>
        public ICommand ShowSettingsCommand { get; }

        /// <summary>
        /// Command requesting to show colors
        /// </summary>
        public ICommand ShowColorsCommand { get; }

        /// <summary>
        /// Command requesting to toggle themes
        /// </summary>
        public ICommand ToggleThemeCommand { get; }

        /// <summary>
        /// Command requesting to toggle Run on Windows startup - set/remove the startup link
        /// </summary>
        public ICommand ToggleRunOnStartupCommand { get; }

        /// <summary>
        /// Command requesting an "ad-hoc" refresh of the list of application windows (no param used)
        /// </summary>
        public ICommand RefreshWindowCollectionCommand { get; }

        /// <summary>
        /// Internal CTOR
        /// </summary>
        /// <param name="main">Reference to main view model</param>
        /// <param name="settings">Application setting</param>
        /// <param name="logger">Logger to be used</param>
        /// <param name="startupService">Startup service to be used</param>
        /// <param name="languageService">Language  service to be used</param>
        /// <param name="backgroundDataService">Background Data service to be used</param>
        internal MenuPopupViewModel(MainViewModel main, IAppSettings settings, ILogger logger, IStartupService startupService, ILanguageService languageService, IBackgroundDataService backgroundDataService)
        {
            this.logger = logger;

            Main = main;
            Settings = settings;
            StartupService = startupService;
            LanguageService = languageService;
            BackgroundDataService = backgroundDataService;

            ToggleRunOnStartupCommand = new RelayCommand(ToggleRunOnWinStartup);
            ShowSettingsCommand = new RelayCommand(ShowSettings);
            ToggleThemeCommand = new RelayCommand(ToggleTheme);
            ShowColorsCommand = new RelayCommand(ShowColors);
            RefreshWindowCollectionCommand = new RelayCommand(Main.RefreshAllWindowsCollection);

            runOnWinStartupSet = startupService.HasAppStartupLink();
            IsColorsEnabled = Settings.FeatureFlag(AppSettings.FF_EnableColorsInMenuPopup, false);
            LanguageService = languageService;

            Edges = new EdgeInfo[] {
                new(AppBarDockMode.Left,LanguageService.Translate(TranslationKeys.EdgeLeft)),
                new(AppBarDockMode.Right,LanguageService.Translate(TranslationKeys.EdgeRight)),
                new(AppBarDockMode.Top,LanguageService.Translate(TranslationKeys.EdgeTop)),
                new(AppBarDockMode.Bottom,LanguageService.Translate(TranslationKeys.EdgeBottom))};
        }

        /// <summary>
        /// CTOR used by DI
        /// </summary>
        /// <param name="main">Reference to main view model</param>
        /// <param name="logger">Logger to be used</param>
        /// <param name="options">Application settings configuration</param>
        /// <param name="startupService">Startup service to be used</param>
        /// <param name="languageService">Language  service to be used</param>
        /// <param name="backgroundDataService">Background Data service to be used</param>
        // ReSharper disable once UnusedMember.Global
        public MenuPopupViewModel(MainViewModel main, ILogger<MainViewModel> logger, IOptions<AppSettings> options, IStartupService startupService, ILanguageService languageService, IBackgroundDataService backgroundDataService)
            : this(main, options.Value, logger, startupService, languageService, backgroundDataService)
        {
            //used from DI - DI populates the parameters and the internal CTOR is called then
        }

        /// <summary>
        /// Fills/Refreshes the <see cref="ThemeBrushes"/> collection from current theme
        /// </summary>
        internal void FillThemeResourcesColl()
        {
            foreach (var name in Enum.GetNames<ThemeResource>())
            {
                if (!name.ToLower().EndsWith("brush")) continue;
                var resource = Application.Current.Resources[name];
                if (resource is not Brush brush) continue;

                var brushInfo = ThemeBrushes.FirstOrDefault(b => b.Name == name);
                if (brushInfo == null)
                {
                    brushInfo = new BrushInfo(name, brush, IsDarkTheme);
                    ThemeBrushes.Add(brushInfo);
                }
                else
                {
                    brushInfo.SetBrush(brush, IsDarkTheme);
                }
            }
        }

        /// <summary>
        /// Toggles Run On Windows startup option.
        /// When it's being set, the Zring link is created in Windows startup folder
        /// When it's being re-set, the Zring link is removed from Windows startup folder
        /// </summary>
        private void ToggleRunOnWinStartup()
        {
            if (StartupService.HasAppStartupLink())
            {
                StartupService.RemoveAppStartupLink();
            }
            else
            {
                StartupService.CreateAppStartupLink("Zring application");
            }

            RunOnWinStartupSet = StartupService.HasAppStartupLink();
        }

        /// <summary>
        /// Switch the panel to Settings
        /// </summary>
        private void ShowSettings()
        {
            IsInSettings = true;
        }

        /// <summary>
        /// Switch the panel to Colors
        /// </summary>
        private void ShowColors()
        {
            IsInColors = true;
        }

        /// <summary>
        /// Toggle application theme between Light and Dark
        /// </summary>
        private void ToggleTheme()
        {
            ApplicationThemeManager.Apply(IsDarkTheme ? ApplicationTheme.Light : ApplicationTheme.Dark, WindowBackdropType.None, true);
            Main.IsDarkTheme = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

            //refresh info about brushes (colors)
            FillThemeResourcesColl();

            //Refresh after the switch
            Main.RefreshAllWindowsCollection(true);

#if DEBUG
            Debug.WriteLine($"Theme change to {(IsDarkTheme ? "Dark" : "Light")}");
#endif
        }

        /// <summary>
        /// Occurs when a property value changes
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Raise <see cref="PropertyChanged"/> event for given <paramref name="propertyName"/>
        /// </summary>
        /// <param name="propertyName">Name of the property changed</param>
        // ReSharper disable once UnusedMember.Global
        public void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
