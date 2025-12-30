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


    }
}
