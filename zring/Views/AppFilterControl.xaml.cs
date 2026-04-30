using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Zring.AppBar;
using Zring.ViewModel;

namespace Zring.Views
{
    /// <summary>
    /// Interaction logic for AppFilterControl.xaml
    /// </summary>
    // ReSharper disable once RedundantExtendsListEntry
    public partial class AppFilterControl : UserControl
    {
        /// <summary>
        /// Appbar dock mode — used to flip the popup placement.
        /// </summary>
        public AppBarDockMode DockMode
        {
            get => (AppBarDockMode)GetValue(DockModeProperty);
            set => SetValue(DockModeProperty, value);
        }

        /// <summary>
        /// Appbar dock mode dependency property
        /// </summary>
        public static readonly DependencyProperty DockModeProperty = DependencyProperty.Register(
            nameof(DockMode),
            typeof(AppBarDockMode),
            typeof(AppFilterControl),
            new FrameworkPropertyMetadata(AppBarDockMode.Bottom));

        /// <summary>
        /// Host window we subscribe to for outside-click and deactivation dismissal. Captured on Loaded.
        /// </summary>
        private Window? hostWindow;

        public AppFilterControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            hostWindow = Window.GetWindow(this);
            if (hostWindow != null)
            {
                hostWindow.PreviewMouseDown += OnHostPreviewMouseDown;
                hostWindow.Deactivated += OnHostDeactivated;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (hostWindow != null)
            {
                hostWindow.PreviewMouseDown -= OnHostPreviewMouseDown;
                hostWindow.Deactivated -= OnHostDeactivated;
                hostWindow = null;
            }
        }

        /// <summary>
        /// Refresh the RunningApps snapshot every time the popup opens.
        /// </summary>
        private void FilterPopup_OnOpened(object sender, EventArgs e)
        {
            if (DataContext is AppFilterViewModel vm)
            {
                vm.OnPopupOpened();
            }
        }

        /// <summary>
        /// Manual outside-click dismissal. We use StaysOpen=True instead of WPF's built-in
        /// StaysOpen=False auto-close to avoid a close-then-reopen race: with StaysOpen=False
        /// the popup's mouse-capture path closes the popup BEFORE the toggle's click handler
        /// runs, then the toggle flips IsChecked back to true and re-opens the popup the user
        /// just tried to close.
        ///
        /// Clicks inside the popup arrive on the popup's separate HWND and never reach this
        /// handler, so they don't need filtering. Clicks on the toggle button itself fall
        /// through so the ToggleButton's own click logic flips IsChecked → IsPopupOpen → close.
        /// Any other click on the host window closes the popup explicitly.
        /// </summary>
        private void OnHostPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!FilterPopup.IsOpen) return;

            if (e.OriginalSource is DependencyObject origin && IsDescendantOfToggle(origin)) return;

            if (DataContext is AppFilterViewModel vm) vm.IsPopupOpen = false;
        }

        /// <summary>
        /// Close the popup when the host window deactivates (user switched to another app).
        /// Mirrors the focus-loss dismissal we'd otherwise inherit from StaysOpen=False.
        /// </summary>
        private void OnHostDeactivated(object? sender, EventArgs e)
        {
            if (FilterPopup.IsOpen && DataContext is AppFilterViewModel vm) vm.IsPopupOpen = false;
        }

        private bool IsDescendantOfToggle(DependencyObject obj)
        {
            DependencyObject? current = obj;
            while (current != null)
            {
                if (ReferenceEquals(current, FilterToggle)) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }
    }
}
