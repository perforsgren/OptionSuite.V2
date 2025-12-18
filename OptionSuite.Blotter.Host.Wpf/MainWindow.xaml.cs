using System.Windows;
using OptionSuite.Blotter.Wpf.ViewModels;

namespace OptionSuite.Blotter.Host.Wpf
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var vm = BlotterCompositionRoot.CreateRootViewModel();
            DataContext = vm;

            Loaded += async (s, e) =>
            {
                await vm.InitialLoadAsync();
            };
        }
    }
}
