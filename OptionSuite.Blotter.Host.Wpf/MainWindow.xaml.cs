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
                _vm.StartPolling(TimeSpan.FromSeconds(2));
            };

            // Pause/resume när window minimeras/återställs
            StateChanged += (s, e) =>
            {
                switch (WindowState)
                {
                    case WindowState.Minimized:
                        _vm.StopPolling();
                        break;
                    case WindowState.Normal:
                    case WindowState.Maximized:
                        _vm.StartPolling(TimeSpan.FromSeconds(2));
                        break;
                }
            };

            Closed += (s, e) =>
            {
                _vm.StopPolling();
                _vm.Dispose();
            };
        }

    }
}
