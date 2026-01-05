using System;
using System.Globalization;
using System.Windows.Data;

namespace OptionSuite.Blotter.Wpf.Converters
{
    /// <summary>
    /// Konverterar "PENDING" → "Pending", "NEW" → "New", etc.
    /// Helt säker mot alla null/empty/exception-fall.
    /// </summary>
    public class ProperCaseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                // Null-säkerhet
                if (value == null)
                    return "Unknown";

                // Tom sträng
                var str = value.ToString();
                if (string.IsNullOrWhiteSpace(str))
                    return "Unknown";

                // Trim whitespace
                str = str.Trim();

                // Special cases (exakt match)
                switch (str)
                {
                    case "NEW": return "New";
                    case "PENDING": return "Pending";
                    case "BOOKED": return "Booked";
                    case "ERROR": return "Error";
                    case "REJECTED": return "Rejected";
                    case "PARTIAL": return "Partial";
                    case "CANCELLED": return "Cancelled";
                }

                // Default: First letter uppercase, rest lowercase
                if (str.Length == 1)
                    return str.ToUpper();

                return char.ToUpper(str[0]) + str.Substring(1).ToLower();
            }
            catch
            {
                // Failsafe - returnera originalvärdet eller fallback
                return value?.ToString() ?? "Unknown";
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("ProperCaseConverter does not support ConvertBack");
        }
    }
}
