using FxTradeHub.Domain.Entities;

namespace FxTradeHub.Services.Notifications
{
    /// <summary>
    /// Tjänst för att skicka notifikationer vid lyckad/misslyckad
    /// parsing av inkommande meddelanden (email, FIX, etc.)
    /// </summary>
    public interface IMessageInNotificationService
    {
        /// <summary>
        /// Kallas när ett inkommande meddelande parsades och sparades i MessageIn.
        /// </summary>
        void NotifyMessageInSuccess(MessageIn entity);

        /// <summary>
        /// Kallas när parsing eller skrivning av MessageIn misslyckades.
        /// </summary>
        /// <param name="venueCode">Venue (VOLBROKER, JPM, BARX, etc.)</param>
        /// <param name="messageType">Meddelandetyp (EMAIL, FIX, etc.)</param>
        /// <param name="sourceMessageKey">Unik nyckel om tillgänglig</param>
        /// <param name="fileName">Filnamn för email-attachments</param>
        /// <param name="errorMessage">Felbeskrivning</param>
        /// <param name="rawPayload">Rå meddelande för debugging</param>
        void NotifyMessageInFailure(
            string venueCode,
            string messageType,
            string sourceMessageKey,
            string fileName,
            string errorMessage,
            string rawPayload);
    }
}
