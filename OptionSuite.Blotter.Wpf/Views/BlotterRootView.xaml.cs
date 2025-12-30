using OptionSuite.Blotter.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OptionSuite.Blotter.Wpf.Views
{
    public partial class BlotterRootView : UserControl
    {
        public BlotterRootView()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            HookGrid(OptionGrid);
            HookGrid(LinearGrid);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UnhookGrid(OptionGrid);
            UnhookGrid(LinearGrid);
        }

        private void HookGrid(DataGrid grid)
        {
            if (grid == null) return;

            grid.PreviewMouseWheel += OnAnyUserInteraction_MouseWheel;
            grid.PreviewKeyDown += OnAnyUserInteraction_KeyDown;

            // Edit-gate
            grid.BeginningEdit += OnAnyUserInteraction_BeginningEdit;
            grid.CellEditEnding += OnAnyUserInteraction_CellEditEnding;
        }

        private void UnhookGrid(DataGrid grid)
        {
            if (grid == null) return;

            grid.PreviewMouseWheel -= OnAnyUserInteraction_MouseWheel;
            grid.PreviewKeyDown -= OnAnyUserInteraction_KeyDown;

            grid.BeginningEdit -= OnAnyUserInteraction_BeginningEdit;
            grid.CellEditEnding -= OnAnyUserInteraction_CellEditEnding;
        }

        private void PulseUserInteraction()
        {
            // - bool IsUserInteracting
            // - void NotifyUserInteraction()
            var vm = DataContext as BlotterRootViewModel;
            vm?.NotifyUserInteraction();
        }

        private void OnAnyUserInteraction_MouseWheel(object sender, MouseWheelEventArgs e) => PulseUserInteraction();
        private void OnAnyUserInteraction_KeyDown(object sender, KeyEventArgs e) => PulseUserInteraction();
        private void OnAnyUserInteraction_BeginningEdit(object sender, DataGridBeginningEditEventArgs e) => PulseUserInteraction();
        private void OnAnyUserInteraction_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) => PulseUserInteraction();
    }
}
