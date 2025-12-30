using System.Collections.Generic;
using System.Threading.Tasks;
using FxTradeHub.Contracts.Dtos;

namespace FxTradeHub.Services
{
    public interface IBlotterTradeDetailsReadServiceAsync
    {
        Task<IList<TradeSystemLinkRow>> GetSystemLinksAsync(string tradeId);
        Task<IList<TradeWorkflowEventRow>> GetLatestWorkflowEventsAsync(string tradeId, int maxRows);
    }
}
