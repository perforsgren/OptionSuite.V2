using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OptionSuite.Blotter.Wpf.Converters
{
    /// <summary>
    /// Konverterar null eller tom sträng till Visibility.Visible, annars Visibility.Collapsed.
    /// Används för att visa innehåll när värdet är null (inverterad logik av NullToCollapsedConverter).
    /// </summary>
    public class NullToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Visibility.Visible;

            if (value is string str && string.IsNullOrEmpty(str))
                return Visibility.Visible;

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
