using System;
using System.Globalization;
using System.Windows.Data;

namespace OptionSuite.Blotter.Wpf.Converters
{
    /// <summary>
    /// Konverterar "PENDING" → "Pending", "NEW" → "New", etc.
    /// </summary>
    public sealed class ProperCaseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrEmpty(value.ToString()))
                return value;

            var str = value.ToString();

            // Special cases
            if (str == "NEW") return "New";
            if (str == "PENDING") return "Pending";
            if (str == "BOOKED") return "Booked";
            if (str == "ERROR") return "Error";
            if (str == "REJECTED") return "Rejected";
            if (str == "PARTIAL") return "Partial";

            // Default: First letter uppercase, rest lowercase
            return char.ToUpper(str[0]) + str.Substring(1).ToLower();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
