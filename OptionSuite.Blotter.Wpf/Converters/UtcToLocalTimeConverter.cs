using System;
using System.Globalization;
using System.Windows.Data;

namespace OptionSuite.Blotter.Wpf.Converters
{
    /// <summary>
    /// Konverterar UTC DateTime till lokal tid för UI-visning.
    /// </summary>
    public class UtcToLocalTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dt && dt.Kind == DateTimeKind.Utc)
            {
                return dt.ToLocalTime();
            }

            if (value is DateTime dtUnspecified)
            {
                // Anta UTC om Kind är Unspecified
                return DateTime.SpecifyKind(dtUnspecified, DateTimeKind.Utc).ToLocalTime();
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
