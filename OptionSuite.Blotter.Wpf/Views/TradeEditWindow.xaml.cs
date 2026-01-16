using System.Windows;
using OptionSuite.Blotter.Wpf.ViewModels;

namespace OptionSuite.Blotter.Wpf.Views
{
    public partial class TradeEditWindow : Window
    {
        public TradeEditWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Cancel button click handler - closes the window with DialogResult = false.
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Called from ViewModel to close the window with a result.
        /// </summary>
        public void CloseWithResult(bool success)
        {
            DialogResult = success;
            Close();
        }
    }
}