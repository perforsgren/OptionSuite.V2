using System;
using System.Globalization;
using System.Windows.Data;
using OptionSuite.Shell.Wpf.Views;

namespace OptionSuite.Shell.Wpf.Infrastructure
{
    public sealed class ModuleIdToViewConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is ShellModuleId id))
                return null;

            // Fas 1: placeholder per modul (sen byter vi till riktiga root views)
            switch (id)
            {
                case ShellModuleId.Blotter:
                case ShellModuleId.Pricing:
                case ShellModuleId.GammaHedger:
                case ShellModuleId.VolatilityManager:
                default:
                    return new ModulePlaceholderView();
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
