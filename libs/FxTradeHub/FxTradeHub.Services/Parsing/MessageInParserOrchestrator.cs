using System;
using System.Collections.Generic;
using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Interfaces;
using FxTradeHub.Domain.Parsing;
using FxTradeHub.Domain.Enums;
using FxTradeHub.Domain.Repositories;

namespace FxTradeHub.Services.Parsing
{
    /// <summary>
    /// Koordinerar parsing av inkommande meddelanden genom att delegera till
    /// rätt parser och persistera normaliserade trades, systemlänkar och
    /// workflow-event. Stödjer flera trades per MessageIn (t.ex. option + hedge).
    /// </summary>
    public class MessageInParserOrchestrator : IMessageInParserOrchestrator
    {
        private readonly IMessageInRepository _messageRepo;
        private readonly IStpRepository _stpRepository;
        private readonly List<IInboundMessageParser> _parsers;

        // TODO: Flytta till lookup-tabell i DB (stp_venue_config) för att kunna 
        // administrera STP-eligible venues utan kodändring.
        // Tabell-förslag: SourceVenueCode VARCHAR(50), StpEligible TINYINT(1), CreatedUtc DATETIME
        private static readonly HashSet<string> StpEligibleVenues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "JPM",
            "BARX",
            "NATWEST",
            "AUTOBAHN"
            // Lägg till fler venues här
        };

        /// <summary>
        /// Skapar en ny instans av MessageInParserOrchestrator med angivna
        /// repository-implementationer och parserlista.
        /// </summary>
        public MessageInParserOrchestrator(
            IMessageInRepository messageRepo,
            IStpRepository stpRepository,
            List<IInboundMessageParser> parsers)
        {
            _messageRepo = messageRepo ?? throw new ArgumentNullException(nameof(messageRepo));
            _stpRepository = stpRepository ?? throw new ArgumentNullException(nameof(stpRepository));
            _parsers = parsers ?? new List<IInboundMessageParser>();
        }

        /// <summary>
        /// Bearbetar en batch av oparsade meddelanden (ParsedFlag = false).
        /// Hämtar ett begränsat antal rader från MessageIn och försöker parsa dem.
        /// </summary>
        public void ProcessPendingMessages()
        {
            const int maxBatchSize = 100;

            var pending = _messageRepo.GetUnparsedMessages(maxBatchSize);
            foreach (var msg in pending)
            {
                ProcessMessage(msg.MessageInId);
            }
        }

        /// <summary>
        /// Bearbetar ett enskilt inkommande meddelande identifierat via MessageInId.
        /// </summary>
        public void ProcessMessage(long messageInId, bool force)
        {
            var message = _messageRepo.GetById(messageInId);
            if (message == null)
                return;

            if (message.ParsedFlag && !force)
                return;

            var parser = FindParser(message);
            if (parser == null)
            {
                MarkFailed(message, "No parser available for this message.");
                return;
            }

            ParseAndPersist(message, parser);
        }

        /// <summary>
        /// Bearbetar ett enskilt inkommande meddelande identifierat via MessageInId.
        /// Standardbeteende: om ParsedFlag redan är satt så görs inget.
        /// </summary>
        public void ProcessMessage(long messageInId)
        {
            ProcessMessage(messageInId, false);
        }

        /// <summary>
        /// Försöker parsa om ett MessageIn även om det redan är markerat som behandlat.
        /// </summary>
        public void ReprocessMessage(long messageInId)
        {
            ProcessMessage(messageInId, true);
        }

        private IInboundMessageParser FindParser(MessageIn message)
        {
            foreach (var parser in _parsers)
            {
                if (parser.CanParse(message))
                    return parser;
            }

            return null;
        }

