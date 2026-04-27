using System.Windows;
using System.Windows.Controls;
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

        public AppFilterControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Refresh the RunningApps snapshot every time the popup opens.
        /// </summary>
        private void FilterPopup_OnOpened(object sender, System.EventArgs e)
        {
            if (DataContext is AppFilterViewModel vm)
            {
                vm.OnPopupOpened();
            }
        }
    }
}
