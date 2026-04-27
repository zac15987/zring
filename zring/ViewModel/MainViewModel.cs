using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Zring.Config;
using Zring.Dto;
using Zring.Win32.NativeInterfaces.Extensions;
using Zring.Win32.Services;
using Zring.Win32.Services.JumpLists;
using Zring.Win32.Services.Pins;
using Zring.Win32.Services.Shell;
using Zring.Win32.Services.Shell.Properties;
using Zring.WpfExt;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using static Zring.Dto.PinnedAppInfo;
using Image = System.Windows.Controls.Image;
using MenuItem = System.Windows.Controls.MenuItem;
using RelayCommand = Zring.WpfExt.RelayCommand;

// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo
// ReSharper disable CommentTypo

namespace Zring.ViewModel
{
    /// <summary>
    /// The ViewModel for <see cref="MainWindow"/>.
    /// Encapsulates the data and logic related to "task bar applications/windows"
    ///  - pulling the list of them, switching the apps, presenting the thumbnails
    /// </summary>
    public partial class MainViewModel : INotifyPropertyChanged
    {

        /// <summary>
        /// Native handle (HWND) of the <see cref="MainWindow"/>
        /// </summary>
        private IntPtr mainWindowHwnd;
        /// <summary>
        /// Native handle of the application window thumbnail currently shown
        /// </summary>
        private IntPtr thumbnailHandle;

        /// <summary>
        /// <see cref="DispatcherTimer"/> used to periodically pull (refresh) the information about (open) application windows
        /// </summary>
        private readonly DispatcherTimer timer;

        /// <summary>
        /// Flag whether the <see cref="RefreshAllWindowsCollection"/> method is run for the first time
        /// </summary>
        private bool isFirstRun = true;


        /// <summary>
        /// Native handle of the last known foreground window
        /// </summary>
        private IntPtr lastForegroundWindow = IntPtr.Zero;

        /// <summary>
        /// Grace deadline after a user-initiated minimize during which the refresh loop
        /// must NOT adopt whatever window Windows picks as the next foreground.
        /// Without this, minimizing an app through the bar immediately lights up the
        /// next Z-ordered window (often an unrelated background app) as "active".
        /// </summary>
        private DateTime suppressForegroundUpdateUntil = DateTime.MinValue;

        /// <summary>
        /// Information about the applications installed in system
        /// </summary>
        private InstalledApplications InstalledApplications => backgroundDataService.InstalledApplications;

        /// <summary>
        /// Information about the applications pinned in the taskbar
        /// </summary>
        internal PinnedAppInfo[] TaskbarPinnedApplications { get; set; } = Array.Empty<PinnedAppInfo>();

        /// <summary>
        /// Snapshot of every visible window enumerated in the last <see cref="RefreshAllWindowsCollection"/>
        /// tick, BEFORE the app filter is applied. Used by <see cref="AppFilterViewModel"/> to show
        /// all currently running apps in the filter popup (otherwise the popup would only see what
        /// already passed the filter).
        /// </summary>
        private readonly List<WndInfo> lastEnumeratedWindows = new();
        /// <summary>
        /// Read-only view of <see cref="lastEnumeratedWindows"/>.
        /// </summary>
        public IReadOnlyList<WndInfo> LastEnumeratedWindows => lastEnumeratedWindows;

        /// <summary>
        /// Information about the known folder paths and GUIDs
        /// </summary>
        private StringGuidPair[] knownFolders = Array.Empty<StringGuidPair>();

        /// <summary>
        /// Dictionary of known AppIds from configuration containing pairs executable-appId (the key is in lower case)
        /// When built from configuration, the record (key) is created for full path from config and another one without a path (file name only) if applicable
        /// </summary>
        private readonly Dictionary<string, string> knownAppIds;

        /// <summary>
        /// Application settings
        /// </summary>
        public IAppSettings Settings { get; }

        /// <summary>
        /// Flag whether the colors panel is enabled
        /// </summary>
        public bool IsContextMenuOnThumbnailEnabled { get; }

        /// <summary>
        /// Flag whether the app uses the dark theme 
        /// </summary>
        private bool isDarkTheme;