        /// <summary>
        /// Kör parsing för ett MessageIn med vald parser och persisterar resultatet.
        /// </summary>
        private void ParseAndPersist(MessageIn source, IInboundMessageParser parser)
        {
            try
            {
                var result = parser.Parse(source);

                if (!result.Success)
                {
                    MarkFailed(source, result.ErrorMessage);
                    return;
                }

                if (result.Trades == null || result.Trades.Count == 0)
                {
                    MarkFailed(source, "Parser returned success but no trades.");
                    return;
                }

                foreach (var tradeBundle in result.Trades)
                {
                    if (tradeBundle == null || tradeBundle.Trade == null)
                    {
                        MarkFailed(source, "Parser returned a trade bundle without Trade.");
                        return;
                    }

                    var trade = tradeBundle.Trade;
                    trade.MessageInId = source.MessageInId;

                    var stpTradeId = _stpRepository.InsertTrade(trade);

                    var systemCode = DetermineSystemCode(source.SourceType);
                    var description = BuildMessageInDescription(source);

                    var messageInEvent = new TradeWorkflowEvent
                    {
                        StpTradeId = stpTradeId,
                        EventType = "MessageInReceived",
                        EventTimeUtc = source.ReceivedUtc,
                        SystemCode = systemCode,
                        InitiatorId = "STP",
                        Description = description
                    };
                    _stpRepository.InsertTradeWorkflowEvent(messageInEvent);

                    var systemLinks = BuildSystemLinksForTrade(source, trade);

                    foreach (var link in systemLinks)
                    {
                        link.StpTradeId = stpTradeId;
                        _stpRepository.InsertTradeSystemLink(link);
                    }

                    if (tradeBundle.WorkflowEvents != null)
                    {
                        foreach (var evt in tradeBundle.WorkflowEvents)
                        {
                            evt.StpTradeId = stpTradeId;
                            _stpRepository.InsertTradeWorkflowEvent(evt);
                        }
                    }
                }

                MarkSuccess(source);
            }
            catch (Exception ex)
            {
                MarkFailed(source, ex.ToString());
            }
        }

        /// <summary>
        /// Bygger standardlänkar (TradeSystemLink) för en ny trade baserat på produkt och venue.
        /// </summary>
        private IList<TradeSystemLink> BuildSystemLinksForTrade(MessageIn source, Trade trade)
        {
            var now = DateTime.UtcNow;
            var links = new List<TradeSystemLink>();

            const string stpModeManual = "MANUAL";
            const string stpModeAuto = "AUTO";

            // OPTION: bokas i MX3 och kan kräva ACK tillbaka (VOLBROKER_STP-länk).
            if (trade.ProductType == ProductType.OptionVanilla ||
                trade.ProductType == ProductType.OptionNdo)
            {
                // MX3 (StpFlag=null för options)
                links.Add(CreateSystemLink(
                    systemCode: SystemCode.Mx3,
                    status: TradeSystemStatus.New,
                    portfolioCode: trade.PortfolioMx3,
                    bookFlag: true,
                    stpMode: stpModeManual,
                    stpFlag: null,
                    externalTradeId: null,
                    createdUtc: now,
                    lastUpdatedUtc: now,
                    importedBy: "STP"));

                if (string.Equals(trade.SourceVenueCode, "VOLBROKER", StringComparison.OrdinalIgnoreCase))
                {
                    links.Add(CreateSystemLink(
                        systemCode: SystemCode.VolbrokerStp,
                        status: TradeSystemStatus.New,
                        portfolioCode: null,
                        bookFlag: false,
                        stpMode: stpModeAuto,
                        stpFlag: null,
                        externalTradeId: trade.TradeId,
                        createdUtc: now,
                        lastUpdatedUtc: now,
                        importedBy: "STP"));
                }

                return links;
            }

            // SPOT/FWD: bokas i MX3 och i Calypso.
            if (trade.ProductType == ProductType.Spot ||
                trade.ProductType == ProductType.Fwd)
            {
                // MX3 (StpFlag=null)
                links.Add(CreateSystemLink(
                    systemCode: SystemCode.Mx3,
                    status: TradeSystemStatus.New,
                    portfolioCode: trade.PortfolioMx3,
                    bookFlag: true,
                    stpMode: stpModeManual,
                    stpFlag: null,
                    externalTradeId: null,
                    createdUtc: now,
                    lastUpdatedUtc: now,
                    importedBy: "STP"));

                // CALYPSO (StpFlag baserat på venue)
                var stpFlag = GetStpFlagForVenue(trade.SourceVenueCode);

                links.Add(CreateSystemLink(
                    systemCode: SystemCode.Calypso,
                    status: TradeSystemStatus.New,
                    portfolioCode: trade.CalypsoBook,
                    bookFlag: true,
                    stpMode: stpModeManual,
                    stpFlag: stpFlag,
                    externalTradeId: null,
                    createdUtc: now,
                    lastUpdatedUtc: now,
                    importedBy: "STP"));

                return links;
            }

            // Övriga produkttyper (SWAP/NDF etc) – V1: endast MX3 som default.
            links.Add(CreateSystemLink(
                systemCode: SystemCode.Mx3,
                status: TradeSystemStatus.New,
                portfolioCode: trade.PortfolioMx3,
                bookFlag: true,
                stpMode: stpModeManual,
                stpFlag: null,
                externalTradeId: null,
                createdUtc: now,
                lastUpdatedUtc: now,
                importedBy: "STP"));

            return links;
        }

