using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FxTradeHub.Contracts.Dtos;
using FxTradeHub.Domain.Entities;

namespace FxTradeHub.Domain.Interfaces
{
    /// <summary>
    /// Asynkron variant av IStpRepository för läsning av STP-readmodellen.
    /// Används främst av blotterns read-tjänster för att hämta TradeSystemSummary
    /// utan att blockera UI-tråden.
    /// </summary>
    public interface IStpRepositoryAsync
    {
        /// <summary>
        /// Hämtar en lista av TradeSystemSummary-rader baserat på filtreringsparametrar.
        /// Joinar Trade + TradeSystemLink på serversidan och filtrerar bort
        /// soft-deletade trades (Trade.IsDeleted = 0).
        /// </summary>
        /// <param name="fromTradeDate">Från och med trade-datum (inklusive), eller null för obegränsat.</param>
        /// <param name="toTradeDate">Till och med trade-datum (inklusive), eller null för obegränsat.</param>
        /// <param name="productType">Produktfilter (SPOT/FWD/SWAP/NDF/OPTION_VANILLA/OPTION_NDO), eller null/tomt för alla.</param>
        /// <param name="sourceType">Källtyp (t.ex. FIX/MAIL/MANUAL), eller null/tomt för alla.</param>
        /// <param name="sourceVenueCode">Venue-kod (t.ex. VOLBROKER, DESK), eller null/tomt för alla.</param>
        /// <param name="counterpartyCode">Motpartskod, eller null/tomt för alla.</param>
        /// <param name="traderId">Trader-id, eller null/tomt för alla.</param>
        /// <param name="currencyPair">Valutapar (t.ex. EURSEK), eller null/tomt för alla.</param>
        /// <param name="maxRows">Maximalt antal rader att returnera, eller null för obegränsat.</param>
        /// <param name="currentUserId">
        /// Aktuell användare (för logging/audit i senare versioner). Används inte i v1
        /// men exponeras här för framtida utbyggnad.
        /// </param>
        /// <returns>
        /// En asynkront hämtad lista med TradeSystemSummary-rader som matchar filtret.
        /// </returns>
        Task<IList<TradeSystemSummary>> GetTradeSystemSummariesAsync(
            DateTime? fromTradeDate,
            DateTime? toTradeDate,
            string productType,
            string sourceType,
            string sourceVenueCode,
            string counterpartyCode,
            string traderId,
            string currencyPair,
            int? maxRows,
            string currentUserId);


        /// <summary>
        /// Hämtar samtliga systemlänkar (TradeSystemLink) för en given STP-trade.
        /// Läser från trade_stp.TradeSystemLink på StpTradeId och filtrerar bort IsDeleted=1.
        /// Sorterar deterministiskt (SystemCode).
        /// </summary>
        /// <param name="stpTradeId">Intern STP-trade-id (Trade.StpTradeId).</param>
        /// <returns>Lista med länkar för trade:n.</returns>
        Task<IReadOnlyList<TradeSystemLinkRow>> GetTradeSystemLinksAsync(long stpTradeId);


        /// <summary>
        /// Hämtar senaste workflow events för en given STP-trade.
        /// Läser från trade_stp.TradeWorkflowEvent på StpTradeId och sorterar nyast först.
        /// </summary>
        /// <param name="stpTradeId">Intern STP-trade-id (Trade.StpTradeId).</param>
        /// <param name="maxRows">Max antal events att returnera (typiskt 50).</param>
        /// <returns>Lista med events i fallande tid (nyast först).</returns>
        Task<IReadOnlyList<TradeWorkflowEventRow>> GetTradeWorkflowEventsAsync(long stpTradeId, int maxRows);



        /// <summary>
        /// D4.2b: Hämtar en komplett trade (BlotterTradeRow) baserat på StpTradeId.
        /// Används för att bygga Mx3OptionExportRequest vid bokning.
        /// </summary>
        /// <param name="stpTradeId">StpTradeId för traden</param>
        /// <returns>BlotterTradeRow eller null om inte funnen</returns>
        Task<BlotterTradeRow> GetTradeByIdAsync(long stpTradeId);

        /// <summary>
        /// D4.2b: Uppdaterar status för en TradeSystemLink.
        /// Används för att sätta Status=PENDING när bokning initieras.
        /// </summary>
        /// <param name="stpTradeId">StpTradeId</param>
        /// <param name="systemCode">Systemkod (t.ex. MX3, CALYPSO)</param>
        /// <param name="status">Ny status (t.ex. PENDING, BOOKED, ERROR)</param>
        /// <param name="lastError">Felmeddelande eller null om success</param>
        Task UpdateTradeSystemLinkStatusAsync(long stpTradeId, string systemCode, string status, string lastError);

        /// <summary>
        /// D4.2b: Skapar ett nytt TradeWorkflowEvent.
        /// Används för att logga Mx3BookingRequested, Mx3Booked, Mx3Error etc.
        /// </summary>
        /// <param name="stpTradeId">StpTradeId</param>
        /// <param name="eventType">Event-typ (t.ex. Mx3BookingRequested)</param>
        /// <param name="systemCode">Systemkod (t.ex. MX3)</param>
        /// <param name="userId">Användare som initierade eventet</param>
        /// <param name="details">Detaljer om eventet (t.ex. filnamn, portfolio)</param>
        Task InsertTradeWorkflowEventAsync(long stpTradeId, string eventType, string systemCode, string userId, string details);

        // ==========================================
        // LEADER ELECTION METHODS (D4.3)
        // ==========================================

        /// <summary>
        /// Uppdaterar presence (heartbeat) för denna blotter-instans.
        /// </summary>
        Task UpdatePresenceAsync(string nodeId, string userName, string machineName);

        /// <summary>
        /// Hämtar lista av användare som är online (LastSeen inom senaste 30 sek).
        /// </summary>
        Task<List<string>> GetOnlineUsersAsync();

        /// <summary>
        /// Hämtar master priority chain (ordning på vem som ska vara master).
        /// </summary>
        Task<List<string>> GetMasterPriorityAsync();

        /// <summary>
        /// Försöker ta master-locket för angiven kandidat.
        /// Returnerar true om vi fick locket, false annars.
        /// </summary>
        Task<bool> TryAcquireMasterLockAsync(string lockName, string candidateUser, string machineName);

        /// <summary>
        /// Hämtar nuvarande master från lock-tabellen.
        /// Returnerar null om ingen master eller låset har gått ut.
        /// </summary>
        Task<string> GetCurrentMasterAsync(string lockName);


    }
}
