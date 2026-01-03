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
                // Format: "{StpTradeId}-{suffix}.xml_*_{Mx3Id}_{FileNumber}.xml"
                var fileNameOnly = Path.GetFileName(anyFileName);

                // Hitta första '-' för att få StpTradeId
                var dashIndex = fileNameOnly.IndexOf('-');
                if (dashIndex <= 0)
                {
                    throw new InvalidOperationException($"Cannot parse StpTradeId from filename: {fileNameOnly}");
                }

                var stpTradeIdStr = fileNameOnly.Substring(0, dashIndex);
                if (!long.TryParse(stpTradeIdStr, out var stpTradeId))
                {
                    throw new InvalidOperationException($"Invalid StpTradeId in filename: {fileNameOnly}");
                }

                // Hitta original filnamn (allt innan första '_')
                // 3070342-L1.xml_evs_ans_ok... → 3070342-L1.xml
                var underscoreIndex = fileNameOnly.IndexOf('_');
                if (underscoreIndex <= 0)
                {
                    throw new InvalidOperationException($"Invalid response filename format: {fileNameOnly}");
                }

                var originalFileName = fileNameOnly.Substring(0, underscoreIndex);

                // Hitta alla 3 filer för denna trade med samma original filename
                var basePattern = $"{originalFileName}_*";
                var allFiles = Directory.GetFiles(responseFolder, basePattern);

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
