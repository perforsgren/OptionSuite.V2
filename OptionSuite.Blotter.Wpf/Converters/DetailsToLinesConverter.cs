using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace OptionSuite.Blotter.Wpf.Converters
{
    /// <summary>
    /// Parsar "Key: Value, Key: Value" format till array av rader.
    /// </summary>
    public sealed class DetailsToLinesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return new string[0];

            var details = value.ToString();

            // Split på ", " och lägg till bullet för varje rad
            return details
                .Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => "  • " + s.Trim())
                .ToArray();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
