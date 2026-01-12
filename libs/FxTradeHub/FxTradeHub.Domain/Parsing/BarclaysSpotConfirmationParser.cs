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
    /// Parser för Barclays (BARX) email-bekräftelser (spot trades).
    /// Läser plain text body från Power Automate email-filer och extraherar trade data.
    /// </summary>
    public sealed class BarclaysSpotConfirmationParser : IInboundMessageParser
    {
        private readonly IStpLookupRepository _lookupRepository;

        /// <summary>
        /// Skapar en ny instans av BarclaysSpotConfirmationParser.
        /// </summary>
        /// <param name="lookupRepository">Repository för lookup av trader routing och counterparty mapping.</param>
        public BarclaysSpotConfirmationParser(IStpLookupRepository lookupRepository)
        {
            _lookupRepository = lookupRepository ?? throw new ArgumentNullException(nameof(lookupRepository));
        }

        /// <summary>
        /// Identifierar om detta är ett Barclays email-meddelande (SourceType=EMAIL, SourceVenueCode=BARX).
        /// </summary>
        public bool CanParse(MessageIn message)
        {
            if (message == null)
                return false;

            if (!string.Equals(message.SourceType, "EMAIL", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(message.SourceVenueCode, "BARX", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        /// <summary>
        /// Parsar Barclays email body och extraherar SPOT trade data.
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

                var tradeData = ParseTradeFields(body);

                if (tradeData == null)
                {
                    return ParseResult.Failed("Failed to parse trade fields from email body");
                }

                // Validate product type
                if (!string.Equals(tradeData.ProductType, "SPOT", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tradeData.ProductType, "NDF", StringComparison.OrdinalIgnoreCase))
                {
                    return ParseResult.Failed($"Unsupported product type: {tradeData.ProductType}. Only SPOT and NDF are supported.");
                }

                // Lookup trader routing 
                var traderRouting = LookupTrader(tradeData.Username);
                if (traderRouting == null)
                {
                    return ParseResult.Failed($"Trader routing not found for BARX trader: {tradeData.Username}");
                }

                // Lookup counterparty
                var counterpartyCode = _lookupRepository.ResolveCounterpartyCode("EMAIL", "BARX", "BARX");
                if (string.IsNullOrWhiteSpace(counterpartyCode))
                {
                    return ParseResult.Failed("Counterparty mapping not found for BARX");
                }

                // Lookup MX3 portfolio
                var portfolioMx3 = _lookupRepository.GetPortfolioCode("MX3", tradeData.CurrencyPair, "SPOT");
                if (string.IsNullOrWhiteSpace(portfolioMx3))
                {
                    return ParseResult.Failed($"MX3 portfolio not found for {tradeData.CurrencyPair} SPOT");
                }

                // Lookup Calypso book
                var calypsoBook = _lookupRepository.GetCalypsoBookByTraderId(traderRouting.InternalUserId);

                // ExecutionTimeUtc: använd email received time (SourceTimestamp eller ReceivedUtc)
                // Fallback till TradeDate om timestamp saknas
                var executionTimeUtc = message.SourceTimestamp ?? message.ReceivedUtc;
                if (executionTimeUtc == default(DateTime))
                {
                    executionTimeUtc = tradeData.TradeDate;
                }

                // Skapa Trade entity
                var trade = new Trade
                {
                    TradeId = tradeData.TradeId,
                    ProductType = tradeData.IsNdf ? ProductType.Ndf : ProductType.Spot,
                    SourceType = "EMAIL",
                    SourceVenueCode = "BARX",
                    CounterpartyCode = counterpartyCode,
                    //BrokerCode = "BARX",
                    TraderId = traderRouting.InternalUserId,
                    InvId = traderRouting.InvId,
                    ReportingEntityId = traderRouting.ReportingEntityId,
                    CurrencyPair = tradeData.CurrencyPair,
                    Mic = "XOFF",
                    TradeDate = tradeData.TradeDate,
                    ExecutionTimeUtc = executionTimeUtc,
                    BuySell = tradeData.BuySell,
                    Notional = tradeData.Notional,
                    NotionalCurrency = tradeData.NotionalCurrency,
                    SettlementDate = tradeData.ValueDate,
                    IsNonDeliverable = tradeData.IsNdf,
                    FixingDate = tradeData.FixingDate,
                    SettlementCurrency = tradeData.SettlementCurrency,
                    SpotRate = tradeData.SpotRate,
                    SwapPoints = tradeData.ForwardPoints,
                    HedgeRate = tradeData.AllInRate > 0 ? tradeData.AllInRate : tradeData.SpotRate,
                    HedgeType = tradeData.IsNdf ? "NDF" : "Spot",
                    PortfolioMx3 = portfolioMx3,
                    CalypsoBook = calypsoBook?.CalypsoBook
                };


                // Skapa TradeNormalized workflow event
                var workflowEvents = new List<TradeWorkflowEvent>
                {
                    new TradeWorkflowEvent
                    {
                        EventType = "TradeNormalized",
                        EventTimeUtc = DateTime.UtcNow,
                        SystemCode = SystemCode.Stp,
                        InitiatorId = "BarclaysSpotConfirmationParser",
                        Description = "Spot normalized"
                    }
                };

                var parsedTrade = new ParsedTradeResult
                {
                    Trade = trade,
                    SystemLinks = null,
                    WorkflowEvents = workflowEvents
                };

                return ParseResult.Ok(new List<ParsedTradeResult> { parsedTrade });
            }
            catch (Exception ex)
            {
                return ParseResult.Failed($"Parse exception: {ex.Message}");
            }
        }

        private BarclaysTradeData ParseTradeFields(string plainText)
        {
            if (string.IsNullOrWhiteSpace(plainText))
                return null;

            var data = new BarclaysTradeData();

            data.TradeId = ExtractField(plainText, @"BARX Trade Id:\s+(\d+)");
            data.Username = ExtractField(plainText, @"Username:\s+([^\r\n]+)");

            // Parse dates (yyyyMMdd format)
            var tradeDateStr = ExtractField(plainText, @"Trade Date:\s+(\d{8})");
            data.TradeDate = ParseBarclaysDate(tradeDateStr);

            var valueDateStr = ExtractField(plainText, @"Value Date:\s+(\d{8})");
            data.ValueDate = ParseBarclaysDate(valueDateStr);

            var fixingDateStr = ExtractField(plainText, @"Fixing Date:\s+(\d{8})");
            if (!string.IsNullOrWhiteSpace(fixingDateStr))
            {
                data.FixingDate = ParseBarclaysDate(fixingDateStr);
                data.IsNdf = true;
            }

            // Determine ProductType based on Fixing Date
            if (data.IsNdf)
            {
                data.ProductType = "NDF";
            }
            else
            {
                var timeBucket = ExtractField(plainText, @"Time Bucket:\s+(\w+)");
                data.ProductType = MapTimeBucketToProduct(timeBucket);
            }

            // Parse currencies and amounts
            var buyCcy = ExtractField(plainText, @"Client Buys Ccy:\s+(\w+)");
            var sellCcy = ExtractField(plainText, @"Client Sells Ccy:\s+(\w+)");
            var buyAmountStr = ExtractField(plainText, @"Client Buys Amount:\s+([\d,\.]+)");
            var sellAmountStr = ExtractField(plainText, @"Client Sells Amount:\s+([\d,\.]+)");

            decimal.TryParse(buyAmountStr?.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var buyAmount);
            decimal.TryParse(sellAmountStr?.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var sellAmount);

            // Parse spot rate och forward points
            var spotRateStr = ExtractField(plainText, @"Spot Rate:\s+([\d\.]+)");
            decimal.TryParse(spotRateStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var spotRate);
            data.SpotRate = spotRate;

            var forwardPointsStr = ExtractField(plainText, @"Forward Points:\s+([-\d\.]+)");
            decimal.TryParse(forwardPointsStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var forwardPoints);
            data.ForwardPoints = forwardPoints;

            // All-in rate (Rate field)
            var rateStr = ExtractField(plainText, @"^\s*Rate:\s+([\d\.]+)", RegexOptions.Multiline);
            decimal.TryParse(rateStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate);
            data.AllInRate = rate;

            // Normalisera valutapar och bestäm notional
            // Använd det STÖRRE beloppet för att undvika avrundningsfel
            var normalizedData = NormalizeCurrencyPairAndNotional(buyCcy, sellCcy, buyAmount, sellAmount, spotRate);
            
            data.CurrencyPair = normalizedData.CurrencyPair;
            data.BuySell = normalizedData.BuySell;
            data.Notional = normalizedData.Notional;
            data.NotionalCurrency = normalizedData.NotionalCurrency;

            // Settlement Currency (för NDF är det alltid USD eller annan "hard currency")
            if (data.IsNdf)
            {
                data.SettlementCurrency = sellCcy;
            }

            return data;
        }


        /// <summary>
        /// Normaliserar valutapar enligt marknadskonvention och väljer korrekt notional.
        /// Använder det STÖRRE beloppet för att undvika avrundningsfel.
        /// </summary>
        private (string CurrencyPair, string BuySell, decimal Notional, string NotionalCurrency) 
            NormalizeCurrencyPairAndNotional(string buyCcy, string sellCcy, decimal buyAmount, decimal sellAmount, decimal spotRate)
        {
            // Marknadskonvention: EUR > GBP > AUD > NZD > USD > övriga
            var baseCurrencyOrder = new[] { "EUR", "GBP", "AUD", "NZD", "USD" };
            
            var buyIndex = Array.IndexOf(baseCurrencyOrder, buyCcy?.ToUpperInvariant());
            var sellIndex = Array.IndexOf(baseCurrencyOrder, sellCcy?.ToUpperInvariant());

            bool isBuyBaseCurrency;
            
            // Avgör vilken valuta som är base currency
            if (buyIndex >= 0 && sellIndex >= 0)
            {
                // Båda finns i listan → lägre index är base
                isBuyBaseCurrency = buyIndex < sellIndex;
            }
            else if (buyIndex >= 0)
            {
                // Bara buy finns i listan → buy är base
                isBuyBaseCurrency = true;
            }
            else if (sellIndex >= 0)
            {
                // Bara sell finns i listan → sell är base
                isBuyBaseCurrency = false;
            }
            else
            {
                // Ingen finns i listan → USD är default base om det finns, annars alfabetisk
                if (string.Equals(buyCcy, "USD", StringComparison.OrdinalIgnoreCase))
                    isBuyBaseCurrency = true;
                else if (string.Equals(sellCcy, "USD", StringComparison.OrdinalIgnoreCase))
                    isBuyBaseCurrency = false;
                else
                    isBuyBaseCurrency = string.Compare(buyCcy, sellCcy, StringComparison.OrdinalIgnoreCase) < 0;
            }

            string currencyPair;
            string buySell;
            decimal notional;
            string notionalCurrency;

            if (isBuyBaseCurrency)
            {
                // Client Buys CNY (base) → CNYUSD, Buy CNY
                currencyPair = buyCcy + sellCcy;
                buySell = "Buy";
                notional = buyAmount;
                notionalCurrency = buyCcy;
            }
            else
            {
                // Client Buys CNY men USD är base → USDCNY, Sell USD
                // Men vi handlade i CNY-belopp (1,000,000), inte USD-belopp (143,219 - avrundat!)
                // Lösning: Använd det STÖRRE beloppet som notional
                currencyPair = sellCcy + buyCcy;
                
                // Om buy-beloppet är större än sell-beloppet → vi handlade i den valutan
                if (buyAmount > sellAmount)
                {
                    // Handlade i CNY → notional = 1,000,000 CNY
                    // Men paret är USDCNY → vi måste konvertera till USD-notional
                    // Alternativ: Behåll CNY-beloppet och markera som "Sell USD" (motsvarar "Buy CNY")
                    buySell = "Buy";  // Client bought CNY
                    notional = buyAmount;
                    notionalCurrency = buyCcy;
                }
                else
                {
                    buySell = "Sell";
                    notional = sellAmount;
                    notionalCurrency = sellCcy;
                }
            }

            return (currencyPair, buySell, notional, notionalCurrency);
        }


        /// <summary>
        /// Mappar Barclays Time Bucket till ProductType.
        /// SP = SPOT, andra värden = FWD.
        /// </summary>
        private string MapTimeBucketToProduct(string timeBucket)
        {
            if (string.IsNullOrWhiteSpace(timeBucket))
                return "SPOT";

            var upper = timeBucket.ToUpperInvariant();
            if (upper == "SP")
                return "SPOT";

            return "FWD";
        }


        /// <summary>
        /// Parsar Barclays datum format: "20260107" → DateTime.
        /// </summary>
        private DateTime ParseBarclaysDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr) || dateStr.Length != 8)
                return DateTime.UtcNow.Date;

            if (DateTime.TryParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }

            return DateTime.UtcNow.Date;
        }

        /// <summary>
        /// Försöker hitta trader routing baserat på full name "Per Forsgren".
        /// </summary>
        private TraderRoutingInfo LookupTrader(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null;

            // Försök direkt lookup först (om username redan är kort kod)
            var routing = _lookupRepository.GetTraderRoutingInfo("BARX", username);
            if (routing != null)
                return routing;

            // Fallback: Försök extrahera initials från "Per Forsgren" → "PEFORSGREN"
            // Detta kräver mapping-tabell i DB
            return null;
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
        /// Internal DTO för parsed Barclays trade data.
        /// </summary>
        private class BarclaysTradeData
        {
            public string TradeId { get; set; }
            public string ProductType { get; set; }
            public string Username { get; set; }
            public DateTime TradeDate { get; set; }
            public DateTime ValueDate { get; set; }
            public DateTime? FixingDate { get; set; }
            public bool IsNdf { get; set; }
            public string BuySell { get; set; }
            public string NotionalCurrency { get; set; }
            public decimal Notional { get; set; }
            public string CurrencyPair { get; set; }
            public string SettlementCurrency { get; set; }
            public decimal SpotRate { get; set; }
            public decimal ForwardPoints { get; set; }
            public decimal AllInRate { get; set; }
        }

    }
}
