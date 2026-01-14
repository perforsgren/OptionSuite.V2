// File: libs/FxTradeHub/FxTradeHub.Domain/Parsing/TullettOptionConfirmationParser.cs

using System;
using System.Collections.Generic;
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

                var headerInfo = ParseHeaderInfo(body);
                if (headerInfo == null)
                {
                    return ParseResult.Failed("Failed to parse header information");
                }

                var options = ParseOptions(body, headerInfo);
                if (options == null || options.Count == 0)
                {
                    return ParseResult.Failed("No options found in confirmation");
                }

                var hedge = ParseHedge(body, headerInfo);

                var traderRouting = _lookupRepository.GetTraderRoutingInfo("TULLETT", headerInfo.TraderName);
                if (traderRouting == null)
                {
                    return ParseResult.Failed($"Trader routing not found for TULLETT trader: {headerInfo.TraderName}");
                }

                var results = new List<ParsedTradeResult>();

                foreach (var optionData in options)
                {
                    var optionTrade = BuildOptionTrade(optionData, headerInfo, traderRouting, message);
                    if (optionTrade != null)
                    {
                        results.Add(optionTrade);
                    }
                }

                if (hedge != null)
                {
                    var hedgeTrade = BuildHedgeTrade(hedge, headerInfo, traderRouting, message);
                    if (hedgeTrade != null)
                    {
                        results.Add(hedgeTrade);
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
            public DateTime PremiumDate { get; set; }
            public string SellerName { get; set; }
            public string SellerLEI { get; set; }
            public string BuyerName { get; set; }
            public string BuyerLEI { get; set; }
        }

        private List<OptionData> ParseOptions(string body, HeaderInfo headerInfo)
        {
            var options = new List<OptionData>();

            var pattern = @"Seller\s*:\s*([^\r\n]+).*?Seller LEI\s*:\s*([A-Z0-9]+).*?Buyer\s*:\s*([^\r\n]+).*?Buyer LEI\s*:\s*([A-Z0-9]+).*?Option\s+(\d+)\s*\n(.*?)(?=(?:Seller\s*:|Confirmation of Hedge|BROKERAGE|$))";
            var optionMatches = Regex.Matches(body, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match optionMatch in optionMatches)
            {
                var sellerName = optionMatch.Groups[1].Value.Trim();
                var sellerLEI = optionMatch.Groups[2].Value.Trim();
                var buyerName = optionMatch.Groups[3].Value.Trim();
                var buyerLEI = optionMatch.Groups[4].Value.Trim();
                var optionNumber = optionMatch.Groups[5].Value;
                var optionSection = optionMatch.Groups[6].Value;

                var option = ParseSingleOption(optionSection);
                if (option != null)
                {
                    option.SellerName = sellerName;
                    option.SellerLEI = sellerLEI;
                    option.BuyerName = buyerName;
                    option.BuyerLEI = buyerLEI;
                    options.Add(option);
                }
            }

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

            var premiumMatch = Regex.Match(optionSection, @"Premium amount\s*:\s*[A-Z]{3}\s*([\d,.]+)", RegexOptions.IgnoreCase);
            if (premiumMatch.Success)
            {
                if (decimal.TryParse(premiumMatch.Groups[1].Value.Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var premium))
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

        private ParsedTradeResult BuildOptionTrade(OptionData option, HeaderInfo headerInfo, TraderRoutingInfo traderRouting, MessageIn message)
        {
            bool isBuyer = string.Equals(option.BuyerLEI, SWEDBANK_LEI, StringComparison.OrdinalIgnoreCase) ||
                           (string.IsNullOrEmpty(option.BuyerLEI) && option.BuyerName.IndexOf("SWEDBANK", StringComparison.OrdinalIgnoreCase) >= 0);

            bool isSeller = string.Equals(option.SellerLEI, SWEDBANK_LEI, StringComparison.OrdinalIgnoreCase) ||
                            (string.IsNullOrEmpty(option.SellerLEI) && option.SellerName.IndexOf("SWEDBANK", StringComparison.OrdinalIgnoreCase) >= 0);

            if (!isBuyer && !isSeller)
            {
                return null;
            }

            var (ccyPair, callPut, notional, notionalCcy, premiumCcy) = DetermineOptionDetails(option, isBuyer);

            if (string.IsNullOrEmpty(ccyPair))
            {
                return null;
            }

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

            var trade = new Trade
            {
                TradeId = option.UTI,
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
                Uti = option.UTI,
                Tvtic = headerInfo.RTN,
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

        private ParsedTradeResult BuildHedgeTrade(HedgeData hedge, HeaderInfo headerInfo, TraderRoutingInfo traderRouting, MessageIn message)
        {
            var ccyPair = hedge.SoldCurrency + hedge.BoughtCurrency;
            var buySell = "Sell";
            var notional = hedge.SoldAmount;
            var notionalCcy = hedge.SoldCurrency;

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

            var trade = new Trade
            {
                TradeId = hedge.UTI,
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
                Uti = hedge.UTI,
                Tvtic = headerInfo.RTN,
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

        #endregion

        #region Helpers

        private (string ccyPair, string callPut, decimal notional, string notionalCcy, string premiumCcy) DetermineOptionDetails(OptionData option, bool isBuyer)
        {
            var callOnMatch = Regex.Match(option.CallOn, @"([A-Z]{3})\s*([\d,.]+)");
            var putOnMatch = Regex.Match(option.PutOn, @"([A-Z]{3})\s*([\d,.]+)");

            if (!callOnMatch.Success || !putOnMatch.Success)
                return (null, null, 0, null, null);

            var callCcy = callOnMatch.Groups[1].Value;
            var callAmt = decimal.Parse(callOnMatch.Groups[2].Value.Replace(",", ""), CultureInfo.InvariantCulture);

            var putCcy = putOnMatch.Groups[1].Value;
            var putAmt = decimal.Parse(putOnMatch.Groups[2].Value.Replace(",", ""), CultureInfo.InvariantCulture);

            var ccyPair = callCcy + putCcy;
            var premiumCcy = putCcy;
            var notional = callAmt;
            var notionalCcy = callCcy;
            var callPut = isBuyer ? "Call" : "Put";

            return (ccyPair, callPut, notional, notionalCcy, premiumCcy);
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
