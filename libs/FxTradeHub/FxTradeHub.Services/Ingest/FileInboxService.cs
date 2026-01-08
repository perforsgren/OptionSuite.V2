using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Services;

namespace FxTradeHub.Services.Ingest
{
    /// <summary>
    /// Läser email-filer (txt/json från Power Automate) och skapar MessageIn-poster
    /// via IMessageInService. Hanterar file parsing och metadata extraction.
    /// </summary>
    public class FileInboxService
    {
        private readonly IMessageInService _messageInService;

        /// <summary>
        /// Skapar en ny instans av FileInboxService.
        /// </summary>
        /// <param name="messageInService">Service för att persistera MessageIn.</param>
        public FileInboxService(IMessageInService messageInService)
        {
            _messageInService = messageInService ?? throw new ArgumentNullException(nameof(messageInService));
        }

        /// <summary>
        /// Bearbetar en email-fil från Power Automate och skapar MessageIn-post.
        /// Läser From/Subject/Body från filinnehåll och skriver till trade_stp.MessageIn via IMessageInService.
        /// Returnerar MessageInId om lyckad insert, annars -1.
        /// </summary>
        /// <param name="filePath">Full path till email-fil (txt-format från Power Automate).</param>
        /// <returns>MessageInId om lyckad insert, annars -1.</returns>
        public long ProcessEmailFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"[FileInboxService] File does not exist: {filePath}");
                return -1;
            }

            try
            {
                var fileContent = File.ReadAllText(filePath, Encoding.UTF8);
                //System.Diagnostics.Debug.WriteLine($"[FileInboxService] Read {fileContent.Length} chars from file");

                var email = ParseEmailFile(fileContent);

                if (email == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[FileInboxService] ParseEmailFile returned null");
                    return -1;
                }

                if (string.IsNullOrWhiteSpace(email.Body))
                {
                    System.Diagnostics.Debug.WriteLine($"[FileInboxService] Email body is empty");
                    return -1;
                }

                //System.Diagnostics.Debug.WriteLine($"[FileInboxService] Parsed email: From={email.From}, Subject={email.Subject}, BodyLength={email.Body.Length}");

                var payloadHash = ComputeSha256Hash(email.Body);

                var messageIn = new MessageIn
                {
                    SourceType = "EMAIL",  
                    SourceVenueCode = DetermineVenueCode(email.From, email.Subject),
                    ReceivedUtc = email.ReceivedUtc,
                    SourceTimestamp = email.ReceivedUtc,
                    RawPayload = email.Body,
                    EmailSubject = email.Subject,
                    EmailFrom = email.From,
                    EmailTo = email.To,
                    RawPayloadHash = payloadHash
                };

                //System.Diagnostics.Debug.WriteLine($"[FileInboxService] SourceVenueCode={messageIn.SourceVenueCode}");

                var messageInId = _messageInService.InsertMessage(messageIn);

                System.Diagnostics.Debug.WriteLine($"[FileInboxService] Created MessageInId={messageInId}");

                return messageInId;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileInboxService] Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[FileInboxService] StackTrace: {ex.StackTrace}");
                return -1;
            }
        }


        /// <summary>
        /// Parsar email-fil från Power Automate (txt-format).
        /// Förväntat format:
        /// From: sender@example.com
        /// Subject: Trade confirmation...
        /// Received: 2026-01-08T10:54:39+00:00
        /// 
        /// ---BODY---
        /// [HTML body content]
        /// </summary>
        private EmailFileContent ParseEmailFile(string fileContent)
        {
            if (string.IsNullOrWhiteSpace(fileContent))
                return null;

            var lines = fileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.None);

            string from = null;
            string subject = null;
            DateTime receivedUtc = DateTime.UtcNow;
            string to = null;

            int bodyStartIndex = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (line.StartsWith("From:", StringComparison.OrdinalIgnoreCase))
                {
                    from = line.Substring(5).Trim();
                }
                else if (line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))
                {
                    subject = line.Substring(8).Trim();
                }
                else if (line.StartsWith("Received:", StringComparison.OrdinalIgnoreCase))
                {
                    var receivedStr = line.Substring(9).Trim();
                    if (DateTime.TryParse(receivedStr, out var parsedDate))
                    {
                        receivedUtc = parsedDate.ToUniversalTime();
                    }
                }
                else if (line.StartsWith("To:", StringComparison.OrdinalIgnoreCase))
                {
                    to = line.Substring(3).Trim();
                }
                else if (line.Contains("---BODY---"))
                {
                    bodyStartIndex = i + 1;
                    break;
                }
            }

            string body = null;
            if (bodyStartIndex >= 0 && bodyStartIndex < lines.Length)
            {
                var bodyLines = new string[lines.Length - bodyStartIndex];
                Array.Copy(lines, bodyStartIndex, bodyLines, 0, bodyLines.Length);
                body = string.Join(Environment.NewLine, bodyLines);
            }

            return new EmailFileContent
            {
                From = from,
                Subject = subject,
                ReceivedUtc = receivedUtc,
                To = to,
                Body = body
            };
        }

        /// <summary>
        /// Avgör SourceVenueCode baserat på From/Subject.
        /// Används för att identifiera broker i counterpartynamepattern-lookup.
        /// </summary>
        private string DetermineVenueCode(string from, string subject)
        {
            if (string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(subject))
                return "UNKNOWN";

            var fromLower = (from ?? string.Empty).ToLowerInvariant();
            var subjectLower = (subject ?? string.Empty).ToLowerInvariant();

            if (fromLower.Contains("jpmorgan") || fromLower.Contains("jpm") ||
                subjectLower.Contains("jpm trade"))
            {
                return "JPM";
            }

            return "UNKNOWN";
        }

        /// <summary>
        /// Beräknar SHA-256 hash av råpayload för deduplication.
        /// </summary>
        private string ComputeSha256Hash(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hashBytes = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        /// <summary>
        /// Internal helper class för parsed email content.
        /// </summary>
        private class EmailFileContent
        {
            public string From { get; set; }
            public string Subject { get; set; }
            public DateTime ReceivedUtc { get; set; }
            public string To { get; set; }
            public string Body { get; set; }
        }
    }
}
