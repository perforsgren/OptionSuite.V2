using System;
using System.Globalization;
using System.Windows;
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
            // ✅ FIX: Hantera null explicit
            if (value == null)
                return DependencyProperty.UnsetValue;

            if (value is DateTime dt)
            {
                // Om redan UTC, konvertera till lokal tid
                if (dt.Kind == DateTimeKind.Utc)
                {
                    return dt.ToLocalTime();
                }

                // Om Kind är Unspecified, anta UTC och konvertera
                if (dt.Kind == DateTimeKind.Unspecified)
                {
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime();
                }

                // Om redan lokal tid, returnera som den är
                return dt;
            }

            // Om värdet inte är en DateTime, returnera UnsetValue
            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("UtcToLocalTimeConverter does not support ConvertBack.");
        }
    }
}
