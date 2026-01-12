// libs/FxTradeHub/FxTradeHub.Contracts/Dto/Mx3LinearExportRequest.cs

using System;

namespace FxTradeHub.Contracts.Dtos
{
    /// <summary>
    /// Request för att exportera linear trade (Spot/Forward) till MX3 XML-format.
    /// </summary>
    public sealed class Mx3LinearExportRequest
    {
        // Trade identifiers
        public string TradeId { get; set; }
        public long StpTradeId { get; set; }

        // Trade basic info
        public string Trader { get; set; }
        public string Portfolio { get; set; }
        public string CurrencyPair { get; set; }

        // Linear details
        public string BuySell { get; set; }
        public decimal Rate { get; set; }

        // Product type
        public string ProductType { get; set; } // "Spot" or "Forward"

        // Dates
        public DateTime TradeDate { get; set; }
        public DateTime SettlementDate { get; set; }

        // Notional
        public decimal Notional { get; set; }

        // Counterpart
        public string Counterpart { get; set; }
        public string CounterpartId { get; set; }
    }
}
