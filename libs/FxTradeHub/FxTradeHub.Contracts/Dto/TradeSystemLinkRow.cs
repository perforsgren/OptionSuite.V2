using System;

namespace FxTradeHub.Contracts.Dtos
{
    public sealed class TradeSystemLinkRow
    {
        public long SystemLinkId { get; set; }
        public long StpTradeId { get; set; }
        public string SystemCode { get; set; }
        public string SystemTradeId { get; set; }
        public string Status { get; set; }
        public DateTime? LastStatusUtc { get; set; }
        public string LastError { get; set; }
        public DateTime? CreatedUtc { get; set; }
        public string PortfolioCode { get; set; }
        public bool? BookFlag { get; set; }
        public string StpMode { get; set; }
        public string ImportedBy { get; set; }
        public string BookedBy { get; set; }
        public DateTime? FirstBookedUtc { get; set; }
        public DateTime? LastBookedUtc { get; set; }
        public bool? StpFlag { get; set; }
        public bool IsDeleted { get; set; }
    }
}