        /// <summary>
        /// Avgör om venue är STP-eligible (Straight-Through Processing).
        /// Returnerar true om trades från denna venue ska ha StpFlag=1.
        /// 
        /// TODO: Flytta till IStpLookupRepository.IsVenueStpEligible() 
        /// med data från stp_venue_config-tabell.
        /// </summary>
        private bool? GetStpFlagForVenue(string sourceVenueCode)
        {
            if (string.IsNullOrWhiteSpace(sourceVenueCode))
                return null;

            return StpEligibleVenues.Contains(sourceVenueCode) ? true : (bool?)null;
        }

        /// <summary>
        /// Skapar en TradeSystemLink med konsistenta defaults.
        /// </summary>
        private TradeSystemLink CreateSystemLink(
            SystemCode systemCode,
            TradeSystemStatus status,
            string portfolioCode,
            bool? bookFlag,
            string stpMode,
            bool? stpFlag,
            string externalTradeId,
            DateTime createdUtc,
            DateTime lastUpdatedUtc,
            string importedBy)
        {
            return new TradeSystemLink
            {
                SystemCode = systemCode,
                Status = status,
                PortfolioCode = portfolioCode ?? string.Empty,
                BookFlag = bookFlag,
                StpMode = stpMode ?? string.Empty,
                ExternalTradeId = externalTradeId ?? string.Empty,
                ErrorCode = string.Empty,
                ErrorMessage = string.Empty,
                ImportedBy = importedBy ?? string.Empty,
                BookedBy = string.Empty,
                FirstBookedUtc = null,
                LastBookedUtc = null,
                StpFlag = stpFlag,
                IsDeleted = false,
                CreatedUtc = createdUtc,
                LastUpdatedUtc = lastUpdatedUtc
            };
        }

        private void MarkSuccess(MessageIn msg)
        {
            msg.ParsedFlag = true;
            msg.ParsedUtc = DateTime.UtcNow;
            msg.ParseError = null;
            _messageRepo.UpdateParsingState(msg);
        }

        private void MarkFailed(MessageIn msg, string error)
        {
            msg.ParsedFlag = true;
            msg.ParsedUtc = DateTime.UtcNow;
            msg.ParseError = error;
            _messageRepo.UpdateParsingState(msg);
        }

        private SystemCode DetermineSystemCode(string sourceType)
        {
            if (string.IsNullOrWhiteSpace(sourceType))
                return SystemCode.Fix;

            var upperSourceType = sourceType.ToUpperInvariant();

            switch (upperSourceType)
            {
                case "FIX":
                    return SystemCode.Fix;
                case "EMAIL":
                case "MAIL":
                    return SystemCode.Mail;
                case "FILE":
                    return SystemCode.Fix;
                default:
                    return SystemCode.Fix;
            }
        }

        private string BuildMessageInDescription(MessageIn source)
        {
            if (string.IsNullOrWhiteSpace(source.SourceType))
                return "Message received";

            var upperSourceType = source.SourceType.ToUpperInvariant();

            switch (upperSourceType)
            {
                case "FIX":
                    var seqNum = source.FixSeqNum.HasValue ? source.FixSeqNum.Value.ToString() : "N/A";
                    return $"FIX {source.FixMsgType ?? "AE"} from {source.SourceVenueCode}, SeqNum={seqNum}";
                case "EMAIL":
                case "MAIL":
                    return $"Email from {source.SourceVenueCode}";
                case "FILE":
                    return $"File from {source.SourceVenueCode}";
                default:
                    return $"Message from {source.SourceVenueCode}";
            }
        }
    }
}