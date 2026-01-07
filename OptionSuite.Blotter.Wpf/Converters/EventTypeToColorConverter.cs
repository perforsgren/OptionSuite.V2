using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace OptionSuite.Blotter.Wpf.Converters
{
    /// <summary>
    /// Mappar EventType till färg baserat på semantisk betydelse.
    /// </summary>
    public sealed class EventTypeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

            var eventType = value.ToString();

            // ========== SUCCESS / CONFIRMED (Teal/Green) ==========
            if (eventType.Contains("Success") ||
                eventType.Contains("Confirmed") ||
                eventType.Contains("Booked") ||
                eventType.Contains("Parsed") ||
                eventType.Contains("AckReceived") ||
                eventType.Contains("Acknowledged") ||
                eventType.Contains("TradeNormalized") ||
                (eventType.Contains("Compliance") && eventType.Contains("Pass")))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2DD4BF")); // Teal

            // ========== PENDING / IN PROGRESS (Yellow/Amber) ==========
            if (eventType.Contains("Pending") ||
                eventType.Contains("Awaiting") ||
                eventType.Contains("Request") ||
                eventType.Contains("Sent") ||
                eventType.Contains("Forwarded") ||
                eventType.Contains("Ingested") ||
                eventType.Contains("Received"))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24")); // Yellow

            // ========== ERRORS / FAILED (Red) ==========
            if (eventType.Contains("Error") ||
                eventType.Contains("Failed") ||
                eventType.Contains("Fail") ||
                eventType.Contains("Rejected") ||
                eventType.Contains("Declined") ||
                eventType.Contains("Timeout") ||
                (eventType.Contains("Compliance") && eventType.Contains("Fail")))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")); // Red

            // ========== CANCELLED (Orange) ==========
            if (eventType.Contains("Cancel"))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FB923C")); // Orange

            // ========== EDITING / MODIFICATIONS (Blue) ==========
            if (eventType.Contains("Edit") ||
                eventType.Contains("Modified") ||
                eventType.Contains("Correction") ||
                eventType.Contains("Amended") ||
                eventType.Contains("FieldUpdate"))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6")); // Blue

            // ========== STATUS UPDATES / INFO (Purple) ==========
            if (eventType.Contains("StatusUpdate") ||
                eventType.Contains("Status") && eventType.Contains("Change"))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A855F7")); // Purple

            // ========== AUDIT / COMPLIANCE (Gray) ==========
            if (eventType.Contains("Audit") ||
                eventType.Contains("Logged"))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")); // Gray

            // Default (Muted gray)
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
