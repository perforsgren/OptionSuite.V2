using OptionSuite.Blotter.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace OptionSuite.Blotter.Wpf.Views
{
    public partial class BlotterRootView : UserControl
    {
        public BlotterRootView()
        {
            InitializeComponent();
        }

        private void OptionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not BlotterRootViewModel vm) return;

            // När en option trade selectas, cleara om det var en linear trade
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TradeRowViewModel selectedTrade)
            {
                // Sätt den nya selectionen direkt
                vm.SelectedTrade = selectedTrade;
            }
            else if (e.RemovedItems.Count > 0 && e.AddedItems.Count == 0)
            {
                // Användaren deselekterade manuellt
                vm.SelectedTrade = null;
            }
        }

        private void LinearGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not BlotterRootViewModel vm) return;

            // När en linear trade selectas, cleara om det var en option trade
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TradeRowViewModel selectedTrade)
            {
                // Sätt den nya selectionen direkt
                vm.SelectedTrade = selectedTrade;
            }
            else if (e.RemovedItems.Count > 0 && e.AddedItems.Count == 0)
            {
                // Användaren deselekterade manuellt
                vm.SelectedTrade = null;
            }
        }
    }
}
