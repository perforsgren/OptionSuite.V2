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

        private BlotterRootViewModel ViewModel => DataContext as BlotterRootViewModel;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            HookGrid(OptionGrid);
            HookGrid(LinearGrid);

            // Subscribe to FocusSearchRequested event from ViewModel
            if (ViewModel != null)
            {
                ViewModel.FocusSearchRequested += OnFocusSearchRequested;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UnhookGrid(OptionGrid);
            UnhookGrid(LinearGrid);

            // Unsubscribe from FocusSearchRequested event
            if (ViewModel != null)
            {
                ViewModel.FocusSearchRequested -= OnFocusSearchRequested;
            }
        }

        /// <summary>
        /// Focuses the search TextBox when requested by the ViewModel.
        /// </summary>
        private void OnFocusSearchRequested(object sender, System.EventArgs e)
        {
            SearchTextBox?.Focus();
            SearchTextBox?.SelectAll();
        }

        private void HookGrid(DataGrid grid)
        {
            if (grid == null) return;

            grid.PreviewMouseWheel += OnAnyUserInteraction_MouseWheel;
            grid.PreviewKeyDown += OnAnyUserInteraction_KeyDown;
            grid.BeginningEdit += OnGrid_BeginningEdit;
            grid.CellEditEnding += OnGrid_CellEditEnding;
            grid.MouseDoubleClick += OnGrid_MouseDoubleClick;
        }

        private void UnhookGrid(DataGrid grid)
        {
            if (grid == null) return;

            grid.PreviewMouseWheel -= OnAnyUserInteraction_MouseWheel;
            grid.PreviewKeyDown -= OnAnyUserInteraction_KeyDown;
            grid.BeginningEdit -= OnGrid_BeginningEdit;
            grid.CellEditEnding -= OnGrid_CellEditEnding;
            grid.MouseDoubleClick -= OnGrid_MouseDoubleClick;
        }

        private void PulseUserInteraction()
        {
            ViewModel?.NotifyUserInteraction();
        }

        private void OnAnyUserInteraction_MouseWheel(object sender, MouseWheelEventArgs e) => PulseUserInteraction();
        private void OnAnyUserInteraction_KeyDown(object sender, KeyEventArgs e) => PulseUserInteraction();

        /// <summary>
        /// Hanterar dubbelklick på rad - öppnar Edit/View-fönster.
        /// </summary>
        private void OnGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid)
                return;

            // Kontrollera att vi klickade på en rad (inte header eller tom yta)
            var originalSource = e.OriginalSource as DependencyObject;
            var row = FindVisualParent<DataGridRow>(originalSource);

            if (row?.Item is TradeRowViewModel trade)
            {
                PulseUserInteraction();
                ViewModel?.OpenTradeEditWindow(trade);
            }
        }

        /// <summary>
        /// Hittar förälder av angiven typ i visual tree.
        /// </summary>
        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                    return parent;

                child = System.Windows.Media.VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void OnGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            PulseUserInteraction();

            if (e.Row?.Item is TradeRowViewModel row)
            {
                row.BeginEdit();
            }
        }

        private void OnGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            PulseUserInteraction();

            if (e.Row?.Item is not TradeRowViewModel row)
                return;

            if (e.EditAction == DataGridEditAction.Cancel)
            {
                row.CancelEdit();
                return;
            }

            var columnHeader = e.Column?.Header?.ToString();
            if (!string.IsNullOrEmpty(columnHeader))
            {
                _ = ViewModel?.OnCellEditEndingAsync(row, columnHeader);
            }
        }

        /// <summary>
        /// Hanterar ComboBox SelectionChanged för inline-edit celler.
        /// VIKTIGT: Ignorerar ändringar som kommer från databinding (refresh).
        /// </summary>
        private void OnPortfolioComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ignorera om inga nya items valdes (händer vid clear)
            if (e.AddedItems.Count == 0)
                return;

            // Ignorera om ComboBox inte har keyboard/mouse focus (= programmatisk ändring)
            var comboBox = sender as ComboBox;
            if (comboBox == null || !comboBox.IsDropDownOpen)
                return;

            var row = comboBox.DataContext as TradeRowViewModel;
            if (row == null || ViewModel == null)
                return;

            // Kolla om värdet faktiskt ändrades
            var newValue = e.AddedItems[0]?.ToString();
            var oldValue = e.RemovedItems.Count > 0 ? e.RemovedItems[0]?.ToString() : null;

            if (string.Equals(newValue, oldValue, StringComparison.Ordinal))
                return;

            // Pulsa interaktion för att pausa polling
            PulseUserInteraction();

            // Spara direkt till DB
            _ = ViewModel.OnCellEditEndingAsync(row, "Portfolio MX3");
        }

        /// <summary>
        /// Hanterar DropDownOpened för att markera raden som "editing" och pausa polling.
        /// </summary>
        private void OnPortfolioComboBox_DropDownOpened(object sender, System.EventArgs e)
        {
            var comboBox = sender as ComboBox;
            var row = comboBox?.DataContext as TradeRowViewModel;

            if (row != null)
            {
                row.BeginEdit();
            }

            PulseUserInteraction();
        }

        /// <summary>
        /// Hanterar DropDownClosed för att avsluta edit-läge.
        /// </summary>
        private void OnPortfolioComboBox_DropDownClosed(object sender, System.EventArgs e)
        {
            var comboBox = sender as ComboBox;
            var row = comboBox?.DataContext as TradeRowViewModel;

            if (row != null)
            {
                row.EndEdit();
            }
        }

        private void OnCalypsoComboBox_DropDownOpened(object sender, System.EventArgs e)
        {
            var comboBox = sender as ComboBox;
            var row = comboBox?.DataContext as TradeRowViewModel;

            if (row != null)
            {
                row.BeginEdit();
            }

            PulseUserInteraction();
        }

        private void OnCalypsoComboBox_DropDownClosed(object sender, System.EventArgs e)
        {
            var comboBox = sender as ComboBox;
            var row = comboBox?.DataContext as TradeRowViewModel;

            if (row == null || ViewModel == null)
                return;

            if (row.HasEditChanges())
            {
                _ = ViewModel.OnCellEditEndingAsync(row, "Book Calypso");
            }
            else
            {
                row.CancelEdit();
            }
        }

        private void OnCalypsoComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as ComboBox;

            if (comboBox?.IsDropDownOpen == true)
            {
                PulseUserInteraction();
            }
        }

        /// <summary>
        /// Rensar sökfältet.
        /// </summary>
        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.SearchText = string.Empty;
            }
            SearchTextBox?.Focus();
        }

        /// <summary>
        /// Handles Escape key in search box to clear the search text.
        /// </summary>
        private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (!string.IsNullOrEmpty(SearchTextBox.Text))
                {
                    // Clear the search text
                    if (ViewModel != null)
                    {
                        ViewModel.SearchText = string.Empty;
                    }
                    e.Handled = true;
                }
            }
        }
    }
}