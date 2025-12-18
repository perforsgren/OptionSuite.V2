using FxTradeHub.Domain.Entities;

namespace FxTradeHub.Domain.Parsing
{
    /// <summary>
    /// Orkestrerar parsingflödet för inkommande MessageIn:
    /// - hämtar meddelanden,
    /// - väljer rätt parser,
    /// - kör parsing,
    /// - persisterar Trade + TradeSystemLink + TradeWorkflowEvent.
    /// </summary>
    public interface IMessageInParserOrchestrator
    {
        /// <summary>
        /// Bearbetar en batch av oparsade meddelanden (ParsedFlag = false).
        /// </summary>
        void ProcessPendingMessages();

        /// <summary>
        /// Bearbetar ett enskilt inkommande meddelande identifierat via MessageInId.
        /// Standardbeteende: om ParsedFlag redan är satt så görs inget.
        /// </summary>
        /// <param name="messageInId">Primärnyckeln för MessageIn-posten som ska bearbetas.</param>
        void ProcessMessage(long messageInId);

        /// <summary>
        /// Försöker parsa om ett MessageIn även om det redan är markerat som behandlat.
        /// Avsett för test eller när man korrigerat lookup/mapping och vill göra ett nytt försök.
        /// </summary>
        /// <param name="messageInId">Primärnyckeln för MessageIn-posten som ska bearbetas.</param>
        void ReprocessMessage(long messageInId);
    }
}
