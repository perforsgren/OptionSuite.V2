// File: libs/FxTradeHub/FxTradeHub.Domain/Parsing/TullettOptionConfirmationParser.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Enums;
using FxTradeHub.Domain.Interfaces;

namespace FxTradeHub.Domain.Parsing
{
    /// <summary>
    /// Parser för Tullett Prebon (TP ICAP) option confirmations via email.
    /// Hanterar vanilla options med eventuell hedge (spot/forward).
    /// </summary>
    public sealed class TullettOptionConfirmationParser : IInboundMessageParser
    {
        private const string SWEDBANK_LEI = "M312WZV08Y7LYUC71685";
        private const string SWEDBANK_NAME = "SWEDBANK AB (PUBL), STO";

        private readonly IStpLookupRepository _lookupRepository;

        public TullettOptionConfirmationParser(IStpLookupRepository lookupRepository)
        {
            _lookupRepository = lookupRepository ?? throw new ArgumentNullException(nameof(lookupRepository));
        }

        public bool CanParse(MessageIn message)
        {
            if (message == null)
                return false;

            if (!string.Equals(message.SourceType, "EMAIL", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(message.SourceVenueCode, "TULLETT", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        public ParseResult Parse(MessageIn message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.RawPayload))
            {
                return ParseResult.Failed("Message or RawPayload is empty");
            }

            try
            {
                var body = message.RawPayload;

                // Strip HTML if the payload is HTML formatted
                if (body.Contains("<html") || body.Contains("<table") || body.Contains("<span"))
                {
                    body = StripHtml(body);
                    Debug.WriteLine("HTML detected and stripped");
                }

                Debug.WriteLine("=== TullettOptionConfirmationParser DEBUG ===");
                Debug.WriteLine($"Body length after processing: {body.Length}");
                Debug.WriteLine($"First 500 chars: {body.Substring(0, Math.Min(500, body.Length))}");

                var headerInfo = ParseHeaderInfo(body);
                if (headerInfo == null)
                {
                    return ParseResult.Failed("Failed to parse header information");
                }

                Debug.WriteLine($"Header parsed - Trader: {headerInfo.TraderName}, TradeDate: {headerInfo.TradeDate}");

                var options = ParseOptions(body, headerInfo);
                if (options == null || options.Count == 0)
                {
                    return ParseResult.Failed("No options found in confirmation");
                }

                Debug.WriteLine($"Options found: {options.Count}");

                var hedge = ParseHedge(body, headerInfo);

                var traderRouting = _lookupRepository.GetTraderRoutingInfo("TULLETT", headerInfo.TraderName);
                if (traderRouting == null)
                {
                    return ParseResult.Failed($"Trader routing not found for TULLETT trader: {headerInfo.TraderName}");
                }

                var results = new List<ParsedTradeResult>();

                int optionLegNumber = 1;
                foreach (var optionData in options)
                {
                    var optionTrade = BuildOptionTrade(optionData, headerInfo, traderRouting, message, optionLegNumber);
                    if (optionTrade != null)
                    {
                        results.Add(optionTrade);
                        optionLegNumber++;
                    }
                }

                int hedgeLegNumber = 1;
                if (hedge != null)
                {
                    var hedgeTrade = BuildHedgeTrade(hedge, headerInfo, traderRouting, message, hedgeLegNumber);
                    if (hedgeTrade != null)
                    {
                        results.Add(hedgeTrade);
                        hedgeLegNumber++;
                    }
                }

                if (results.Count == 0)
                {
                    return ParseResult.Failed("Failed to build trades from parsed data");
                }

                return ParseResult.Ok(results);
            }
            catch (Exception ex)
            {
                return ParseResult.Failed($"Parse exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Strips HTML tags and decodes HTML entities to get plain text.
        /// </summary>
        private string StripHtml(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            // Remove style and script blocks completely
            var result = Regex.Replace(html, @"<style[^>]*>.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<script[^>]*>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // Replace <br>, <p>, <div>, <tr> tags with newlines
            result = Regex.Replace(result, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</p>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</div>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</tr>", "\n", RegexOptions.IgnoreCase);

            // Replace table cells with tab (to preserve column structure)
            result = Regex.Replace(result, @"</td>", "\t", RegexOptions.IgnoreCase);

            // Remove all remaining HTML tags
            result = Regex.Replace(result, @"<[^>]+>", "", RegexOptions.Singleline);

            // Decode HTML entities
            result = DecodeHtmlEntities(result);

            // Normalize whitespace - collapse multiple spaces/tabs but preserve newlines
            result = Regex.Replace(result, @"[ \t]+", " ");
            result = Regex.Replace(result, @"(\r?\n\s*)+", "\n");

            // Trim lines
            var lines = result.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Decodes common HTML entities to their character equivalents.
        /// </summary>
        private string DecodeHtmlEntities(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Common HTML entities
            text = text.Replace("&nbsp;", " ");
            text = text.Replace("&amp;", "&");
            text = text.Replace("&lt;", "<");
            text = text.Replace("&gt;", ">");
            text = text.Replace("&quot;", "\"");
            text = text.Replace("&apos;", "'");
            text = text.Replace("&#39;", "'");

            // Numeric entities (basic support)
            text = Regex.Replace(text, @"&#(\d+);", m =>
            {
                if (int.TryParse(m.Groups[1].Value, out int code) && code < 65536)
                {
                    return ((char)code).ToString();
                }
                return m.Value;
            });

            return text;
        }

        #region Header Parsing

        private class HeaderInfo
        {
            public string TraderName { get; set; }
            public string BrokerName { get; set; }
            public DateTime TradeDate { get; set; }
            public DateTime ExecutionTimeUtc { get; set; }
            public string BrokerTradeReference { get; set; }
            public string RTN { get; set; }
            public string Strategy { get; set; }
            public string Mic { get; set; }
            public string UtiNamespace { get; set; }  // Added
        }

        private HeaderInfo ParseHeaderInfo(string body)
        {
            var info = new HeaderInfo { Mic = "TPIR" };

            var traderMatch = Regex.Match(body, @"Trader\s*:\s*(.+?)(?:\n|\r)", RegexOptions.IgnoreCase);
            if (traderMatch.Success)
            {
                info.TraderName = traderMatch.Groups[1].Value.Trim();
            }

            var brokerMatch = Regex.Match(body, @"Broker\s*:\s*(.+?)(?:\n|\r)", RegexOptions.IgnoreCase);
            if (brokerMatch.Success)
            {
                info.BrokerName = brokerMatch.Groups[1].Value.Trim();
            }

            var tradeDateMatch = Regex.Match(body, @"Trade Date\s*:\s*(\d{1,2}-[A-Za-z]{3}-\d{4})", RegexOptions.IgnoreCase);
            if (tradeDateMatch.Success)
            {
                if (DateTime.TryParseExact(tradeDateMatch.Groups[1].Value, "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tradeDate))
                {
                    info.TradeDate = tradeDate;
                }
            }

            var executionTimeMatch = Regex.Match(body, @"Execution Time\s*:\s*(\d{1,2}:\d{2}:\d{2})", RegexOptions.IgnoreCase);
            if (executionTimeMatch.Success)
            {
                if (TimeSpan.TryParse(executionTimeMatch.Groups[1].Value, out var time))
                {
                    info.ExecutionTimeUtc = info.TradeDate.Add(time);
                }
            }

            var brokerRefMatch = Regex.Match(body, @"Broker Trade Reference\s*:\s*(.+?)(?:\n|\r)", RegexOptions.IgnoreCase);
            if (brokerRefMatch.Success)
            {
                info.BrokerTradeReference = brokerRefMatch.Groups[1].Value.Trim();
            }

            var rtnMatch = Regex.Match(body, @"RTN\s*:\s*(\d+)", RegexOptions.IgnoreCase);
            if (rtnMatch.Success)
            {
                info.RTN = rtnMatch.Groups[1].Value.Trim();
            }

            var strategyMatch = Regex.Match(body, @"Strategy \d+\s*:\s*(.+?)(?:\n|\r)", RegexOptions.IgnoreCase);
            if (strategyMatch.Success)
            {
                info.Strategy = strategyMatch.Groups[1].Value.Trim();
            }

            // Parse UTI Namespace: "UTI Namespace : 213800R54EFFINMY1P02"
            var utiNamespaceMatch = Regex.Match(body, @"UTI Namespace\s*:\s*([A-Z0-9]+)", RegexOptions.IgnoreCase);
            if (utiNamespaceMatch.Success)
            {
                info.UtiNamespace = utiNamespaceMatch.Groups[1].Value.Trim();
            }

            return info;
        }

        #endregion

        #region Option Parsing

        private class OptionData
        {
            public string UTI { get; set; }
            public string ISIN { get; set; }
            public string Style { get; set; }
            public DateTime ExpiryDate { get; set; }
            public DateTime DeliveryDate { get; set; }
            public string CallOn { get; set; }
            public string PutOn { get; set; }
            public decimal Strike { get; set; }
            public decimal SpotRate { get; set; }
            public decimal? Volatility { get; set; }
            public decimal? SwapPoints { get; set; }
            public decimal Premium { get; set; }
            public string PremiumCurrency { get; set; }  // Added
            public DateTime PremiumDate { get; set; }
            public string SellerName { get; set; }
            public string SellerLEI { get; set; }
            public string BuyerName { get; set; }
            public string BuyerLEI { get; set; }
        }

        private List<OptionData> ParseOptions(string body, HeaderInfo headerInfo)
        {
            var options = new List<OptionData>();

            Debug.WriteLine("=== ParseOptions DEBUG (after HTML strip) ===");
            Debug.WriteLine($"Body length: {body.Length}");

            // Find all "Option N" positions
            var optionRegex = new Regex(@"Option\s+(\d+)", RegexOptions.IgnoreCase);
            var optionMatches = optionRegex.Matches(body);

            Debug.WriteLine($"Found {optionMatches.Count} option markers");

            foreach (Match optMatch in optionMatches)
            {
                var optIndex = optMatch.Index;
                var optNum = optMatch.Groups[1].Value;

                Debug.WriteLine($"Processing Option {optNum} at index {optIndex}");

                // Find the seller/buyer block before this option
                // Search backwards from Option N to find Seller/Buyer info
                var searchStart = Math.Max(0, optIndex - 1000);
                var headerSection = body.Substring(searchStart, optIndex - searchStart);

                // Find the LAST occurrence of Seller in the header section (closest to Option N)
                var sellerMatch = Regex.Match(headerSection, @"Seller\s*:\s*([^\n]+)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
                var sellerLeiMatch = Regex.Match(headerSection, @"Seller\s+LEI\s*:\s*([A-Z0-9]{18,22})", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
                var buyerMatch = Regex.Match(headerSection, @"Buyer\s*:\s*([^\n]+)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
                var buyerLeiMatch = Regex.Match(headerSection, @"Buyer\s+LEI\s*:\s*([A-Z0-9]{18,22})", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);

                Debug.WriteLine($"Seller: {sellerMatch.Success} = '{(sellerMatch.Success ? sellerMatch.Groups[1].Value.Trim() : "N/A")}'");
                Debug.WriteLine($"Buyer: {buyerMatch.Success} = '{(buyerMatch.Success ? buyerMatch.Groups[1].Value.Trim() : "N/A")}'");

                // Find end of this option's content
                var nextOptionMatch = optionRegex.Match(body, optIndex + 10);
                var nextBoundary = nextOptionMatch.Success ? nextOptionMatch.Index : body.Length;

                // Check for other section boundaries
                var hedgeIdx = body.IndexOf("Confirmation of Hedge", optIndex + 10, StringComparison.OrdinalIgnoreCase);
                if (hedgeIdx > 0 && hedgeIdx < nextBoundary) nextBoundary = hedgeIdx;

                var brokerageIdx = body.IndexOf("BROKERAGE", optIndex + 10, StringComparison.OrdinalIgnoreCase);
                if (brokerageIdx > 0 && brokerageIdx < nextBoundary) nextBoundary = brokerageIdx;

                // Also look for next Seller block (for Option 2's seller info)
                var nextSellerIdx = body.IndexOf("Seller", optIndex + 10, StringComparison.OrdinalIgnoreCase);
                if (nextSellerIdx > 0 && nextSellerIdx < nextBoundary) nextBoundary = nextSellerIdx;

                // Extract option content
                var optionContent = body.Substring(optIndex, nextBoundary - optIndex);

                Debug.WriteLine($"Option content length: {optionContent.Length}");
                Debug.WriteLine($"Option content first 300: {optionContent.Substring(0, Math.Min(300, optionContent.Length))}");

                if (sellerMatch.Success && buyerMatch.Success)
                {
                    var option = ParseSingleOption(optionContent);
                    if (option != null)
                    {
                        option.SellerName = sellerMatch.Groups[1].Value.Trim();
                        option.SellerLEI = sellerLeiMatch.Success ? sellerLeiMatch.Groups[1].Value.Trim() : null;
                        option.BuyerName = buyerMatch.Groups[1].Value.Trim();
                        option.BuyerLEI = buyerLeiMatch.Success ? buyerLeiMatch.Groups[1].Value.Trim() : null;
                        options.Add(option);
                        Debug.WriteLine($"Option {optNum} added - UTI: {option.UTI}, Seller: {option.SellerName}, Buyer: {option.BuyerName}");
                    }
                }
                else
                {
                    Debug.WriteLine($"Could not find seller/buyer for Option {optNum}");
                }
            }

            Debug.WriteLine($"Total options parsed: {options.Count}");
            return options;
        }

        private OptionData ParseSingleOption(string optionSection)
        {
            var option = new OptionData();

            var utiMatch = Regex.Match(optionSection, @"UTI\s*:\s*([A-Z0-9]+)", RegexOptions.IgnoreCase);
            if (utiMatch.Success)
            {
                option.UTI = utiMatch.Groups[1].Value.Trim();
            }

            var isinMatch = Regex.Match(optionSection, @"ISIN\s*:\s*([A-Z0-9]+)", RegexOptions.IgnoreCase);
            if (isinMatch.Success)
            {
                option.ISIN = isinMatch.Groups[1].Value.Trim();
            }

            var styleMatch = Regex.Match(optionSection, @"Style\s*:\s*(.+?)(?:\n|\r)", RegexOptions.IgnoreCase);
            if (styleMatch.Success)
            {
                option.Style = styleMatch.Groups[1].Value.Trim();
            }

            var expiryMatch = Regex.Match(optionSection, @"Expiry details\s*:\s*(\d{1,2}\s+[A-Za-z]{3}\s+\d{4})", RegexOptions.IgnoreCase);
            if (expiryMatch.Success)
            {
                if (DateTime.TryParseExact(expiryMatch.Groups[1].Value, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
                {
                    option.ExpiryDate = expiry;
                }
            }

            var deliveryMatch = Regex.Match(optionSection, @"Delivery date\s*:\s*(\d{1,2}\s+[A-Za-z]{3}\s+\d{4})", RegexOptions.IgnoreCase);
            if (deliveryMatch.Success)
            {
                if (DateTime.TryParseExact(deliveryMatch.Groups[1].Value, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var delivery))
                {
                    option.DeliveryDate = delivery;
                }
            }

            var callOnMatch = Regex.Match(optionSection, @"Call on\s*:\s*(.+?)(?:\n|\r)", RegexOptions.IgnoreCase);
            if (callOnMatch.Success)
            {
                option.CallOn = callOnMatch.Groups[1].Value.Trim();
            }

            var putOnMatch = Regex.Match(optionSection, @"Put on\s*:\s*(.+?)(?:\n|\r)", RegexOptions.IgnoreCase);
            if (putOnMatch.Success)
            {
                option.PutOn = putOnMatch.Groups[1].Value.Trim();
            }

            var strikeMatch = Regex.Match(optionSection, @"Strike price\s*:\s*([\d,.]+)", RegexOptions.IgnoreCase);
            if (strikeMatch.Success)
            {
                if (decimal.TryParse(strikeMatch.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var strike))
                {
                    option.Strike = strike;
                }
            }

            var spotMatch = Regex.Match(optionSection, @"Spot Rate\s*:\s*([\d,.]+)", RegexOptions.IgnoreCase);
            if (spotMatch.Success)
            {
                if (decimal.TryParse(spotMatch.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var spot))
                {
                    option.SpotRate = spot;
                }
            }

            var volMatch = Regex.Match(optionSection, @"Volatility\s*:\s*([\d,.]+)%?", RegexOptions.IgnoreCase);
            if (volMatch.Success)
            {
                if (decimal.TryParse(volMatch.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var vol))
                {
                    option.Volatility = vol;
                }
            }

            var swapMatch = Regex.Match(optionSection, @"Swap Points\s*:\s*(-?[\d,.]+)", RegexOptions.IgnoreCase);
            if (swapMatch.Success)
            {
                if (decimal.TryParse(swapMatch.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var swap))
                {
                    option.SwapPoints = swap;
                }
            }

            // Parse Premium amount with currency: "Premium amount : USD 781,500.00"
            var premiumMatch = Regex.Match(optionSection, @"Premium amount\s*:\s*([A-Z]{3})\s*([\d,.]+)", RegexOptions.IgnoreCase);
            if (premiumMatch.Success)
            {
                option.PremiumCurrency = premiumMatch.Groups[1].Value.Trim();
                if (decimal.TryParse(premiumMatch.Groups[2].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var premium))
                {
                    option.Premium = premium;
                }
            }

            var premiumDateMatch = Regex.Match(optionSection, @"Premium value date\s*:\s*(\d{1,2}\s+[A-Za-z]{3}\s+\d{4})", RegexOptions.IgnoreCase);
            if (premiumDateMatch.Success)
            {
                if (DateTime.TryParseExact(premiumDateMatch.Groups[1].Value, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var premDate))
                {
                    option.PremiumDate = premDate;
                }
            }

            return option;
        }

        #endregion

        #region Hedge Parsing

        private class HedgeData
        {
            public string UTI { get; set; }
            public string ISIN { get; set; }
            public string SoldCurrency { get; set; }
            public decimal SoldAmount { get; set; }
            public string BoughtCurrency { get; set; }
            public decimal BoughtAmount { get; set; }
            public string CounterpartyName { get; set; }
            public decimal HedgeRate { get; set; }
            public DateTime ValueDate { get; set; }
        }

        private HedgeData ParseHedge(string body, HeaderInfo headerInfo)
        {
            var hedgeMatch = Regex.Match(body, @"Confirmation of Hedge Details\s*\n(.*?)(?=BROKERAGE|Thanks and Regards|$)",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (!hedgeMatch.Success)
                return null;

            var hedgeSection = hedgeMatch.Groups[1].Value;
            var hedge = new HedgeData();

            var utiMatch = Regex.Match(hedgeSection, @"UTI\s*:\s*([A-Z0-9]+)", RegexOptions.IgnoreCase);
            if (utiMatch.Success)
            {
                hedge.UTI = utiMatch.Groups[1].Value.Trim();
            }

            var isinMatch = Regex.Match(hedgeSection, @"ISIN\s*:\s*([A-Z0-9]+)", RegexOptions.IgnoreCase);
            if (isinMatch.Success)
            {
                hedge.ISIN = isinMatch.Groups[1].Value.Trim();
            }

            var soldMatch = Regex.Match(hedgeSection, @"You have sold\s*:\s*([A-Z]{3})\s*([\d,.]+)", RegexOptions.IgnoreCase);
            if (soldMatch.Success)
            {
                hedge.SoldCurrency = soldMatch.Groups[1].Value.Trim();
                if (decimal.TryParse(soldMatch.Groups[2].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var soldAmt))
                {
                    hedge.SoldAmount = soldAmt;
                }
            }

            var boughtMatch = Regex.Match(hedgeSection, @"You have bought\s*:\s*([A-Z]{3})\s*([\d,.]+)", RegexOptions.IgnoreCase);
            if (boughtMatch.Success)
            {
                hedge.BoughtCurrency = boughtMatch.Groups[1].Value.Trim();
                if (decimal.TryParse(boughtMatch.Groups[2].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var boughtAmt))
                {
                    hedge.BoughtAmount = boughtAmt;
                }
            }

            var cpMatch = Regex.Match(hedgeSection, @"Counterparty\s*:\s*(.+?)(?:\n|\r)", RegexOptions.IgnoreCase);
            if (cpMatch.Success)
            {
                hedge.CounterpartyName = cpMatch.Groups[1].Value.Trim();
            }

            var rateMatch = Regex.Match(hedgeSection, @"Hedge rate\s*:\s*([\d,.]+)", RegexOptions.IgnoreCase);
            if (rateMatch.Success)
            {
                if (decimal.TryParse(rateMatch.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
                {
                    hedge.HedgeRate = rate;
                }
            }

            var valueDateMatch = Regex.Match(hedgeSection, @"Value date\s*:\s*(\d{1,2}\s+[A-Za-z]{3}\s+\d{4})", RegexOptions.IgnoreCase);
            if (valueDateMatch.Success)
            {
                if (DateTime.TryParseExact(valueDateMatch.Groups[1].Value, "dd MMM yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var valueDate))
                {
                    hedge.ValueDate = valueDate;
                }
            }

            return hedge;
        }

        #endregion

        #region Trade Building

        private ParsedTradeResult BuildOptionTrade(OptionData option, HeaderInfo headerInfo, TraderRoutingInfo traderRouting, MessageIn message, int legNumber)
        {
            bool isBuyer = string.Equals(option.BuyerLEI, SWEDBANK_LEI, StringComparison.OrdinalIgnoreCase) ||
                           (string.IsNullOrEmpty(option.BuyerLEI) && (option.BuyerName?.IndexOf("SWEDBANK", StringComparison.OrdinalIgnoreCase) >= 0));

            bool isSeller = string.Equals(option.SellerLEI, SWEDBANK_LEI, StringComparison.OrdinalIgnoreCase) ||
                            (string.IsNullOrEmpty(option.SellerLEI) && (option.SellerName?.IndexOf("SWEDBANK", StringComparison.OrdinalIgnoreCase) >= 0));

            if (!isBuyer && !isSeller)
            {
                return null;
            }

            var (ccyPair, callPut, notional, notionalCcy, _) = DetermineOptionDetails(option, isBuyer);

            if (string.IsNullOrEmpty(ccyPair))
            {
                return null;
            }

            // Use premium currency from parsed option data
            var premiumCcy = option.PremiumCurrency;

            string counterpartyCode = isBuyer
                ? ResolveCounterpartyFromName(option.SellerName)
                : ResolveCounterpartyFromName(option.BuyerName);

            if (string.IsNullOrEmpty(counterpartyCode))
            {
                counterpartyCode = "TULLETT";
            }

            var portfolioMx3 = _lookupRepository.GetPortfolioCode("MX3", ccyPair, "OPTION_VANILLA");
            if (string.IsNullOrWhiteSpace(portfolioMx3))
            {
                portfolioMx3 = "FXO_CORP";
            }

            var calypsoBook = _lookupRepository.GetCalypsoBookByTraderId(traderRouting.InternalUserId);

            // Generate trade ID from broker reference
            var tradeId = GenerateTradeId(headerInfo.BrokerTradeReference, "O", legNumber);

            // TVTIC = leg-specific UTI from the option (e.g., "20260114045000000000000005388809")
            var tvtic = option.UTI;

            // UTI = UTI Namespace + leg UTI (e.g., "213800R54EFFINMY1P02" + "20260114045000000000000005388809")
            var uti = BuildFullUti(headerInfo.UtiNamespace, option.UTI);

            var trade = new Trade
            {
                TradeId = tradeId,
                ProductType = ProductType.OptionVanilla,
                SourceType = "EMAIL",
                SourceVenueCode = "TULLETT",
                MessageInId = message.MessageInId,
                CounterpartyCode = counterpartyCode,
                BrokerCode = "TULLETT",
                TraderId = traderRouting.InternalUserId,
                InvId = traderRouting.InvId,
                ReportingEntityId = traderRouting.ReportingEntityId,
                CurrencyPair = ccyPair,
                Mic = headerInfo.Mic ?? "TPIR",
                Isin = option.ISIN,
                TradeDate = headerInfo.TradeDate,
                ExecutionTimeUtc = headerInfo.ExecutionTimeUtc,
                BuySell = isBuyer ? "Buy" : "Sell",
                Notional = notional,
                NotionalCurrency = notionalCcy,
                SettlementDate = option.DeliveryDate,
                Uti = uti,           // Full UTI: Namespace + leg UTI
                Tvtic = tvtic,       // Leg-specific UTI
                CallPut = callPut,
                Strike = option.Strike,
                ExpiryDate = option.ExpiryDate,
                Cut = "USNY",
                Premium = option.Premium,
                PremiumCurrency = premiumCcy,
                PremiumDate = option.PremiumDate,
                SpotRate = option.SpotRate,
                SwapPoints = option.SwapPoints,
                PortfolioMx3 = portfolioMx3,
                CalypsoBook = calypsoBook?.CalypsoBook,
                IsDeleted = false,
                LastUpdatedUtc = DateTime.UtcNow
            };

            var systemLinks = CreateSystemLinks(trade);
            var workflowEvents = CreateWorkflowEvents(trade);

            return new ParsedTradeResult
            {
                Trade = trade,
                SystemLinks = systemLinks,
                WorkflowEvents = workflowEvents
            };
        }

        private ParsedTradeResult BuildHedgeTrade(HedgeData hedge, HeaderInfo headerInfo, TraderRoutingInfo traderRouting, MessageIn message, int legNumber)
        {
            // Determine proper currency pair order
            var soldCcy = hedge.SoldCurrency;
            var boughtCcy = hedge.BoughtCurrency;

            var soldPriority = CurrencyPriority.TryGetValue(soldCcy, out var sp) ? sp : 0;
            var boughtPriority = CurrencyPriority.TryGetValue(boughtCcy, out var bp) ? bp : 0;

            string ccyPair, buySell, notionalCcy;
            decimal notional;

            if (soldPriority >= boughtPriority)
            {
                // Sold currency is base - we're selling base currency
                ccyPair = soldCcy + boughtCcy;
                buySell = "Sell";
                notional = hedge.SoldAmount;
                notionalCcy = soldCcy;
            }
            else
            {
                // Bought currency is base - we're buying base currency
                ccyPair = boughtCcy + soldCcy;
                buySell = "Buy";
                notional = hedge.BoughtAmount;
                notionalCcy = boughtCcy;
            }

            var counterpartyCode = ResolveCounterpartyFromName(hedge.CounterpartyName);
            if (string.IsNullOrEmpty(counterpartyCode))
            {
                counterpartyCode = "TULLETT";
            }

            var productType = (hedge.ValueDate - headerInfo.TradeDate).Days <= 2
                ? ProductType.Spot
                : ProductType.Fwd;

            var portfolioMx3 = _lookupRepository.GetPortfolioCode("MX3", ccyPair, productType.ToString());
            var calypsoBook = _lookupRepository.GetCalypsoBookByTraderId(traderRouting.InternalUserId);

            // Generate trade ID from broker reference
            var tradeId = GenerateTradeId(headerInfo.BrokerTradeReference, "H", legNumber);

            // TVTIC = leg-specific UTI from the hedge (e.g., "20260114045000000000000005388808")
            var tvtic = hedge.UTI;

            // UTI = UTI Namespace + leg UTI
            var uti = BuildFullUti(headerInfo.UtiNamespace, hedge.UTI);

            var trade = new Trade
            {
                TradeId = tradeId,
                ProductType = productType,
                SourceType = "EMAIL",
                SourceVenueCode = "TULLETT",
                MessageInId = message.MessageInId,
                CounterpartyCode = counterpartyCode,
                BrokerCode = "TULLETT",
                TraderId = traderRouting.InternalUserId,
                InvId = traderRouting.InvId,
                ReportingEntityId = traderRouting.ReportingEntityId,
                CurrencyPair = ccyPair,
                Mic = headerInfo.Mic ?? "TPIR",
                Isin = hedge.ISIN,
                TradeDate = headerInfo.TradeDate,
                ExecutionTimeUtc = headerInfo.ExecutionTimeUtc,
                BuySell = buySell,
                Notional = notional,
                NotionalCurrency = notionalCcy,
                SettlementDate = hedge.ValueDate,
                Uti = uti,           // Full UTI: Namespace + leg UTI
                Tvtic = tvtic,       // Leg-specific UTI
                HedgeRate = hedge.HedgeRate,
                HedgeType = productType == ProductType.Spot ? "SPOT" : "FWD",
                PortfolioMx3 = portfolioMx3,
                CalypsoBook = calypsoBook?.CalypsoBook,
                IsDeleted = false,
                LastUpdatedUtc = DateTime.UtcNow
            };

            var systemLinks = CreateSystemLinks(trade);
            var workflowEvents = CreateWorkflowEvents(trade);

            return new ParsedTradeResult
            {
                Trade = trade,
                SystemLinks = systemLinks,
                WorkflowEvents = workflowEvents
            };
        }

        /// <summary>
        /// Builds full UTI from namespace and leg UTI.
        /// E.g., "213800R54EFFINMY1P02" + "20260114045000000000000005388809" = "213800R54EFFINMY1P0220260114045000000000000005388809"
        /// </summary>
        private string BuildFullUti(string utiNamespace, string legUti)
        {
            if (string.IsNullOrWhiteSpace(legUti))
                return null;

            if (string.IsNullOrWhiteSpace(utiNamespace))
                return legUti;

            return utiNamespace + legUti;
        }

        #endregion

        #region Helpers

        // Standard currency priority for determining base currency (higher = base)
        private static readonly Dictionary<string, int> CurrencyPriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "EUR", 100 },
            { "GBP", 90 },
            { "AUD", 80 },
            { "NZD", 70 },
            { "USD", 60 },
            { "CAD", 50 },
            { "CHF", 40 },
            { "JPY", 30 },
            { "NOK", 20 },
            { "SEK", 10 },
            { "DKK", 5 }
        };

        private (string ccyPair, string callPut, decimal notional, string notionalCcy, string premiumCcy) DetermineOptionDetails(OptionData option, bool isBuyer)
        {
            // Parse Call on: SEK 270,000,000.00
            var callOnMatch = Regex.Match(option.CallOn ?? "", @"([A-Z]{3})\s*([\d,.]+)");
            // Parse Put on: USD 30,000,000.00
            var putOnMatch = Regex.Match(option.PutOn ?? "", @"([A-Z]{3})\s*([\d,.]+)");

            if (!callOnMatch.Success || !putOnMatch.Success)
                return (null, null, 0, null, null);

            var callCcy = callOnMatch.Groups[1].Value;
            var callAmt = decimal.Parse(callOnMatch.Groups[2].Value.Replace(",", ""), CultureInfo.InvariantCulture);

            var putCcy = putOnMatch.Groups[1].Value;
            var putAmt = decimal.Parse(putOnMatch.Groups[2].Value.Replace(",", ""), CultureInfo.InvariantCulture);

            // Determine base/quote currency based on market convention
            var callPriority = CurrencyPriority.TryGetValue(callCcy, out var cp) ? cp : 0;
            var putPriority = CurrencyPriority.TryGetValue(putCcy, out var pp) ? pp : 0;

            string baseCcy, quoteCcy;
            decimal baseAmt, quoteAmt;
            bool callIsBase;

            if (callPriority >= putPriority)
            {
                // Call currency is base (e.g., USD in USDSEK)
                baseCcy = callCcy;
                quoteCcy = putCcy;
                baseAmt = callAmt;
                quoteAmt = putAmt;
                callIsBase = true;
            }
            else
            {
                // Put currency is base (e.g., EUR in EURSEK)
                baseCcy = putCcy;
                quoteCcy = callCcy;
                baseAmt = putAmt;
                quoteAmt = callAmt;
                callIsBase = false;
            }

            var ccyPair = baseCcy + quoteCcy;

            // Determine Call/Put from Swedbank's perspective
            // The option type is determined by what RIGHT Swedbank gets on the BASE currency
            string callPut;
            if (isBuyer)
            {
                // Swedbank buys the option - gets the right
                // Call on base = Call, Put on base = Put
                callPut = callIsBase ? "Call" : "Put";
            }
            else
            {
                // Swedbank sells the option - gives away the right
                // From seller's perspective, it's still named by what the buyer gets
                callPut = callIsBase ? "Call" : "Put";
            }

            // Notional is always in base currency
            var notional = baseAmt;
            var notionalCcy = baseCcy;

            // Premium currency is extracted from the Premium field in ParseSingleOption
            // We pass null here - it will be set from the parsed Premium amount field
            // The actual premium currency comes from "Premium amount : USD 781,500.00"
            string premiumCcy = null;

            Debug.WriteLine($"DetermineOptionDetails: CallOn={option.CallOn}, PutOn={option.PutOn}");
            Debug.WriteLine($"  -> CcyPair={ccyPair}, CallPut={callPut}, Notional={notional} {notionalCcy}, isBuyer={isBuyer}");

            return (ccyPair, callPut, notional, notionalCcy, premiumCcy);
        }

        /// <summary>
        /// Generates trade ID from broker reference with leg suffix.
        /// E.g., "3790813 Version 1" -> "3790813-O1" for first option leg
        /// </summary>
        private string GenerateTradeId(string brokerTradeReference, string legType, int legNumber)
        {
            if (string.IsNullOrWhiteSpace(brokerTradeReference))
                return null;

            // Remove "Version N" suffix if present
            var baseRef = Regex.Replace(brokerTradeReference, @"\s*Version\s*\d+\s*$", "", RegexOptions.IgnoreCase).Trim();

            // legType: "O" for option, "H" for hedge
            return $"{baseRef}-{legType}{legNumber}";
        }

        private string ResolveCounterpartyFromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return _lookupRepository.ResolveCounterpartyCode("EMAIL", "TULLETT", name) ?? "TULLETT";
        }

        private List<TradeSystemLink> CreateSystemLinks(Trade trade)
        {
            var links = new List<TradeSystemLink>();

            links.Add(new TradeSystemLink
            {
                SystemCode = SystemCode.Mx3,
                Status = TradeSystemStatus.New,
                BookFlag = true,
                StpFlag = trade.ProductType == ProductType.OptionVanilla ||
                          trade.ProductType == ProductType.OptionNdo,
                LastUpdatedUtc = DateTime.UtcNow
            });

            if (trade.ProductType == ProductType.Spot || trade.ProductType == ProductType.Fwd)
            {
                links.Add(new TradeSystemLink
                {
                    SystemCode = SystemCode.Calypso,
                    Status = TradeSystemStatus.New,
                    BookFlag = true,
                    StpFlag = false,
                    LastUpdatedUtc = DateTime.UtcNow
                });
            }

            return links;
        }

        private List<TradeWorkflowEvent> CreateWorkflowEvents(Trade trade)
        {
            return new List<TradeWorkflowEvent>
            {
                new TradeWorkflowEvent
                {
                    EventType = "TradeNormalized",
                    EventTimeUtc = DateTime.UtcNow,
                    SystemCode = SystemCode.Stp,
                    InitiatorId = "TullettOptionConfirmationParser",
                    Description = "Confirmation normalized"
                }
            };
        }

        #endregion
    }
}