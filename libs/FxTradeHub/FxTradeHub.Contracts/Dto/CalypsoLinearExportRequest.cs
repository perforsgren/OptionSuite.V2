// libs/FxTradeHub/FxTradeHub.Contracts/Dto/CalypsoLinearExportRequest.cs

using System;

namespace FxTradeHub.Contracts.Dtos
{
    /// <summary>
    /// Request för att exportera linear trade till Calypso CSV-format.
    /// </summary>
    public sealed class CalypsoLinearExportRequest
    {
        // Trade identifiers
        public string TradeId { get; set; }
        public long StpTradeId { get; set; }

        // Trade basic info
        public string Trader { get; set; }
        public string CalypsoBook { get; set; }
        public string CurrencyPair { get; set; }
        public string Counterparty { get; set; }

        // Linear details
        public string BuySell { get; set; }
        public decimal Rate { get; set; }

        // Product type
        public string ProductType { get; set; } // "Spot" or "Forward"

        // Dates
        public DateTime TradeDate { get; set; }
        public DateTime SettlementDate { get; set; }
        public DateTime ExecutionTimeUtc { get; set; }

        // Notional
        public decimal Notional { get; set; }

        // STP flag (from trade_stp.tradesystemlink)
        public bool StpFlag { get; set; }

        // EMIR fields (only for Forwards)
        public string Mic { get; set; }
        public string Tvtic { get; set; }
        public string Uti { get; set; }
        public string Isin { get; set; }
        public string InvestorId { get; set; }
    }
}
