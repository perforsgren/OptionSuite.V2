using System;
using System.Windows;
using OptionSuite.Blotter.Wpf.ViewModels;

namespace OptionSuite.Blotter.Host.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly BlotterRootViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();

            _vm = BlotterCompositionRoot.CreateRootViewModel();
            DataContext = _vm;

            Loaded += async (s, e) =>
            {
                await _vm.InitialLoadAsync().ConfigureAwait(true);

                // Starta polling efter första loaden
                _vm.StartPolling(TimeSpan.FromSeconds(2));
            };

            Closed += (s, e) =>
            {
                _vm.StopPolling();
            };
        }
    }
}
