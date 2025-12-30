using System;

namespace FxTradeHub.Contracts.Dtos
{
    public sealed class TradeWorkflowEventRow
    {
        public long WorkflowEventId { get; set; }
        public long StpTradeId { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string EventType { get; set; }
        public string SystemCode { get; set; }
        public string UserId { get; set; }
        public string Details { get; set; }
    }
}
