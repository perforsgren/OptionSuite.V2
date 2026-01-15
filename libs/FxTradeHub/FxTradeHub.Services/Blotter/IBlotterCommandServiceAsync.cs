using System.Threading.Tasks;

namespace FxTradeHub.Services.Blotter
{
    /// <summary>
    /// Command Service för write-operationer i blottern.
    /// Separerad från read service enligt CQRS-pattern.
    /// </summary>
    public interface IBlotterCommandServiceAsync
    {
        /// <summary>
        /// Bokar en option trade till MX3.
        /// Skapar XML-fil, uppdaterar TradeSystemLink till PENDING, och loggar TradeWorkflowEvent.
        /// </summary>
        /// <param name="stpTradeId">StpTradeId för traden som ska bokas</param>
        /// <returns>Resultat med success/error</returns>
        Task<BookTradeResult> BookOptionToMx3Async(long stpTradeId);

        /// <summary>
        /// Bokar en linear trade till Calypso.
        /// Skapar CSV-fil, uppdaterar TradeSystemLink till PENDING, och loggar TradeWorkflowEvent.
        /// </summary>
        Task<BookTradeResult> BookLinearToCalypsoAsync(long stpTradeId);

        /// <summary>
        /// Bokar en linear trade till MX3.
        /// Skapar XML-fil, uppdaterar TradeSystemLink till PENDING, och loggar TradeWorkflowEvent.
        /// </summary>
        Task<BookTradeResult> BookLinearToMx3Async(long stpTradeId);

        /// <summary>
        /// Bokar en NDF trade till MX3.
        /// Skapar XML-fil, uppdaterar TradeSystemLink till PENDING, och loggar TradeWorkflowEvent.
        /// </summary>
        Task<BookTradeResult> BookNdfToMx3Async(long stpTradeId);

        /// <summary>
        /// Uppdaterar routing-fält för en trade (inline editing från blotter).
        /// Uppdaterar Trade.PortfolioMx3 och/eller Trade.CalypsoBook.
        /// Skapar audit event TradeInlineEdited.
        /// </summary>
        /// <param name="stpTradeId">StpTradeId för traden</param>
        /// <param name="portfolioMx3">Nytt värde för PortfolioMx3, eller null för att inte ändra</param>
        /// <param name="calypsoBook">Nytt värde för CalypsoBook, eller null för att inte ändra</param>
        /// <param name="userId">Användare som gör ändringen</param>
        /// <returns>Task</returns>
        Task UpdateTradeRoutingFieldsAsync(long stpTradeId, string portfolioMx3, string calypsoBook, string userId);

    }

    /// <summary>
    /// Resultat från Book Trade-operation.
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