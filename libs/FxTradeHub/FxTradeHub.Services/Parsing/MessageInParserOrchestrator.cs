using System;
using System.Collections.Generic;
using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Interfaces;
using FxTradeHub.Domain.Parsing;
using FxTradeHub.Domain.Enums;
using FxTradeHub.Domain.Repositories;
using FxTradeHub.Services.Notifications;

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
        private readonly IMessageInNotificationService _notificationService;

        // TODO: Flytta till lookup-tabell i DB (stp_venue_config) för att kunna 
        // administrera STP-eligible venues utan kodändring.
        private static readonly HashSet<string> StpEligibleVenues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "JPM",
            "BARX",
            "NATWEST",
            "AUTOBAHN"
        };

        /// <summary>
        /// Skapar en ny instans av MessageInParserOrchestrator.
        /// </summary>
        public MessageInParserOrchestrator(
            IMessageInRepository messageRepo,
            IStpRepository stpRepository,
            List<IInboundMessageParser> parsers,
            IMessageInNotificationService notificationService = null)
        {
            _messageRepo = messageRepo ?? throw new ArgumentNullException(nameof(messageRepo));
            _stpRepository = stpRepository ?? throw new ArgumentNullException(nameof(stpRepository));
            _parsers = parsers ?? new List<IInboundMessageParser>();
            _notificationService = notificationService; // Optional - kan vara null
        }

        public void ProcessPendingMessages()
        {
            const int maxBatchSize = 100;

            var pending = _messageRepo.GetUnparsedMessages(maxBatchSize);
            foreach (var msg in pending)
            {
                ProcessMessage(msg.MessageInId);
            }
        }

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
                var errorMsg = "No parser available for this message.";
                MarkFailed(message, errorMsg);

                // ✅ Skicka email-notifiering vid total failure
                _notificationService?.NotifyMessageInFailure(
                    venueCode: message.SourceVenueCode ?? "UNKNOWN",
                    messageType: message.SourceType ?? "UNKNOWN",
                    sourceMessageKey: message.SourceMessageKey ?? $"MsgInId={message.MessageInId}",
                    fileName: BuildFileNameDescription(message),
                    errorMessage: errorMsg,
                    rawPayload: message.RawPayload ?? "(no payload)"
                );

                return;
            }

            ParseAndPersist(message, parser);
        }

        public void ProcessMessage(long messageInId)
        {
            ProcessMessage(messageInId, false);
        }

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

        private void ParseAndPersist(MessageIn source, IInboundMessageParser parser)
        {
            try
            {
                var result = parser.Parse(source);

                if (!result.Success)
                {
                    MarkFailed(source, result.ErrorMessage);

                    // ✅ Skicka email vid parser failure
                    _notificationService?.NotifyMessageInFailure(
                        venueCode: source.SourceVenueCode ?? "UNKNOWN",
                        messageType: source.SourceType ?? "UNKNOWN",
                        sourceMessageKey: source.SourceMessageKey ?? $"MsgInId={source.MessageInId}",
                        fileName: BuildFileNameDescription(source),
                        errorMessage: $"Parser: {parser.GetType().Name}, Error: {result.ErrorMessage}",
                        rawPayload: source.RawPayload ?? "(no payload)"
                    );

                    return;
                }

                if (result.Trades == null || result.Trades.Count == 0)
                {
                    var errorMsg = "Parser returned success but no trades.";
                    MarkFailed(source, errorMsg);

                    // ✅ Skicka email vid no-trades failure
                    _notificationService?.NotifyMessageInFailure(
                        venueCode: source.SourceVenueCode ?? "UNKNOWN",
                        messageType: source.SourceType ?? "UNKNOWN",
                        sourceMessageKey: source.SourceMessageKey ?? $"MsgInId={source.MessageInId}",
                        fileName: BuildFileNameDescription(source),
                        errorMessage: errorMsg,
                        rawPayload: source.RawPayload ?? "(no payload)"
                    );

                    return;
                }

                foreach (var tradeBundle in result.Trades)
                {
                    if (tradeBundle == null || tradeBundle.Trade == null)
                    {
                        var errorMsg = "Parser returned a trade bundle without Trade.";
                        MarkFailed(source, errorMsg);

                        // ✅ Skicka email vid null trade
                        _notificationService?.NotifyMessageInFailure(
                            venueCode: source.SourceVenueCode ?? "UNKNOWN",
                            messageType: source.SourceType ?? "UNKNOWN",
                            sourceMessageKey: source.SourceMessageKey ?? $"MsgInId={source.MessageInId}",
                            fileName: BuildFileNameDescription(source),
                            errorMessage: errorMsg,
                            rawPayload: source.RawPayload ?? "(no payload)"
                        );

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

                // ✅ Skicka email vid success (om aktiverat i settings)
                _notificationService?.NotifyMessageInSuccess(source);
            }
            catch (Exception ex)
            {
                MarkFailed(source, ex.ToString());

                // ✅ Skicka email vid exception
                _notificationService?.NotifyMessageInFailure(
                    venueCode: source.SourceVenueCode ?? "UNKNOWN",
                    messageType: source.SourceType ?? "UNKNOWN",
                    sourceMessageKey: source.SourceMessageKey ?? $"MsgInId={source.MessageInId}",
                    fileName: BuildFileNameDescription(source),
                    errorMessage: $"Exception: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}",
                    rawPayload: source.RawPayload ?? "(no payload)"
                );
            }
        }

        private IList<TradeSystemLink> BuildSystemLinksForTrade(MessageIn source, Trade trade)
        {
            var now = DateTime.UtcNow;
            var links = new List<TradeSystemLink>();

            const string stpModeManual = "MANUAL";
            const string stpModeAuto = "AUTO";

            if (trade.ProductType == ProductType.OptionVanilla ||
                trade.ProductType == ProductType.OptionNdo)
            {
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

            if (trade.ProductType == ProductType.Spot ||
                trade.ProductType == ProductType.Fwd)
            {
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

        private bool? GetStpFlagForVenue(string sourceVenueCode)
        {
            if (string.IsNullOrWhiteSpace(sourceVenueCode))
                return null;

            return StpEligibleVenues.Contains(sourceVenueCode) ? true : (bool?)null;
        }

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

        /// <summary>
        /// Bygger en beskrivning av filnamn/meddelandetyp för email-notifieringar.
        /// </summary>
        private string BuildFileNameDescription(MessageIn source)
        {
            // För EMAIL: använd SourceMessageKey som ofta är filnamnet
            if (source.SourceType != null &&
                source.SourceType.Equals("EMAIL", StringComparison.OrdinalIgnoreCase))
            {
                return source.SourceMessageKey ?? "(no filename)";
            }

            // För FIX: använd FIX message type + venue
            if (source.SourceType != null &&
                source.SourceType.Equals("FIX", StringComparison.OrdinalIgnoreCase))
            {
                return $"FIX {source.FixMsgType ?? "?"} from {source.SourceVenueCode ?? "?"}";
            }

            return source.SourceMessageKey ?? $"MessageInId={source.MessageInId}";
        }
    }
}
