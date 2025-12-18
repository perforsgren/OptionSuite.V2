using System;
using System.Globalization;
using System.Windows.Data;

namespace OptionSuite.Shell.Wpf.Infrastructure
{
    public sealed class ModuleSelectedToTagConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return null;

            if (!(values[0] is ShellModuleId current))
                return null;

            if (!(values[1] is ShellModuleId selected))
                return null;

            return (current == selected) ? "Selected" : null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
