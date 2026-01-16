using System.Windows;
using OptionSuite.Blotter.Wpf.ViewModels;

namespace OptionSuite.Blotter.Wpf.Views
{
    public partial class TradeEditWindow : Window
    {
        public TradeEditWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is TradeEditViewModel oldVm)
            {
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (e.NewValue is TradeEditViewModel newVm)
            {
                newVm.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        private void OnViewModelPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Close window when save completes successfully
            // The ViewModel calls closeAction which we need to handle
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