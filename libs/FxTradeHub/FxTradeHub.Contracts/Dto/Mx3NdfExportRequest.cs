using System;

namespace FxTradeHub.Contracts.Dtos
{
    /// <summary>
    /// Request för export av NDF (Non-Deliverable Forward) till MX3.
    /// </summary>
    public sealed class Mx3NdfExportRequest
    {
        public string TradeId { get; set; }
        public long StpTradeId { get; set; }
        public string Trader { get; set; }
        public string Portfolio { get; set; }
        public string CurrencyPair { get; set; }
        public string BuySell { get; set; }
        public decimal Rate { get; set; }
        public DateTime TradeDate { get; set; }
        public DateTime SettlementDate { get; set; }
        public DateTime FixingDate { get; set; }
        public decimal Notional { get; set; }
        public string NotionalCurrency { get; set; }  
        public string SettlementCurrency { get; set; }
        public string FixingSource { get; set; }
        public string Counterparty { get; set; }  

        // MiFID fields
        public string Mic { get; set; }
        public string Isin { get; set; }
        public string InvId { get; set; }
        public string ReportingEntityId { get; set; }
        public DateTime? ExecutionTimeUtc { get; set; }

        // Sales margin
        public decimal? Margin { get; set; }
    }
}