        /// <summary>
        /// Flag whether the app uses the dark theme  
        /// </summary>
        public bool IsDarkTheme
        {
            get => isDarkTheme;
            set
            {
                if (isDarkTheme != value)
                {
                    isDarkTheme = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Array of the information about all monitors (displays)
        /// </summary>
        public MonitorInfo[] AllMonitors { get; }

        /// <summary>
        /// Application window buttons group manager
        /// </summary>
        public AppButtonManager ButtonManager { get; }

        /// <summary>
        /// Command requesting an "ad-hoc" refresh of the list of application windows (no param used)
        /// </summary>
        public ICommand RefreshWindowCollectionCommand { get; }
        /// <summary>
        /// Command requesting the toggle of the application window.
        /// Switch the application window to foreground or minimize it
        /// The command parameter is HWND of the application window
        /// </summary>
        public ICommand ToggleApplicationWindowCommand { get; }
        /// <summary>
        /// Command requesting the render of application window thumbnail into the popup
        /// The command parameter is fully populated <see cref="ThumbnailPopupCommandParams"/> object
        /// </summary>
        public ICommand ShowThumbnailCommand { get; }
        /// <summary>
        /// Command requesting to hide application window thumbnail (no param used)
        /// </summary>
        public ICommand HideThumbnailCommand { get; }

        /// <summary>
        /// Command requesting to build the context menu for application window button
        /// </summary>
        public ICommand BuildContextMenuCommand { get; }

        /// <summary>
        /// Command requesting to launch pinned application
        /// </summary>
        public ICommand LaunchPinnedAppCommand { get; }

        /// <summary>
        /// Command requesting to close the application window
        /// </summary>
        public ICommand CloseApplicationWindowCommand { get; }

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
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// JumpList service to be used
        /// </summary>
        private readonly IJumpListService jumpListService;

        /// <summary>
        /// Language  service to be used
        /// </summary>
        private readonly ILanguageService languageService;

        /// <summary>
        /// Pins  service to be used
        /// </summary>
        private readonly IPinsService pinsService;

        /// <summary>
        /// Background data service to be used
        /// </summary>
        private readonly IBackgroundDataService backgroundDataService;

        /// <summary>
        /// Map used for simple anonymization
        /// </summary>
        /// 
        private readonly Dictionary<char, char> anonymizeMap = new();

        /// <summary>
        /// Internal CTOR
        /// Directly used by <see cref="ViewModelLocator"/> when creating a design time instance.
        /// Internally called by public "DI bound" CTOR
        /// </summary>
        /// <param name="settings">Application setting</param>
        /// <param name="logger">Logger to be used</param>
        /// <param name="jumpListService">JumpList service to be used</param>
        /// <param name="languageService">Language service to be used</param>
        /// <param name="backgroundDataService">Background Data service to be used</param>
        /// <param name="pinsService">Pins service to be used</param>
        internal MainViewModel(IAppSettings settings, ILogger logger, IJumpListService jumpListService, ILanguageService languageService, IBackgroundDataService backgroundDataService, IPinsService pinsService)
        {
            this.logger = logger;
            this.jumpListService = jumpListService;
            this.languageService = languageService;
            this.backgroundDataService = backgroundDataService;
            this.pinsService = pinsService;

            Settings = settings;
            AllMonitors = Monitor.GetAllMonitors();
            ButtonManager = new AppButtonManager(Settings);

            RefreshWindowCollectionCommand = new RelayCommand(RefreshAllWindowsCollection);
            ToggleApplicationWindowCommand = new RelayCommand(ToggleApplicationWindow);
            ShowThumbnailCommand = new RelayCommand(ShowThumbnail);
            HideThumbnailCommand = new RelayCommand(HideThumbnail);
            BuildContextMenuCommand = new RelayCommand(BuildContextMenu);
            LaunchPinnedAppCommand = new RelayCommand(LaunchPinnedApp);
            CloseApplicationWindowCommand = new RelayCommand(CloseApplicationWindow);

            knownAppIds = settings.GetKnowAppIds();

            if (settings.FeatureFlag(AppSettings.FF_AnonymizeWindows, false))
            {
                InitAnonymizeMap();
            }
            IsContextMenuOnThumbnailEnabled = Settings.FeatureFlag(AppSettings.FF_EnableContextMenuOnThumbnail, false);

            timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(Settings.RefreshWindowInfosIntervalMs)
            };
            timer.Tick += (_, _) => { if (!ButtonManager.IsBusy) RefreshAllWindowsCollection(false); };

            this.languageService = languageService;
        }


        /// <summary>
        /// CTOR used by DI
        /// </summary>
        /// <param name="options">Application settings configuration</param>
        /// <param name="logger">Logger to be used</param>
        /// <param name="languageService">Language service to be used</param>
        /// <param name="jumpListService">JumpList service to be used</param>
        /// <param name="backgroundDataService">Background Data service to be used</param>
        /// <param name="pinsService">Pins service to be used</param>
        // ReSharper disable once UnusedMember.Global
        public MainViewModel(IOptions<AppSettings> options, ILogger<MainViewModel> logger, IJumpListService jumpListService, ILanguageService languageService, IBackgroundDataService backgroundDataService, IPinsService pinsService)
            : this(options.Value, logger, jumpListService, languageService, backgroundDataService, pinsService)
        {
            //used from DI - DI populates the parameters and the internal CTOR is called then
        }


        /// <summary>
        /// (Late) initialize the view model.
        /// Registers the native handle (HWND) of the <see cref="MainWindow"/> and
        /// starts the <see cref="timer"/> used to periodically populate the information about application windows
        /// </summary>
        /// <param name="mainWndHwnd">Native handle (HWND) of the <see cref="MainWindow"/></param>
        public void Init(IntPtr mainWndHwnd)
        {
            mainWindowHwnd = mainWndHwnd;

            //init theme
            ApplicationTheme theme;
            switch (Settings.StartupTheme)
            {
                case StartupThemeEnum.System:
                    {
                        var systemTheme = ApplicationThemeManager.GetSystemTheme();
                        var isSystemThemeDark = systemTheme is SystemTheme.Dark or SystemTheme.CapturedMotion or SystemTheme.Glow;
                        theme = isSystemThemeDark ? ApplicationTheme.Dark : ApplicationTheme.Light;
                        break;
                    }
                case StartupThemeEnum.Light:
                    theme = ApplicationTheme.Light;
                    break;
                case StartupThemeEnum.Dark:
                default:
                    theme = ApplicationTheme.Dark;
                    break;
            }

            ApplicationThemeManager.Apply(theme, WindowBackdropType.None, true);
            IsDarkTheme = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

            //start app windows refresh timer
            timer.Start();
        }



        /// <summary>
        /// Initialize character map for simple anonymization
        /// </summary>
        private void InitAnonymizeMap()
        {
            var rnd = new Random(DateTime.Now.GetHashCode());
            for (var c = 'a'; c <= 'z'; c++)
            {
                anonymizeMap[c] = (char)('a' + rnd.Next(26));
            }
            for (var c = 'A'; c <= 'Z'; c++)
            {
                anonymizeMap[c] = (char)('A' + rnd.Next(26));
            }
            for (var c = '0'; c <= '9'; c++)
            {
                anonymizeMap[c] = (char)('0' + rnd.Next(26));
            }
            foreach (var c in " -.:/")
            {
                anonymizeMap[c] = (char)('a' + rnd.Next(26));
            }
        }

        /// <summary>
        /// Simple anonymize given string
        /// </summary>
        /// <param name="s">string to anonymize</param>
        /// <param name="saltInt">anonymization salt</param>
        /// <returns>anonymized string</returns>
        private string? Anonymize(string? s, int saltInt)
        {
            if (s == null) return null;
            var salt = saltInt.ToString();
            while (salt.Length < s.Length)
            {
                salt += salt;
            }

            var retVal = string.Empty;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                var cs = (char)(c + salt[i]);
                retVal += anonymizeMap.TryGetValue(cs, out var a) ? a : anonymizeMap.TryGetValue(c, out a) ? a : c;
            }

            return retVal;
        }


        /// <summary>
        /// Pulls the information about available application windows and updates <see cref="ButtonManager"/> window collection.
        /// </summary>
        /// <param name="hardRefresh">When the parameter is bool and true, it forces the hard refresh.
        /// The <see cref="ButtonManager"/>window collection is cleared first and the background data are refreshed on hard refresh.
        ///  Otherwise just the window collection is updated</param>
        internal void RefreshAllWindowsCollection(object? hardRefresh)
        {
            var isHardRefresh = (hardRefresh is bool b || bool.TryParse(hardRefresh?.ToString(), out b)) && b;

            if (isFirstRun)
            {
                isHardRefresh = true;
                isFirstRun = false;
            }

            //Reset the pre-filter snapshot for this tick; populated inside the enum callback below.
            lastEnumeratedWindows.Clear();

            if (isHardRefresh)
            {
                //get known folders
                knownFolders = Shell.GetKnownFolders();

                //reload taskbar pinned applications
                TaskbarPinnedApplications = pinsService.RefreshTaskbarPins();

                ButtonManager.BeginHardRefresh(TaskbarPinnedApplications);

                //Refresh also background data (installed apps, Start pins)
                backgroundDataService.Refresh();
            }
            else
            {
                //begin update of windows collection
                ButtonManager.BeginUpdate();
            }

            //Retrieve the current foreground window
            var foregroundWindow = WndAndApp.GetForegroundWindow();
            if (foregroundWindow != mainWindowHwnd)
            {
                //During the post-minimize grace period, skip the lastForegroundWindow update
                //so no arbitrary next-in-Z-order app briefly lights up as "active".
                //Any explicit ActivateWindow call still sets SetForegroundWindow synchronously
                //and will be picked up once the grace period ends.
                if (DateTime.UtcNow >= suppressForegroundUpdateUntil)
                {
                    lastForegroundWindow = foregroundWindow; //"filter out" the main window as being the foreground one to proper handle the toggle
                }

                if (IsInMenuPopup && !Settings.FeatureFlag(AppSettings.FF_KeepMenuPopupOpen, false)) IsInMenuPopup = false; //cancel popup/search when other app get's focus
            }

            //Enum windows
            WndAndApp.EnumVisibleWindows(
                mainWindowHwnd,
                hwnd => ButtonManager[hwnd],
                (hwnd, wnd, caption, threadId, processId, ptrProcess) =>
                {
                    //caption anonymization
                    if (Settings.FeatureFlag<bool>(AppSettings.FF_AnonymizeWindows))
                    {
                        var appName =
                            InstalledApplications.GetInstalledApplicationFromAppId(wnd?.AppId ?? string.Empty)?.Name ??
                            InstalledApplications.GetInstalledApplicationFromExecutable(wnd?.Executable ?? string.Empty)?.Name;

                        if (caption != appName)
                        {
                            caption = (string.IsNullOrEmpty(appName) ? "" : $"{appName} - ") + Anonymize(caption, hwnd.ToInt32());
                        }
                    }

                    //Check whether it's a "new" application window or a one already existing in the ButtonManager
                    if (wnd == null)
                    {
                        //app executable 
                        var executable = WndAndApp.GetProcessExecutable(ptrProcess);
                        //new window
                        wnd = new WndInfo(hwnd, caption, threadId, processId, executable);

                        //Try to get AppUserModelId using the win32 app resolver, no need to wait for background data
                        if (Settings.FeatureFlag<bool>(AppSettings.FF_UseApplicationResolver))
                        {
                            var appId = WndAndApp.GetWindowApplicationUserModelId(hwnd);
                            wnd.AppId = appId;
                            if (appId != null)
                            {
                                wnd.InstalledApplication = InstalledApplications.GetInstalledApplicationFromAppId(appId);
                                wnd.PinnedApplication = TaskbarPinnedApplications.FirstOrDefault(p => p.AppId == appId);
                                LogAppIdResolved(hwnd, caption, appId, "Resolver");
                            }
                        }
                    }
                    else
                    {
                        //existing (known) window - MarkToKeep is deferred to the end of the callback,
                        //so that the app filter below can drop the window by leaving it marked for removal.
                        wnd.Title = caption; //update the title

                        if (wnd.InstalledApplication == null && wnd.AppId != null)
                        {
                            //try to update reference to installed app
                            wnd.InstalledApplication = InstalledApplications.GetInstalledApplicationFromAppId(wnd.AppId);
                        }
                    }

                    wnd.IsForeground = hwnd == lastForegroundWindow; //check whether the window is foreground window (will be highlighted in UI)


                    if ((wnd.AppId is null || Settings.CheckForAppIdChange) && backgroundDataService.BackgroundDataRetrieved) // || isHardRefresh - not needed as wnd will be new with AppId =null
                    {
                        string? appUserModelId = null;
                        string? appIdSource = null;

                        //Try to get AppUserModelId using the win32 app resolver
                        if (Settings.FeatureFlag<bool>(AppSettings.FF_UseApplicationResolver))
                        {
                            appUserModelId = WndAndApp.GetWindowApplicationUserModelId(hwnd);
                            if (appUserModelId != null) appIdSource = "Resolver";
                        }

                        //Try to get AppUserModelId from window - for windows that explicitly define the AppId
                        if (appUserModelId == null)
                        {
                            var store = Shell.GetPropertyStoreForWindow(wnd.Hwnd);
                            if (store != null)
                            {
                                var hr = store.GetCount(out var c);
                                if (hr.IsSuccess && c > 0)
                                {
                                    //try to get AppUserModelId property
                                    appUserModelId = store.GetPropertyValue<string>(PropertyKey.PKEY_AppUserModel_ID);
                                    var shellProperties = store.GetProperties();
                                    wnd.ShellProperties = shellProperties;
                                    if (appUserModelId != null) appIdSource = "PropertyStore";
                                }
                            }
                        }

                        //Try the app ids from configuration
                        //It must contain the record for (shell) explorer (done in CTOR) as it will not work properly for explorer windows without this hack
                        if (appUserModelId == null && wnd.Executable != null &&
                            (knownAppIds.TryGetValue(wnd.Executable.ToLowerInvariant(), out var appId) ||
                             knownAppIds.TryGetValue(Path.GetFileName(wnd.Executable.ToLowerInvariant()), out appId)))
                        {
                            appUserModelId = appId;
                            appIdSource = "KnownAppIds";
                        }


                        //try to get AppUserModelId from process if not "at window"
                        if (appUserModelId == null)
                        {
                            appUserModelId = WndAndApp.GetProcessApplicationUserModelId(ptrProcess);
                            if (appUserModelId != null) appIdSource = "Process";
                        }

                        if (appUserModelId == null && !string.IsNullOrEmpty(wnd.Executable))
                        {
                            //try to get from installed app (identified by executable) or use executable as fallback
                            appUserModelId = InstalledApplications.GetAppIdFromExecutable(wnd.Executable, out var _);
                            if (appUserModelId != null) appIdSource = "InstalledApplication";
                        }

                        if (appUserModelId != null)
                        {
                            wnd.InstalledApplication = InstalledApplications.GetInstalledApplicationFromAppId(appUserModelId);
                            wnd.PinnedApplication = TaskbarPinnedApplications.FirstOrDefault(p => p.AppId == appUserModelId);
                            LogAppIdResolved(hwnd, caption, appUserModelId, appIdSource ?? "Unknown");
                        }
                        else
                        {
                            LogAppIdResolutionFailed(hwnd, caption, wnd.Executable ?? "<unknown>");
                        }

                        wnd.AppId = appUserModelId;
                    }



                    if (Settings.CheckForIconChange || isHardRefresh)
                    {
                        //Try to retrieve the window icon
                        wnd.BitmapSource = WndAndApp.GetWindowIcon(hwnd);

                        if (wnd.BitmapSource == null)
                        {
                            //try to get icon from installed application
                            if (!string.IsNullOrEmpty(wnd.AppId))
                            {
                                wnd.BitmapSource = InstalledApplications.GetInstalledApplicationFromAppId(wnd.AppId)?.IconSource;
                            }
                        }

                        wnd.BitmapSource = InvertBitmapIfApplicable(wnd.BitmapSource);
                    }

                    //Record the pre-filter snapshot so the filter popup can list every running app,
                    //not just the ones that already passed the filter.
                    lastEnumeratedWindows.Add(wnd);

                    //App filter gate: when active and this window's group is not selected,
                    //skip both Add (for new) and MarkToKeep (for existing).
                    //Property mutations above (Title/IsForeground/...) flip ChangeStatus from
                    //ToRemove to Changed via WndInfo.OnPropertyChanged, so re-mark explicitly
                    //to guarantee EndUpdate drops this window.
                    if (Settings.IsAppFilterActive && !Settings.MatchesAppFilter(wnd.Group))
                    {
                        if (wnd.ChangeStatus != WndInfo.ChangeStatusEnum.New)
                        {
                            wnd.MarkForRemoval();
                        }
                        return;
                    }

                    //Add new window to button manager or keep existing
                    if (wnd.ChangeStatus == WndInfo.ChangeStatusEnum.New)
                    {
                        ButtonManager.Add(wnd);
                    }
                    else
                    {
                        wnd.MarkToKeep();
                    }

                    if (isHardRefresh)
                    {
                        LogEnumeratedWindowInfo(wnd.ToString());
                    }

                }); //enum visible windows

            ButtonManager.EndUpdate();

        }

        /// <summary>
        /// Inverts the bitmap when in the light scheme, <see cref="IAppSettings.InvertWhiteIcons"/> is set and the bitmap is white pixels only or
        /// in the dark scheme, <see cref="IAppSettings.InvertBlackIcons"/> is set and the bitmap is black pixels only
        /// </summary>
        /// <param name="bitmapSource"></param>
        /// <returns></returns>
        private BitmapSource? InvertBitmapIfApplicable(BitmapSource? bitmapSource)
        {
            if (bitmapSource == null) return null;
            if (Settings.InvertWhiteIcons && !IsDarkTheme) return Resource.InvertBitmapIfWhiteOnly(bitmapSource);
            if (Settings.InvertBlackIcons && IsDarkTheme) return Resource.InvertBitmapIfBlackOnly(bitmapSource);
            return bitmapSource;
        }

        /// <summary>
        /// Switch the application window with given <paramref name="hwnd"/> to foreground or minimize it.
        /// </summary>
        /// <remarks>
        /// The function doesn't throw any exception when the handle is invalid, it just ignores it end "silently" returns
        /// </remarks>
        /// <param name="hwnd">Native handle (HWND) of the application window</param>
        private void ToggleApplicationWindow(object? hwnd)
        {
            if (hwnd is not IntPtr hWndIntPtr || hWndIntPtr == IntPtr.Zero) return; //invalid command parameter, do nothing
            ToggleApplicationWindow(hWndIntPtr, false);
        }

        /// <summary>
        /// Switch the application window with given <paramref name="hwnd"/> to foreground or minimize it (if <paramref name="forceActivate"/> is not set).
        /// </summary>
        /// <remarks>
        /// The function doesn't throw any exception when the handle is invalid, it just ignores it end "silently" returns
        /// </remarks>
        /// <param name="hwnd">Native handle (HWND) of the application window</param>
        /// <param name="forceActivate">When the flag is set, the window is always activated. When it's false and the window is foreground already, it's minimized</param>
        public void ToggleApplicationWindow(IntPtr hwnd, bool forceActivate)
        {
            if (hwnd == IntPtr.Zero) return; //invalid command parameter, do nothing

            //got the handle, get the window information
            var wnd = ButtonManager[hwnd];
            if (wnd is null) return; //unknown window, do nothing

            if (wnd.IsForeground && !forceActivate)
            {
                //it's a foreground window - minimize it and return
                WndAndApp.MinimizeWindow(hwnd);
                //Clear IsForeground immediately. If our appbar grabs focus on the click,
                //the refresh loop's guard (foregroundWindow != mainWindowHwnd) keeps
                //lastForegroundWindow stale at this hwnd, which would otherwise leave
                //IsForeground stuck true and make subsequent clicks minimize a no-op.
                wnd.IsForeground = false;
                if (lastForegroundWindow == hwnd) lastForegroundWindow = IntPtr.Zero;
                //Suppress the next few refresh ticks from adopting whichever window Windows
                //promotes to foreground (usually an arbitrary next-in-Z-order window like the
                //IDE/editor). Without this, the UI briefly highlights that window as "active".
                suppressForegroundUpdateUntil = DateTime.UtcNow.AddMilliseconds(500);
                LogMinimizeApp(hwnd, wnd.Title);
                return;
            }

            var wasMinimized = WndAndApp.ActivateWindow(hwnd);
            LogSwitchApp(hwnd, wnd.Title, wasMinimized);

            //refresh the window list
            RefreshAllWindowsCollection(false);
        }

        /// <summary>
        /// Registers and shows the application window thumbnail within the popup
        /// Parameter <paramref name="param"/> must be <see cref="ThumbnailPopupCommandParams"/> object,
        /// encapsulating <see cref="ThumbnailPopupCommandParams.SourceHwnd"/> of application window,
        /// <see cref="ThumbnailPopupCommandParams.TargetHwnd"/> of the popup window and
        /// <see cref="ThumbnailPopupCommandParams.TargetRect"/> with the bounding box within the popup window.
        /// </summary>
        /// <param name="param"><see cref="ThumbnailPopupCommandParams"/> object with source and target information</param>
        /// <exception cref="ArgumentException">When the <paramref name="param"/> is not <see cref="ThumbnailPopupCommandParams"/> object or is null, <see cref="ArgumentException"/> is thrown</exception>
        private void ShowThumbnail(object? param)
        {
            if (param is not ThumbnailPopupCommandParams cmdParams)
            {
                LogWrongCommandParameter(nameof(ThumbnailPopupCommandParams));
                throw new ArgumentException($"Command parameter must be {nameof(ThumbnailPopupCommandParams)}", nameof(param));
            }

            HideThumbnail(); //unregister (hide) existing thumbnail if any
            if (cmdParams.SourceHwnd == IntPtr.Zero) return;

            thumbnailHandle = Thumbnail.ShowThumbnail(cmdParams.SourceHwnd, cmdParams.TargetHwnd, (Rect)cmdParams.TargetRect, out var thumbCentered);

            LogShowThumbnail(cmdParams.SourceHwnd, cmdParams.TargetHwnd, thumbCentered, thumbnailHandle);
        }

        /// <summary>
        /// Unregister (hide) the existing thumbnail identified by <see cref="MainViewModel.thumbnailHandle"/>
        /// </summary>
        /// <remarks>
        /// When there is no thumbnail (<see cref="MainViewModel.thumbnailHandle"/> is <see cref="IntPtr.Zero"/>),
        /// no exception is thrown and the method "silently" returns
        /// </remarks>
        private void HideThumbnail()
        {
            if (thumbnailHandle == IntPtr.Zero) return;

            Thumbnail.HideThumbnail(thumbnailHandle);
            LogHideThumbnail(thumbnailHandle);
            thumbnailHandle = IntPtr.Zero;
        }


        /// <summary>
        /// Close the application window 
        /// Parameter <paramref name="param"/> must be <see cref="WndInfo"/> object
        /// </summary>
        /// <param name="param"><see cref="WndInfo"/> object </param>
        /// <exception cref="ArgumentException">When the <paramref name="param"/> is not <see cref="WndInfo"/> object or is null, <see cref="ArgumentException"/> is thrown</exception>
        private void CloseApplicationWindow(object? param)
        {
            if (param is not WndInfo wndInfo)
            {
                LogWrongCommandParameter(nameof(WndInfo));
                throw new ArgumentException($"Command parameter must be {nameof(WndInfo)}", nameof(param));
            }

            WndAndApp.CloseWindow(wndInfo!.Hwnd);
        }

        /// <summary>
        /// Launches the pinned application
        /// Parameter <paramref name="param"/> must be <see cref="PinnedAppInfo"/> object
        /// </summary>
        /// <param name="param"><see cref="PinnedAppInfo"/> object with reference to pinned application</param>
        /// <exception cref="ArgumentException">When the <paramref name="param"/> is not <see cref="PinnedAppInfo"/> object or is null, <see cref="ArgumentException"/> is thrown</exception>

        internal void LaunchPinnedApp(object? param)
        {
            if (param is not PinnedAppInfo pinnedAppInfo)
            {
                LogWrongCommandParameter(nameof(PinnedAppInfo));
                throw new ArgumentException($"Command parameter must be {nameof(PinnedAppInfo)}", nameof(param));
            }

            pinnedAppInfo.LaunchPinnedApp(e =>
            {
                LogCantStartApp(pinnedAppInfo.PinnedAppType == PinnedAppTypeEnum.Package ? pinnedAppInfo.AppId ?? "[Null] appID" : pinnedAppInfo.LinkFile ?? "[Null] link file", e);
            });
        }

        /// <summary>
        /// Launches the installed application
        /// Parameter <paramref name="param"/> must be <see cref="InstalledApplication"/> object
        /// </summary>
        /// <param name="param"><see cref="InstalledApplication"/> object with reference to installed application</param>
        /// <exception cref="ArgumentException">When the <paramref name="param"/> is not <see cref="InstalledApplication"/> object or is null, <see cref="ArgumentException"/> is thrown</exception>

        internal void LaunchInstalledApp(object? param)
        {
            if (param is not InstalledApplication installedApplication)
            {
                LogWrongCommandParameter(nameof(InstalledApplication));
                throw new ArgumentException($"Command parameter must be {nameof(InstalledApplication)}", nameof(param));
            }

            installedApplication.LaunchInstalledApp(e =>
            {
                LogCantStartApp(installedApplication.ShellProperties.IsStoreApp ? installedApplication.AppUserModelId ?? "[Null] appID" : installedApplication.Executable ?? "[Null] file", e);
            });
        }

        /// <summary>
        /// Builds the context menu for application window button
        /// Parameter <paramref name="param"/> must be <see cref="BuildContextMenuCommandParams"/> object
        /// </summary>
        /// <param name="param"><see cref="BuildContextMenuCommandParams"/> object with reference to <see cref="AppButton"/> and <see cref="ButtonInfo"/></param>
        /// <exception cref="ArgumentException">When the <paramref name="param"/> is not <see cref="BuildContextMenuCommandParams"/> object or is null, <see cref="ArgumentException"/> is thrown</exception>
        private void BuildContextMenu(object? param)
        {
            if (param is not BuildContextMenuCommandParams cmdParams)
            {
                LogWrongCommandParameter(nameof(BuildContextMenuCommandParams));
                throw new ArgumentException($"Command parameter must be {nameof(BuildContextMenuCommandParams)}",
                    nameof(param));
            }

            MenuItem menuItem;
            var menu = new ContextMenu();

            var buttonInfo = cmdParams.ButtonInfo;
            WndInfo? wndInfo = null;
            if (buttonInfo is WndInfo wi)
            {
                wndInfo = wi;
            }

            var isWindow = wndInfo != null;

            var appId = buttonInfo.AppId;

            if (appId != null)
            {
                //appId can be an executable full path, ensure that known folders are transformed to their GUIDs
                appId = Shell.ReplaceKnownFolderWithGuid(appId);

                //JumpList into the context menu
                var jumplistItems = jumpListService.GetJumpListItems(appId!, InstalledApplications);
                if (jumplistItems.Length > 0)
                {
                    string? lastCategory = null;
                    string localizedTasks = languageService.Translate(TranslationKeys.JumpListCategoryTasks) ?? "Tasks";

                    foreach (var linkInfo in jumplistItems.Where(l => l.HasTarget)) //skip separators for UI simplicity
                    {
                        if (linkInfo.Category != lastCategory)
                        {
                            //category title
                            menuItem = new MenuItem
                            {
                                Header = linkInfo.Category,
                                IsEnabled = false
                            };
                            menu.Items.Add(menuItem);

                            lastCategory = linkInfo.Category;
                        }

                        //jumplist item
                        menuItem = new MenuItem
                        {
                            Header = linkInfo.Name
                        };

                        //caption anonymization
                        if (Settings.FeatureFlag<bool>(AppSettings.FF_AnonymizeWindows) && linkInfo.Category != localizedTasks)
                        {
                            menuItem.Header = Anonymize(linkInfo.Name, linkInfo.GetHashCode());
                        }

                        if (linkInfo.Icon != null)
                        {
                            menuItem.Icon = new Image
                            {
                                Source = InvertBitmapIfApplicable(linkInfo.Icon)
                            };
                        }
                        else
                        {
                            //use app icon
                            menuItem.Icon = new Image
                            {
                                Source = InvertBitmapIfApplicable(buttonInfo.BitmapSource)
                            };
                        }

                        menuItem.Click += (_, _) =>
                        {
                            try
                            {
                                if (!linkInfo.IsStoreApp)
                                {
                                    Process.Start(new ProcessStartInfo(linkInfo.TargetPath!)
                                    {
                                        Arguments = linkInfo.Arguments,
                                        WorkingDirectory = linkInfo.WorkingDirectory,
                                        UseShellExecute = true
                                    });
                                }
                                else
                                {
                                    Package.ActivateApplication(linkInfo.TargetPath, linkInfo.Arguments, out _);
                                }
                            }
                            catch (Exception ex)
                            {
                                LogCantStartApp(linkInfo.ToString(), ex);
                            }

                        };
                        menu.Items.Add(menuItem);
                    }

                    menu.Items.Add(new Separator());
                }
            }


            BuildContextMenuItemLaunchNewInstance(isWindow, buttonInfo, menu);

            if (isWindow)
            {
                //close window menu item
                menuItem = new MenuItem
                {
                    Header = languageService.Translate(TranslationKeys.JumpListMenuCloseWindow),
                    Icon = new SymbolIcon()
                    {
                        Symbol = SymbolRegular.DismissSquare20
                    }
                };
                menuItem.Click += (_, _) => { WndAndApp.CloseWindow(wndInfo!.Hwnd); };
                menu.Items.Add(menuItem);
            }

            menu.Items.Add(new Separator());

            //close menu (cancel) menu item
            menuItem = new MenuItem
            {
                Header = languageService.Translate(TranslationKeys.JumpListMenuCancel),
                Icon = new SymbolIcon()
                {
                    Symbol = SymbolRegular.ShareCloseTray20
                }
            };
            menuItem.Click += (_, _) =>
            {
                //do nothing, just close the context menu
            };
            menu.Items.Add(menuItem);

            cmdParams.Button.ContextMenu = menu;
        }

        /// <summary>
        /// Builds the context menu item for launching a new instance of application
        /// </summary>
        /// <param name="isWindow">Flag whether the context menu is for window button</param>
        /// <param name="buttonInfo">Information about application window or pinned app </param>
        /// <param name="menu">Context menu</param>
        private void BuildContextMenuItemLaunchNewInstance(bool isWindow, ButtonInfo buttonInfo, ContextMenu menu)
        {
            if (isWindow)
            {
                //Start new instance menu item (window)
                if (!File.Exists(buttonInfo.Executable)) return;

                var appName =
                    InstalledApplications.GetInstalledApplicationFromAppId(buttonInfo.AppId ?? string.Empty)?.Name ??
                    InstalledApplications.GetInstalledApplicationFromExecutable(buttonInfo.Executable)?.Name ??
                    FileVersionInfo.GetVersionInfo(buttonInfo.Executable).FileDescription ??
                    Path.GetFileName(buttonInfo.Executable);

                var menuItem = new MenuItem
                {
                    Header = appName,
                    Icon = new Image
                    {
                        Source = InvertBitmapIfApplicable(buttonInfo.BitmapSource)
                    }
                };
                menuItem.Click += (_, _) =>
                {
                    if (buttonInfo.Executable.ToLowerInvariant().EndsWith("\\explorer.exe"))
                    {
                        //explorer and the "special" folders like control panel
                        try
                        {
                            Process.Start(new ProcessStartInfo("explorer")
                            {
                                Arguments = buttonInfo.AppId != null
                                    ? $"shell:appsFolder\\{buttonInfo.AppId}"
                                    : null,
                            });
                        }
                        catch (Exception ex)
                        {
                            LogCantStartApp(appName, ex);
                        }
                    }
                    else
                    {
                        var started = false;
                        if (!buttonInfo.Executable.ToLowerInvariant().EndsWith("\\applicationframehost.exe"))
                        {
                            try
                            {
                                Process.Start(buttonInfo.Executable);
                                started = true;
                            }
                            catch (Exception ex)
                            {
                                LogCantStartApp(appName, ex);
                            }
                        }

                        if (started || buttonInfo.AppId == null) return;

                        //maybe store/UWP app
                        try
                        {
                            Package.ActivateApplication(buttonInfo.AppId, null, out _);
                        }
                        catch (Exception ex)
                        {
                            LogCantStartApp(appName, ex);
                        }
                    }
                };
                menu.Items.Add(menuItem);
            }
            else
            {
                //Start new instance menu item (pinned app)
                if (buttonInfo is not PinnedAppInfo pinnedAppInfo) return;
                var menuItem = new MenuItem
                {
                    Header = pinnedAppInfo.Title,
                    Icon = new Image
                    {
                        Source = InvertBitmapIfApplicable(pinnedAppInfo.BitmapSource)
                    }
                };
                menuItem.Click += (_, _) => { LaunchPinnedApp(pinnedAppInfo); };
                menu.Items.Add(menuItem);
            }
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
