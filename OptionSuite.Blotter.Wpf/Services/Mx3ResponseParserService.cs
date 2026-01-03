using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OptionSuite.Blotter.Wpf.Services
{
    /// <summary>
    /// Resultat från MX3 response parsing.
    /// </summary>
    public sealed class Mx3ResponseResult
    {
        public long StpTradeId { get; set; }
        public string Mx3TradeId { get; set; }
        public string Mx3ContractId { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Parsar MX3 response-filer (3 filer per trade).
    /// </summary>
    public static class Mx3ResponseParserService
    {
        /// <summary>
        /// Parsar MX3 response från filnamn och *_2.xml (success) eller *_3.xml (error).
        /// Filformat: 3070342-L1.xml_evs_ans_ok.dtd_37933598_2.xml
        /// </summary>
        public static Mx3ResponseResult Parse(string responseFolder, string anyFileName)
        {
            try
            {
                // Extrahera StpTradeId från filnamn
                // Format: "{StpTradeId}-L1.xml_*_{Mx3Id}_{FileNumber}.xml"
                var fileNameOnly = Path.GetFileName(anyFileName);
                var parts = fileNameOnly.Split('-', '_');

                if (parts.Length < 2 || !long.TryParse(parts[0], out var stpTradeId))
                {
                    throw new InvalidOperationException($"Cannot parse StpTradeId from filename: {fileNameOnly}");
                }

                // Hitta alla 3 filer för denna trade
                var basePattern = $"{stpTradeId}-L1.xml_";
                var allFiles = Directory.GetFiles(responseFolder, basePattern + "*");

                // Hitta success-fil (*_2.xml) och error-fil (*_3.xml)
                var successFile = allFiles.FirstOrDefault(f => f.Contains("_evs_ans_ok.") && f.EndsWith("_2.xml"));
                var errorFile = allFiles.FirstOrDefault(f => f.Contains("_evs_ans_err.") && f.EndsWith("_3.xml"));

                var result = new Mx3ResponseResult
                {
                    StpTradeId = stpTradeId
                };

                // Parse success-fil
                if (!string.IsNullOrEmpty(successFile) && File.Exists(successFile))
                {
                    var successXml = XDocument.Load(successFile);
                    var answerStatus = successXml.Root?.Attribute("MXAnswerStatus")?.Value;

                    if (answerStatus == "OK")
                    {
                        result.IsSuccess = true;

                        // Extrahera MX3 IDs
                        var ns = successXml.Root?.Name.Namespace ?? XNamespace.None;
                        result.Mx3ContractId = successXml.Descendants("contractId").FirstOrDefault()?.Value;
                        result.Mx3TradeId = successXml.Descendants("tradeInternalId").FirstOrDefault()?.Value;
                    }
                }

                // Parse error-fil (warnings kan finnas även om OK)
                if (!string.IsNullOrEmpty(errorFile) && File.Exists(errorFile))
                {
                    var errorXml = XDocument.Load(errorFile);
                    var exceptions = errorXml.Descendants("MXException")
                        .Where(e => e.Element("Level")?.Value == "Warning")
                        .Select(e => e.Element("Description")?.Value)
                        .Where(d => !string.IsNullOrEmpty(d))
                        .ToList();

                    if (exceptions.Any())
                    {
                        result.ErrorMessage = string.Join("; ", exceptions);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                return new Mx3ResponseResult
                {
                    StpTradeId = 0,
                    IsSuccess = false,
                    ErrorMessage = $"Parse error: {ex.Message}"
                };
            }
        }
    }
}
