using System;
using System.Windows;
using System.Windows.Input;
using OptionSuite.Blotter.Wpf.ViewModels;

namespace OptionSuite.Blotter.Wpf.Views
{
    public partial class TradeEditWindow : Window
    {
        public TradeEditWindow()
        {
            InitializeComponent();
            StateChanged += OnStateChanged;
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
        /// Minimize button click handler.
        /// </summary>
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// Maximize/Restore button click handler.
        /// </summary>
        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized 
                ? WindowState.Normal 
                : WindowState.Maximized;
        }

        /// <summary>
        /// Close button click handler.
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Update maximize icon when window state changes.
        /// </summary>
        private void OnStateChanged(object sender, EventArgs e)
        {
            // Could update the maximize icon here if needed
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