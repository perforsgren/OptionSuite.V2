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
                var fileNameOnly = Path.GetFileName(anyFileName);

                // NYTT FORMAT: {StpTradeId}_{TradeId}.xml_evs_ans_ok...
                // Exempel: 127_3070345-L1.xml_evs_ans_ok.dtd_37933607_2.xml

                // Första delen före _ är StpTradeId
                var parts = fileNameOnly.Split('_');
                if (parts.Length < 2)
                {
                    throw new InvalidOperationException($"Invalid response filename format: {fileNameOnly}");
                }

                if (!long.TryParse(parts[0], out var stpTradeId))
                {
                    throw new InvalidOperationException($"Cannot parse StpTradeId from filename: {fileNameOnly}");
                }

                // Hitta original filename: "{StpTradeId}_{TradeId}.xml"
                var xmlIndex = fileNameOnly.IndexOf(".xml_");
                if (xmlIndex <= 0)
                {
                    throw new InvalidOperationException($"Cannot find .xml marker in filename: {fileNameOnly}");
                }

                var originalFileName = fileNameOnly.Substring(0, xmlIndex + 4); // +4 för ".xml"

                // Hitta alla 3 filer
                var basePattern = $"{originalFileName}_*";
                var allFiles = Directory.GetFiles(responseFolder, basePattern);

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
                        result.Mx3ContractId = successXml.Descendants("contractId").FirstOrDefault()?.Value;
                        result.Mx3TradeId = successXml.Descendants("tradeInternalId").FirstOrDefault()?.Value;
                    }
                }

                // Parse error-fil
                if (!string.IsNullOrEmpty(errorFile) && File.Exists(errorFile))
                {
                    var errorXml = XDocument.Load(errorFile);

                    // ✅ Ta ALLA errors (Warning, Abort, Fatal, etc.)
                    var exceptions = errorXml.Descendants("MXException")
                        .Select(e => new
                        {
                            Level = e.Element("Level")?.Value,
                            Code = e.Element("Code")?.Value,
                            Description = e.Element("Description")?.Value,
                            Module = e.Element("Module")?.Value
                        })
                        .Where(e => !string.IsNullOrEmpty(e.Description))
                        .Where(e => e.Level == "Abort" || e.Level == "Warning" || e.Level == "Fatal")  // ✅ Inkludera Abort
                        .ToList();

                    if (exceptions.Any())
                    {
                        // Hitta mest specifika error (ofta den sista)
                        var mainError = exceptions
                            .Where(e => e.Code != "Checkpoint")  // Skip generic wrapper errors
                            .LastOrDefault() ?? exceptions.Last();

                        result.ErrorMessage = $"[{mainError.Code}] {mainError.Description}";

                        // Om det finns modul-info, lägg till det
                        if (!string.IsNullOrEmpty(mainError.Module) && mainError.Module != "MXSI")
                        {
                            result.ErrorMessage += $" (Module: {mainError.Module})";
                        }
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
