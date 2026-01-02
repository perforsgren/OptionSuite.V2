using System;

namespace FxTradeHub.Contracts.Dtos
{
    /// <summary>
    /// D4.2a: Request-objekt för att exportera en option-trade till MX3 XML-format.
    /// Innehåller alla fält som behövs för att bygga en komplett MX3 booking XML-fil.
    /// </summary>
    public sealed class Mx3OptionExportRequest
    {
        // Trade identifiers
        public string TradeId { get; set; }
        public long StpTradeId { get; set; }

        // Trade basic info
        public string Trader { get; set; }
        public string Portfolio { get; set; }
        public string CurrencyPair { get; set; }

        // Option details
        public decimal Strike { get; set; }
        public string BuySell { get; set; }
        public string CallPut { get; set; }
        public string Cut { get; set; }

        // Dates
        public DateTime TradeDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public DateTime SettlementDate { get; set; }
        public DateTime PremiumDate { get; set; }

        // Notionals
        public decimal Notional { get; set; }
        public string NotionalCurrency { get; set; }

        // Premium
        public decimal Premium { get; set; }
        public string PremiumCurrency { get; set; }

        // Counterpart
        public string Counterpart { get; set; }
        public string CounterpartId { get; set; }

        // Execution
        public string ExecutionTime { get; set; }

        // MiFID II fields
        public string ISIN { get; set; }
        public string MIC { get; set; }
        public string InvID { get; set; }
        public string TVTIC { get; set; }

        // Optional fields
        public decimal? Margin { get; set; }
        public string Broker { get; set; }
        public string ReportingEntity { get; set; }
    }
}
