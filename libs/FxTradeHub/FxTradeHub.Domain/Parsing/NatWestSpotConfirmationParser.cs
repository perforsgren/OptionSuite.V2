using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Enums;
using FxTradeHub.Domain.Interfaces;
using FxTradeHub.Domain.Repositories;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FxTradeHub.Domain.Parsing
{
    /// <summary>
    /// Parser för NatWest spot confirmation emails.
    /// Hanterar HTML-format från NatWest Markets.
    /// </summary>
    public class NatWestSpotConfirmationParser : IInboundMessageParser
    {
        private readonly IStpLookupRepository _lookupRepo;

        public NatWestSpotConfirmationParser(IStpLookupRepository lookupRepo)
        {
            _lookupRepo = lookupRepo ?? throw new ArgumentNullException(nameof(lookupRepo));
        }

        public bool CanParse(MessageIn messageIn)
        {
            if (messageIn == null) return false;
            if (string.IsNullOrWhiteSpace(messageIn.RawPayload)) return false;

            var payload = messageIn.RawPayload;
            return payload.IndexOf("NatWest Deal Notification", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   payload.IndexOf("natwest.com/markets", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public ParseResult Parse(MessageIn messageIn)
        {
            if (messageIn == null)
                return ParseResult.Failed("MessageIn är null");

            var html = messageIn.RawPayload;
            if (string.IsNullOrWhiteSpace(html))
                return ParseResult.Failed("RawPayload är tomt");

            try
            {
                var plainText = StripHtmlTags(html);

                var data = new ParsedData
                {
                    TradeReference = ExtractField(plainText, @"Trade Reference\s+([A-Z0-9]+)"),
                    Trader = ExtractField(plainText, @"Trader\s+([\w\.]+)"),
                    ExecutionTime = ExtractField(plainText, @"Time of execution\s+([\d\/\s:]+)\s*\(GMT\)"),
                    CurrencyPair = ExtractField(plainText, @"(EUR\/SEK|SEK\/EUR|USD\/SEK|EUR\/USD)\s+Spot"),
                    BuySellText = ExtractField(plainText, @"Buy/Sell\s+Counterparty (Sells|Buys)\s+(\w{3})"),
                    NotionalAmounts = ExtractField(plainText, @"Notional Amount\s+([\d,\.\s]+EUR[\d,\.\s]+SEK|[\d,\.\s]+SEK[\d,\.\s]+EUR|[\d,\.\s]+USD[\d,\.\s]+SEK)"),
                    SpotRate = ExtractField(plainText, @"Spot Rate\s+([\d\.]+)"),
                    SettlementDate = ExtractField(plainText, @"Settlement Date\s+([\d\-A-Za-z]+)"),
                    TradeDate = ExtractField(plainText, @"Trade Date\s+([\d\-A-Za-z]+)")
                };

                if (string.IsNullOrWhiteSpace(data.TradeReference))
                    return ParseResult.Failed("Could not extract Trade Reference from NatWest email");

                // Parse execution time
                DateTime executionTimeUtc;
                if (!string.IsNullOrWhiteSpace(data.ExecutionTime))
                {
                    executionTimeUtc = ParseNatWestDateTime(data.ExecutionTime);
                }
                else
                {
                    executionTimeUtc = messageIn.ReceivedUtc;
                }

                // Parse currency pair
                string ccyPair = data.CurrencyPair?.Replace("/", "") ?? "UNKNOWN";

                // Parse buy/sell
                string buySell = ParseBuySell(data.BuySellText);

                // Parse notional
                var (notional, notionalCurrency) = ParseNotional(data.NotionalAmounts, ccyPair, buySell);

                // Parse spot rate
                decimal? spotRate = null;
                if (decimal.TryParse(data.SpotRate, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
                {
                    spotRate = rate;
                }

                // Parse settlement date
                DateTime settlementDate = messageIn.ReceivedUtc.Date;
                if (!string.IsNullOrWhiteSpace(data.SettlementDate))
                {
                    settlementDate = ParseNatWestDate(data.SettlementDate);
                }

                // Parse trade date
                DateTime tradeDate = messageIn.ReceivedUtc.Date;
                if (!string.IsNullOrWhiteSpace(data.TradeDate))
                {
                    tradeDate = ParseNatWestDate(data.TradeDate);
                }

                // Trader routing lookup
                var traderRoutingInfo = _lookupRepo.GetTraderRoutingInfo("NATWEST", data.Trader);
                string internalTraderId = traderRoutingInfo?.InternalUserId ?? data.Trader;

                // Counterparty lookup
                var counterpartyCode = _lookupRepo.ResolveCounterpartyCode(
                    "EMAIL",
                    "NATWEST",
                    "NatWest"
                );

                if (string.IsNullOrWhiteSpace(counterpartyCode))
                {
                    return ParseResult.Failed("Counterparty mapping not found for NatWest");
                }

                // Portfolio lookup
                string portfolioMx3 = _lookupRepo.GetPortfolioCode("MX3", ccyPair, "SPOT") ?? "FX_SPOT";

                // CalypsoBook lookup (för SPOT)
                string calypsoBook = string.Empty;
                if (!string.IsNullOrWhiteSpace(internalTraderId))
                {
                    var calypsoRule = _lookupRepo.GetCalypsoBookByTraderId(internalTraderId);
                    if (calypsoRule != null && calypsoRule.IsActive && !string.IsNullOrWhiteSpace(calypsoRule.CalypsoBook))
                    {
                        calypsoBook = calypsoRule.CalypsoBook;
                    }
                }

                var trade = new Trade
                {
                    TradeId = data.TradeReference,
                    ProductType = ProductType.Spot,
                    SourceType = "EMAIL",
                    SourceVenueCode = "NATWEST",
                    CounterpartyCode = counterpartyCode,
                    TraderId = internalTraderId,
                    InvId = traderRoutingInfo?.InvId ?? string.Empty,
                    ReportingEntityId = traderRoutingInfo?.ReportingEntityId ?? string.Empty,
                    CurrencyPair = ccyPair,
                    BuySell = buySell,
                    Notional = notional,
                    NotionalCurrency = notionalCurrency,
                    SpotRate = spotRate,
                    HedgeRate = spotRate,
                    HedgeType = "Spot",
                    SettlementDate = settlementDate,
                    SettlementCurrency = notionalCurrency,
                    TradeDate = tradeDate,
                    ExecutionTimeUtc = executionTimeUtc,
                    PortfolioMx3 = portfolioMx3,
                    CalypsoBook = calypsoBook,
                    Mic = "XOFF",
                    MessageInId = messageIn.MessageInId
                };

                var workflowEvents = new List<TradeWorkflowEvent>
                {
                    new TradeWorkflowEvent
                    {
                        EventType = "TradeNormalized",
                        EventTimeUtc = DateTime.UtcNow,
                        SystemCode = SystemCode.Stp,
                        InitiatorId = "NatWestSpotConfirmationParser",
                        Description = "Spot normalized"
                    }
                };

                var parsedTrades = new List<ParsedTradeResult>
                {
                    new ParsedTradeResult
                    {
                        Trade = trade,
                        SystemLinks = new List<TradeSystemLink>(),
                        WorkflowEvents = workflowEvents
                    }
                };

                return ParseResult.Ok(parsedTrades);
            }
            catch (Exception ex)
            {
                return ParseResult.Failed("Fel vid parsning av NatWest email: " + ex.Message);
            }
        }

        private class ParsedData
        {
            public string TradeReference { get; set; }
            public string Trader { get; set; }
            public string ExecutionTime { get; set; }
            public string CurrencyPair { get; set; }
            public string BuySellText { get; set; }
            public string NotionalAmounts { get; set; }
            public string SpotRate { get; set; }
            public string SettlementDate { get; set; }
            public string TradeDate { get; set; }
        }

        private string ExtractField(string text, string pattern)
        {
            var match = Regex.Match(text, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return match.Success && match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : null;
        }

        private string StripHtmlTags(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            var text = Regex.Replace(html, @"<[^>]+>", " ");
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }

        private DateTime ParseNatWestDateTime(string dateTimeStr)
        {
            var cleaned = dateTimeStr.Replace("(GMT)", "").Trim();

            if (DateTime.TryParseExact(cleaned, "dd/MM/yyyy HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            {
                return dt.ToUniversalTime();
            }

            throw new Exception("Could not parse NatWest execution time: " + dateTimeStr);
        }

        private DateTime ParseNatWestDate(string dateStr)
        {
            if (DateTime.TryParseExact(dateStr, "dd-MMM-yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt;
            }

            throw new Exception("Could not parse NatWest date: " + dateStr);
        }

        private string ParseBuySell(string buySellText)
        {
            if (string.IsNullOrWhiteSpace(buySellText))
                return "UNKNOWN";

            // "Counterparty Sells EUR" = Vi (Swedbank) säljer EUR = SELL
            // "Counterparty Buys EUR" = Vi (Swedbank) köper EUR = BUY
            if (buySellText.IndexOf("Sells", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Sell";
            else if (buySellText.IndexOf("Buys", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Buy";

            return "UNKNOWN";
        }

        private (decimal notional, string notionalCurrency) ParseNotional(string notionalText, string ccyPair, string buySell)
        {
            if (string.IsNullOrWhiteSpace(notionalText))
                throw new Exception("Notional Amount field is empty");

            if (string.IsNullOrWhiteSpace(ccyPair) || ccyPair.Length < 6)
                throw new Exception("Invalid currency pair: " + ccyPair);

            // Basvalutan är de första 3 bokstäverna i ccyPair
            var baseCurrency = ccyPair.Substring(0, 3).ToUpperInvariant();

            // Sök efter belopp för basvalutan i notionalText
            // Pattern: antal följt av valutakod, t.ex. "2,500,000.00 EUR"
            var pattern = @"([\d,\.]+)\s*" + baseCurrency;
            var match = Regex.Match(notionalText, pattern, RegexOptions.IgnoreCase);

            if (match.Success)
            {
                var amountStr = match.Groups[1].Value.Replace(",", "");
                if (decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                {
                    return (amount, baseCurrency);
                }
            }

            throw new Exception("Could not parse notional for base currency " + baseCurrency + " from: " + notionalText);
        }

    }
}
