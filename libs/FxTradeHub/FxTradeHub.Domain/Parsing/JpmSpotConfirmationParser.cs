using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Enums;
using FxTradeHub.Domain.Interfaces;

namespace FxTradeHub.Domain.Parsing
{
    /// <summary>
    /// Parser för JPM email-bekräftelser (spot trades).
    /// Läser HTML body från Power Automate email-filer och extraherar trade data.
    /// </summary>
    public sealed class JpmSpotConfirmationParser : IInboundMessageParser
    {
        private readonly IStpLookupRepository _lookupRepository;

        /// <summary>
        /// Skapar en ny instans av JpmSpotConfirmationParser.
        /// </summary>
        /// <param name="lookupRepository">Repository för lookup av trader routing och counterparty mapping.</param>
        public JpmSpotConfirmationParser(IStpLookupRepository lookupRepository)
        {
            _lookupRepository = lookupRepository ?? throw new ArgumentNullException(nameof(lookupRepository));
        }

        /// <summary>
        /// Identifierar om detta är ett JPM email-meddelande (SourceType=EMAIL, SourceVenueCode=JPM).
        /// </summary>
        public bool CanParse(MessageIn message)
        {
            if (message == null)
                return false;

            if (!string.Equals(message.SourceType, "EMAIL", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(message.SourceVenueCode, "JPM", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }


        /// <summary>
        /// Parsar JPM email body och extraherar SPOT trade data.
        /// Returnerar ParseResult med Trade + TradeNormalized workflow event.
        /// </summary>
        public ParseResult Parse(MessageIn message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.RawPayload))
            {
                return ParseResult.Failed("Message or RawPayload is empty");
            }

            try
            {
                var body = message.RawPayload;

                // Parse fields från plain text body direkt
                var tradeData = ParseTradeFields(body);

                if (tradeData == null)
                {
                    return ParseResult.Failed("Failed to parse trade fields from email body");
                }

                // Validate product type
                if (!string.Equals(tradeData.TradeType, "SPOT", StringComparison.OrdinalIgnoreCase))
                {
                    return ParseResult.Failed($"Unsupported trade type: {tradeData.TradeType}. Only SPOT is supported.");
                }

                // Lookup trader routing
                var traderRouting = _lookupRepository.GetTraderRoutingInfo("JPM", tradeData.CustomerOwner);
                if (traderRouting == null)
                {
                    return ParseResult.Failed($"Trader routing not found for JPM trader: {tradeData.CustomerOwner}");
                }

                // Lookup counterparty (JPM är alltid counterparty för dessa trades)
                var counterpartyCode = _lookupRepository.ResolveCounterpartyCode("EMAIL", "JPM", "JPM");
                if (string.IsNullOrWhiteSpace(counterpartyCode))
                {
                    return ParseResult.Failed("Counterparty mapping not found for JPM");
                }

                // Lookup MX3 portfolio
                var portfolioMx3 = _lookupRepository.GetPortfolioCode("MX3", tradeData.CurrencyPair, "SPOT");
                if (string.IsNullOrWhiteSpace(portfolioMx3))
                {
                    return ParseResult.Failed($"MX3 portfolio not found for {tradeData.CurrencyPair} SPOT");
                }

                // Lookup Calypso book för trader
                var calypsoBook = _lookupRepository.GetCalypsoBookByTraderId(traderRouting.InternalUserId);

                // Skapa Trade entity
                var trade = new Trade
                {
                    TradeId = tradeData.TradeId,
                    ProductType = ProductType.Spot,
                    SourceType = "EMAIL",
                    SourceVenueCode = "JPM",
                    CounterpartyCode = counterpartyCode,
                    TraderId = traderRouting.InternalUserId,
                    InvId = traderRouting.InvId,
                    ReportingEntityId = traderRouting.ReportingEntityId,
                    CurrencyPair = tradeData.CurrencyPair,
                    Mic = "XOFF",
                    TradeDate = tradeData.TradeDate,
                    ExecutionTimeUtc = tradeData.TradeTime,
                    BuySell = tradeData.BuySell,
                    Notional = tradeData.Notional,
                    NotionalCurrency = tradeData.NotionalCurrency,
                    SettlementDate = tradeData.ValueDate,
                    SpotRate = tradeData.SpotRate,
                    HedgeRate = tradeData.SpotRate,
                    HedgeType = "Spot",
                    SettlementCurrency = tradeData.NotionalCurrency,
                    PortfolioMx3 = portfolioMx3,
                    CalypsoBook = calypsoBook?.CalypsoBook,
                    IsNonDeliverable = false
                };


                // Skapa TradeNormalized workflow event
                var workflowEvents = new List<TradeWorkflowEvent>
                {
                    new TradeWorkflowEvent
                    {
                        EventType = "TradeNormalized",
                        EventTimeUtc = DateTime.UtcNow,
                        SystemCode = SystemCode.Stp,
                        InitiatorId = "JpmSpotConfirmationParser",
                        Description = "Spot normalized"
                    }
                };

                var parsedTrade = new ParsedTradeResult
                {
                    Trade = trade,
                    SystemLinks = null,  // Systemlänkar skapas av MessageInParserOrchestrator
                    WorkflowEvents = workflowEvents
                };

                return ParseResult.Ok(new List<ParsedTradeResult> { parsedTrade });
            }
            catch (Exception ex)
            {
                return ParseResult.Failed($"Parse exception: {ex.Message}");
            }
        }


        /// <summary>
        /// Extraherar plain text från HTML body genom att ta innehåll från span lang="EN-GB" taggar.
        /// </summary>
        private string ExtractPlainTextFromHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return null;

            // Regex för att extrahera text från <span lang="EN-GB">TEXT</span>
            var regex = new Regex(@"<span lang=""EN-GB"">(.*?)</span>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var matches = regex.Matches(html);

            var lines = new List<string>();
            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var text = match.Groups[1].Value;
                    // Decode HTML entities (&nbsp; etc)
                    text = System.Net.WebUtility.HtmlDecode(text);
                    lines.Add(text);
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Parsar trade fields från plain text (tabulerad format från JPM email).
        /// Hanterar både ORDER-format (Customer Owner) och DIRECT TRADE-format (Customer User fallback).
        /// </summary>
        private JpmTradeData ParseTradeFields(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
                return null;

            var data = new JpmTradeData();

            // Parse fields med regex patterns
            data.TradeId = ExtractField(plainText, @"Trade ID:\s+(\S+)");
            data.TradeType = ExtractField(plainText, @"Trade Type:\s+(\w+)");
            data.CurrencyPair = ExtractField(plainText, @"Currency Pair:\s+([\w/]+)");

            // Customer Owner (ORDER format) ELLER Customer User (DIRECT TRADE format)
            data.CustomerOwner = ExtractField(plainText, @"Customer Owner:\s+(\w+)");
            if (string.IsNullOrWhiteSpace(data.CustomerOwner))
            {
                data.CustomerOwner = ExtractField(plainText, @"Customer User:\s+(\w+)");
            }

            // Parse dates
            var tradeDateStr = ExtractField(plainText, @"Trade Date:\s+([\d\-A-Z]+)");
            data.TradeDate = ParseJpmDate(tradeDateStr);

            var tradeTimeStr = ExtractField(plainText, @"Trade Time:\s+(.+)");
            data.TradeTime = ParseJpmDateTime(tradeTimeStr);

            var valueDateStr = ExtractField(plainText, @"Value Date:\s+([\d\-A-Z]+)");
            data.ValueDate = ParseJpmDate(valueDateStr);

            // Parse amounts (BUY: EUR 500,000.00 / SELL: SEK 5,406,034.00)
            // Hanterar både med och utan leading whitespace (DIRECT TRADE vs ORDER format)
            var buyLine = ExtractField(plainText, @"^\s*BUY:\s+([^\r\n]+)", RegexOptions.Multiline);
            var sellLine = ExtractField(plainText, @"^\s*SELL:\s+([^\r\n]+)", RegexOptions.Multiline);

            ParseBuySellLine(buyLine, out var buyCcy, out var buyAmount);
            ParseBuySellLine(sellLine, out var sellCcy, out var sellAmount);

            // Normalize Currency Pair (CNH/SEK → CNHSEK)
            if (!string.IsNullOrWhiteSpace(data.CurrencyPair))
            {
                data.CurrencyPair = data.CurrencyPair.Replace("/", string.Empty);
            }

            // Determine BuySell based on CurrencyPair base currency
            if (!string.IsNullOrWhiteSpace(data.CurrencyPair) && data.CurrencyPair.Length >= 3)
            {
                var baseCcy = data.CurrencyPair.Substring(0, 3);

                if (string.Equals(baseCcy, buyCcy, StringComparison.OrdinalIgnoreCase))
                {
                    data.BuySell = "Buy";
                    data.NotionalCurrency = buyCcy;
                    data.Notional = buyAmount;
                }
                else
                {
                    data.BuySell = "Sell";
                    data.NotionalCurrency = sellCcy;
                    data.Notional = sellAmount;
                }
            }

            // Parse spot rate (försök först "Spot Rate:", sedan "All In Rate:")
            var spotRateStr = ExtractField(plainText, @"Spot Rate:\s+([\d\.]+)");
            if (string.IsNullOrWhiteSpace(spotRateStr))
            {
                spotRateStr = ExtractField(plainText, @"All In Rate:\s+([\d\.]+)");
            }

            if (decimal.TryParse(spotRateStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var spotRate))
            {
                data.SpotRate = spotRate;
            }

            return data;
        }


        /// <summary>
        /// Extraherar field från text med regex pattern.
        /// </summary>
        private string ExtractField(string text, string pattern)
        {
            return ExtractField(text, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Extraherar field från text med regex pattern och custom options.
        /// </summary>
        private string ExtractField(string text, string pattern, RegexOptions options)
        {
            var match = Regex.Match(text, pattern, options);
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : null;
        }



        /// <summary>
        /// Parsar JPM datum format: "05-JAN-2026" → DateTime.
        /// </summary>
        private DateTime ParseJpmDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
                return DateTime.UtcNow.Date;

            // Format: 05-JAN-2026
            if (DateTime.TryParseExact(dateStr, "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }

            return DateTime.UtcNow.Date;
        }

        /// <summary>
        /// Parsar JPM datetime format: "Mon Jan 05 2026 00:20:25.004 GMT-05:00" → DateTime UTC.
        /// </summary>
        private DateTime ParseJpmDateTime(string dateTimeStr)
        {
            if (string.IsNullOrWhiteSpace(dateTimeStr))
                return DateTime.UtcNow;

            // Ta bort timezone suffix (GMT-05:00) och parsa som UTC
            var cleaned = Regex.Replace(dateTimeStr, @"\s+GMT[+-]\d{2}:\d{2}$", string.Empty);

            if (DateTime.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                return dt;
            }

            return DateTime.UtcNow;
        }

        /// <summary>
        /// Parsar BUY/SELL line: "EUR 500,000.00" → ("EUR", 500000.00).
        /// </summary>
        private void ParseBuySellLine(string line, out string currency, out decimal amount)
        {
            currency = null;
            amount = 0m;

            if (string.IsNullOrWhiteSpace(line))
                return;

            // Format: "EUR 500,000.00"
            var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                currency = parts[0];
                var amountStr = parts[1].Replace(",", string.Empty);
                decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out amount);
            }
        }

        /// <summary>
        /// Internal DTO för parsed JPM trade data.
        /// </summary>
        private class JpmTradeData
        {
            public string TradeId { get; set; }
            public string TradeType { get; set; }
            public string CurrencyPair { get; set; }
            public string CustomerOwner { get; set; }
            public DateTime TradeDate { get; set; }
            public DateTime TradeTime { get; set; }
            public DateTime ValueDate { get; set; }
            public string BuySell { get; set; }
            public string NotionalCurrency { get; set; }
            public decimal Notional { get; set; }
            public decimal SpotRate { get; set; }
        }
    }
}
