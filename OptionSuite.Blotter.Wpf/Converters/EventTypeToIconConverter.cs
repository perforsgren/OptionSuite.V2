using System;
using System.Globalization;
using System.Windows.Data;

namespace OptionSuite.Blotter.Wpf.Converters
{
    /// <summary>
    /// Mappar EventType till ikon.
    /// Hanterar booking, parsing, editing, errors, status updates m.m.
    /// </summary>
    public sealed class EventTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return "●";

            var eventType = value.ToString();

            // ========== BOOKING / EXPORT ==========
            if (eventType.Contains("Booking") && eventType.Contains("Request"))
                return "▶";  // Booking requested
            
            if (eventType.Contains("Export") && (eventType.Contains("Success") || eventType.Contains("Sent")))
                return "↗";  // Export sent/success
            
            if (eventType.Contains("Export") && eventType.Contains("Fail"))
                return "✕";  // Export failed



            // ========== PARSING / INGESTION ==========
            if (eventType == "MessageInReceived")
                return "↓";  // FIX message received

            if (eventType == "TradeNormalized")
                return "→";  // Trade normalized/routed

            if (eventType.Contains("Parsed") || eventType.Contains("Ingested"))
                return "⊕";  // Successfully parsed
            
            if (eventType.Contains("Received") || eventType.Contains("Incoming"))
                return "↓";  // Message received
            
            if (eventType.Contains("Parsing") && eventType.Contains("Fail"))
                return "⊗";  // Parsing failed
            
            if (eventType.Contains("Validation") && eventType.Contains("Fail"))
                return "⚠";  // Validation failed

            // ========== STATUS UPDATES ==========
            if (eventType.Contains("Confirmed") || eventType.Contains("Booked") || eventType.Contains("Success"))
                return "✓";  // Confirmed/Success
            
            if (eventType.Contains("Rejected") || eventType.Contains("Declined"))
                return "✕";  // Rejected
            
            if (eventType.Contains("Pending") || eventType.Contains("Awaiting"))
                return "◷";  // Pending/Waiting
            
            if (eventType.Contains("StatusUpdate") || eventType.Contains("Status") && eventType.Contains("Change"))
                return "⟳";  // Status updated

            // ========== EDITING / CORRECTIONS ==========
            if (eventType.Contains("Edit") || eventType.Contains("Modified"))
                return "✎";  // Edited
            
            if (eventType.Contains("Correction") || eventType.Contains("Amended"))
                return "⤺";  // Correction applied
            
            if (eventType.Contains("FieldUpdate"))
                return "≡";  // Field updated

            // ========== SYSTEM COMMUNICATION ==========
            if (eventType.Contains("SentTo") || eventType.Contains("Forwarded"))
                return "→";  // Sent to system
            
            if (eventType.Contains("AckReceived") || eventType.Contains("Acknowledged"))
                return "✓";  // Ack received
            
            if (eventType.Contains("AckFail"))
                return "✕";  // Ack failed

            // ========== ERRORS ==========
            if (eventType.Contains("Error") || eventType.Contains("Failed") || eventType.Contains("Fail"))
                return "⚠";  // Error/Failed
            
            if (eventType.Contains("Timeout"))
                return "⏱";  // Timeout
            
            if (eventType.Contains("Network"))
                return "⚡";  // Network issue

            // ========== CANCELLATION ==========
            if (eventType.Contains("Cancel"))
                return "⊘";  // Cancelled

            // ========== AUDIT / COMPLIANCE ==========
            if (eventType.Contains("Audit") || eventType.Contains("Logged"))
                return "📋";  // Audit log
            
            if (eventType.Contains("Compliance") && eventType.Contains("Pass"))
                return "✓";  // Compliance passed
            
            if (eventType.Contains("Compliance") && eventType.Contains("Fail"))
                return "⚠";  // Compliance failed

            // Default
            return "●";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
