using OptionSuite.Blotter.Wpf.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace OptionSuite.Blotter.Wpf.Views
{
    /// <summary>
    /// Root-view för Blotter-modulen.
    /// I Fas 2 hålls denna extremt tunn – all logik ligger i ViewModel.
    /// </summary>
    public partial class BlotterRootView : UserControl
    {
        /// <summary>
        /// Skapar root-viewn och initierar XAML.
        /// </summary>
        public BlotterRootView()
        {
            InitializeComponent();
        }

        private void OptionsGrid_GotFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is BlotterRootViewModel vm)
            {
                vm.OnOptionGridGotFocus();
            }
        }

        private void LinearGrid_GotFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is BlotterRootViewModel vm)
            {
                vm.OnLinearGridGotFocus();
            }
        }

    }


}
