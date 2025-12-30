using System;

namespace FxTradeHub.Contracts.Dtos
{
    public sealed class TradeSystemLinkRow
    {
        public long SystemLinkId { get; set; }
        public string TradeId { get; set; }          // STP TradeId (string)
        public string SystemCode { get; set; }       // "MX3", "CALYPSO", ...

        public string Status { get; set; }           // "NEW", "PENDING", "BOOKED", ...
        public string SystemTradeId { get; set; }    // externa id:t i target-systemet

        public DateTime? LastStatusUtc { get; set; }
        public string LastError { get; set; }

        public string PortfolioCode { get; set; }
        public bool? BookFlag { get; set; }
        public string StpMode { get; set; }

        public string ImportedBy { get; set; }
        public string BookedBy { get; set; }
        public DateTime? FirstBookedUtc { get; set; }
        public DateTime? LastBookedUtc { get; set; }

        public bool? StpFlag { get; set; }
        public DateTime? SystemCreatedUtc { get; set; }

        public bool IsDeleted { get; set; }
    }
}
