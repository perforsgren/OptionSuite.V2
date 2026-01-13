using System.Threading.Tasks;

namespace FxTradeHub.Services.Blotter
{
    /// <summary>
    /// D4.2a: Command Service för write-operationer i blottern.
    /// Separerad från read service enligt CQRS-pattern.
    /// </summary>
    public interface IBlotterCommandServiceAsync
    {
        /// <summary>
        /// D4.2a: Bokar en option trade till MX3.
        /// Skapar XML-fil, uppdaterar TradeSystemLink till PENDING, och loggar TradeWorkflowEvent.
        /// </summary>
        /// <param name="stpTradeId">StpTradeId för traden som ska bokas</param>
        /// <returns>Resultat med success/error</returns>
        Task<BookTradeResult> BookOptionToMx3Async(long stpTradeId);

        Task<BookTradeResult> BookLinearToCalypsoAsync(long stpTradeId);
    }

    /// <summary>
    /// D4.2a: Resultat från Book Trade-operation.
    /// </summary>
    public sealed class BookTradeResult
    {
        /// <summary>
        /// True om bokningen lyckades (XML skapad + DB uppdaterad).
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Filnamn för den skapade XML-filen (om Success = true).
        /// </summary>
        public string XmlFileName { get; set; }

        /// <summary>
        /// Felmeddelande om Success = false.
        /// </summary>
        public string ErrorMessage { get; set; }
    }
}
