using System;
using System.Globalization;
using System.Windows.Data;

namespace OptionSuite.Blotter.Wpf.Converters
{
    /// <summary>
    /// Konverterar null → true, not-null → false.
    /// </summary>
    public class NullToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Behandla både null och tomma strängar som false
            if (value == null) return false;
            if (value is string str && string.IsNullOrWhiteSpace(str)) return false;
            return true;
        }


        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
