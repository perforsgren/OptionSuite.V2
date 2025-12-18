using System.Windows;
using OptionSuite.Blotter.Wpf.ViewModels;

namespace OptionSuite.Blotter.Host.Wpf
{
    /// <summary>
    /// Minimal host för Blotter i Fas 2.
    /// Visar Root VM via DataTemplate (Generic.xaml).
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new BlotterRootViewModel();
        }
    }
}
