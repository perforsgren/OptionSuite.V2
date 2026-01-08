using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OptionSuite.Blotter.Wpf.Converters
{
    /// <summary>
    /// Mappar SystemLink status eller pipeline-status till färg.
    /// Används för workflow chips i Summary-tab.
    /// </summary>
    public sealed class SystemStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF)); // Teal (default)

            var status = value.ToString().ToUpperInvariant();

            // ========== SUCCESS (Green) ==========
            if (status == "BOOKED" ||
                status == "INGESTED" ||
                status == "NORMALIZED" ||
                status.StartsWith("AUDIT:"))
            {
                return new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // Green
            }

            // ========== WARNING (Orange/Yellow) ==========
            if (status == "PENDING" ||
                status == "ERROR" ||
                status == "REJECTED" ||
                status.Contains("PENDING"))
            {
                return new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); // Orange
            }

            // ========== NEW (Blue) ==========
            if (status == "NEW" || status.Contains(": NEW"))
            {
                return new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)); // Blue
            }

            // ========== NEUTRAL (Teal) ==========
            return new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF)); // Teal
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}