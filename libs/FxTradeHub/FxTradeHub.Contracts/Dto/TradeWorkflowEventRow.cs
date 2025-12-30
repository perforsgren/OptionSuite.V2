using System;

namespace FxTradeHub.Contracts.Dtos
{
    public sealed class TradeWorkflowEventRow
    {
        public long WorkflowEventId { get; set; }
        public string TradeId { get; set; }

        public DateTime EventTimeUtc { get; set; }
        public string EventType { get; set; }     // t.ex. "INGESTED", "NORMALIZED", "MX3_STATUS_CHANGED"
        public string Message { get; set; }       // fri text
        public string CreatedBy { get; set; }     // user/service
    }
}
