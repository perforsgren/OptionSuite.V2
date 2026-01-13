using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OptionSuite.Blotter.Wpf.Services
{
    /// <summary>
    /// Resultat från Calypso response parsing.
    /// </summary>
    public sealed class CalypsoResponseResult
    {
        // ✅ NYTT: Samma som MX3
        public long StpTradeId { get; set; }

        public string CalypsoTradeId { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
    }

    public static class CalypsoResponseParserService
    {
        public static CalypsoResponseResult Parse(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);

                // ✅ Parse StpTradeId från filnamn (NYTT: samma som MX3)
                var stpTradeId = ExtractStpTradeIdFromFileName(fileName);

                if (stpTradeId == 0)
                {
                    throw new InvalidOperationException($"Cannot extract StpTradeId from filename: {fileName}");
                }

                // Läs XML
                var xml = XDocument.Load(filePath);
                var root = xml.Root;

                if (root == null || root.Name.LocalName != "CalypsoAcknowledgement")
                {
                    throw new InvalidOperationException($"Invalid Calypso XML root element: {fileName}");
                }

                var result = new CalypsoResponseResult
                {
                    StpTradeId = stpTradeId  // ✅ NYTT
                };

                // Check rejected count
                var rejectedAttr = root.Attribute("Rejected");
                var rejected = rejectedAttr != null ? int.Parse(rejectedAttr.Value) : 0;

                if (rejected > 0)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = ParseErrorMessage(root);
                    return result;
                }

                // Parse success från CalypsoTrades
                var calypsoTrades = root.Element("CalypsoTrades");
                if (calypsoTrades == null)
                {
                    throw new InvalidOperationException($"Missing CalypsoTrades element: {fileName}");
                }

                var firstTrade = calypsoTrades.Elements("CalypsoTrade").FirstOrDefault();
                if (firstTrade == null)
                {
                    throw new InvalidOperationException($"No CalypsoTrade elements found: {fileName}");
                }

                var statusElement = firstTrade.Element("Status");
                var status = statusElement?.Value ?? "";

                result.IsSuccess = status.Equals("Success", StringComparison.OrdinalIgnoreCase);
                result.CalypsoTradeId = firstTrade.Element("CalypsoTradeId")?.Value ?? "";

                return result;
            }
            catch (Exception ex)
            {
                return new CalypsoResponseResult
                {
                    StpTradeId = 0,
                    IsSuccess = false,
                    ErrorMessage = $"Parse error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Extraherar StpTradeId från filnamn.
        /// Format: FX_SPOT_580_3966887408_result.xml → StpTradeId = 580
        /// </summary>
        private static long ExtractStpTradeIdFromFileName(string fileName)
        {
            try
            {
                // Ta bort file extension
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                // "FX_SPOT_580_3966887408_result"

                // Ta bort "_result" suffix
                if (nameWithoutExt.EndsWith("_result", StringComparison.OrdinalIgnoreCase))
                {
                    nameWithoutExt = nameWithoutExt.Substring(0, nameWithoutExt.Length - 7);
                }
                // "FX_SPOT_580_3966887408"

                var parts = nameWithoutExt.Split('_');
                if (parts.Length < 3)
                    return 0;

                // Format: FX_SPOT_580_3966887408 → parts[2] = "580"
                if (parts[0].Equals("FX", StringComparison.OrdinalIgnoreCase) &&
                    (parts[1].Equals("SPOT", StringComparison.OrdinalIgnoreCase) ||
                     parts[1].Equals("FORWARD", StringComparison.OrdinalIgnoreCase)))
                {
                    if (long.TryParse(parts[2], out var stpTradeId))
                        return stpTradeId;
                }

                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string ParseErrorMessage(XElement root)
        {
            try
            {
                var calypsoErrors = root.Element("CalypsoErrors");
                if (calypsoErrors == null)
                    return "Unknown error";

                var errorMessages = calypsoErrors.Elements("CalypsoError")
                    .SelectMany(ce => ce.Elements("Error"))
                    .Select(e => e.Element("Message")?.Value)
                    .Where(m => !string.IsNullOrEmpty(m))
                    .ToList();

                return errorMessages.Any()
                    ? string.Join("; ", errorMessages)
                    : "Rejected by Calypso";
            }
            catch
            {
                return "Error parsing error message";
            }
        }
    }
}