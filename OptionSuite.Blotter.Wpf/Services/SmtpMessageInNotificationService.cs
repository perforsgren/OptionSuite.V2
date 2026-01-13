using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using FxTradeHub.Domain.Entities;

namespace FxTradeHub.Services.Notifications
{
    /// <summary>
    /// SMTP-baserad notification service för MessageIn parsing.
    /// Skickar email vid fel (och optionellt vid success).
    /// </summary>
    public sealed class SmtpMessageInNotificationService : IMessageInNotificationService
    {
        private readonly MessageInNotificationSettings _settings;

        public SmtpMessageInNotificationService(MessageInNotificationSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            if (string.IsNullOrWhiteSpace(_settings.SmtpHost))
                throw new ArgumentException("SmtpHost måste vara angivet.", nameof(settings));

            if (string.IsNullOrWhiteSpace(_settings.FromAddress))
                throw new ArgumentException("FromAddress måste vara angivet.", nameof(settings));

            if (_settings.ToAddresses == null || _settings.ToAddresses.Length == 0)
                throw new ArgumentException("Minst en ToAddress måste vara angiven.", nameof(settings));
        }

        public void NotifyMessageInSuccess(MessageIn entity)
        {
            if (!_settings.SendOnSuccess || entity == null)
                return;

            try
            {
                var venue = entity.SourceVenueCode ?? "?";
                var sourceKey = entity.SourceMessageKey ?? "(no key)";

                var subject = $"[STP][OK] {venue} {sourceKey}";

                var body = new StringBuilder();
                body.AppendLine("MessageIn – SUCCESS");
                body.AppendLine();
                body.AppendLine($"Venue:            {venue}");
                body.AppendLine($"SourceKey:        {sourceKey}");
                body.AppendLine($"SourceType:       {entity.SourceType ?? "(null)"}");
                body.AppendLine($"Counterparty:     {entity.ExternalCounterpartyName ?? "(null)"}");
                body.AppendLine($"InstrumentCode:   {entity.InstrumentCode ?? "(null)"}");
                body.AppendLine($"Side:             {entity.Side ?? "(null)"}");
                body.AppendLine($"Notional:         {entity.Notional?.ToString("N2", CultureInfo.InvariantCulture) ?? "(null)"}");
                body.AppendLine($"NotionalCcy:      {entity.NotionalCurrency ?? "(null)"}");
                body.AppendLine($"TradeDate:        {(entity.TradeDate.HasValue ? entity.TradeDate.Value.ToString("yyyy-MM-dd") : "(null)")}");
                body.AppendLine($"ReceivedUtc:      {entity.ReceivedUtc:yyyy-MM-dd HH:mm:ss}");
                body.AppendLine();
                body.AppendLine($"RawPayloadHash:   {entity.RawPayloadHash ?? "(null)"}");

                SendMail(subject, body.ToString());
            }
            catch
            {
                // Notifiering får aldrig slå ut STP-flödet
            }
        }


        public void NotifyMessageInFailure(
            string venueCode,
            string messageType,
            string sourceMessageKey,
            string fileName,
            string errorMessage,
            string rawPayload)
        {
            if (!_settings.SendOnFailure)
                return;

            try
            {
                var venue = string.IsNullOrWhiteSpace(venueCode) ? "?" : venueCode;
                var key = string.IsNullOrWhiteSpace(sourceMessageKey) ? "(no key)" : sourceMessageKey;
                var file = string.IsNullOrWhiteSpace(fileName) ? "(no file)" : fileName;

                var subject = $"[STP][ERROR] {venue} {messageType} {key}";

                var body = new StringBuilder();
                body.AppendLine("MessageIn – FAILURE");
                body.AppendLine();
                body.AppendLine($"Venue:            {venue}");
                body.AppendLine($"MessageType:      {messageType ?? "?"}");
                body.AppendLine($"SourceKey:        {key}");
                body.AppendLine($"FileName:         {file}");
                body.AppendLine();
                body.AppendLine("Error:");
                body.AppendLine(errorMessage ?? "(null)");
                body.AppendLine();
                body.AppendLine("Raw Payload:");
                body.AppendLine(TruncatePayload(rawPayload, 5000));

                SendMail(subject, body.ToString());
            }
            catch
            {
                // Notifiering får aldrig slå ut STP-flödet
            }
        }

        private string TruncatePayload(string payload, int maxLength)
        {
            if (string.IsNullOrEmpty(payload))
                return "(null)";

            if (payload.Length <= maxLength)
                return payload;

            return payload.Substring(0, maxLength) + "\n\n[... truncated ...]";
        }

        private void SendMail(string subject, string body)
        {
            try
            {
                using (var message = new MailMessage())
                {
                    message.From = new MailAddress(_settings.FromAddress);

                    foreach (var addr in _settings.ToAddresses.Where(a => !string.IsNullOrWhiteSpace(a)))
                        message.To.Add(new MailAddress(addr.Trim()));

                    message.Subject = subject ?? string.Empty;
                    message.Body = body ?? string.Empty;
                    message.IsBodyHtml = false;

                    using (var client = new SmtpClient(_settings.SmtpHost, _settings.SmtpPort))
                    {
                        client.EnableSsl = _settings.EnableSsl;

                        if (!string.IsNullOrWhiteSpace(_settings.SmtpUser))
                        {
                            client.Credentials = new NetworkCredential(
                                _settings.SmtpUser,
                                _settings.SmtpPassword ?? string.Empty);
                        }

                        client.Send(message);
                    }
                }
            }
            catch
            {
                // Tyst felhantering - email får aldrig slå ut STP
            }
        }
    }

    /// <summary>
    /// Settings för MessageIn email notifications.
    /// </summary>
    public sealed class MessageInNotificationSettings
    {
        public string SmtpHost { get; set; }
        public int SmtpPort { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string SmtpUser { get; set; }
        public string SmtpPassword { get; set; }
        public string FromAddress { get; set; }
        public string[] ToAddresses { get; set; }
        public bool SendOnSuccess { get; set; } = false;
        public bool SendOnFailure { get; set; } = true;
    }
}
