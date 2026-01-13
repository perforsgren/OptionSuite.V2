using System;

namespace FxTradeHub.Contracts.Dtos
{
    /// <summary>
    /// Lightweight DTO för PENDING trades vid startup reconciliation.
    /// Innehåller bara det som behövs för att bygga filnamn och processa responses.
    /// </summary>
    public sealed class PendingTradeSystemLink
    {
        /// <summary>
        /// Intern primärnyckel för traden.
        /// </summary>
        public long StpTradeId { get; set; }

        /// <summary>
        /// Externt trade-id (används i filnamn).
        /// </summary>
        public string TradeId { get; set; }

        /// <summary>
        /// Produkttyp (SPOT, FWD, OPTION_VANILLA, etc).
        /// Används för att bygga filnamn (FX_SPOT_ vs FX_FORWARD_).
        /// </summary>
        public string ProductType { get; set; }

        /// <summary>
        /// SystemCode (MX3, CALYPSO).
        /// </summary>
        public string SystemCode { get; set; }

        /// <summary>
        /// Senaste uppdateringstid för systemlänken.
        /// Kan användas för att identifiera "stuck" trades.
        /// </summary>
        public DateTime LastUpdatedUtc { get; set; }
    }
}