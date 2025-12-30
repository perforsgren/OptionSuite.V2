using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FxTradeHub.Contracts.Dtos;
using FxTradeHub.Domain.Interfaces;

namespace FxTradeHub.Services
{
    public sealed class BlotterTradeDetailsReadServiceAsync : IBlotterTradeDetailsReadServiceAsync
    {
        private readonly IStpRepositoryAsync _repo;

        public BlotterTradeDetailsReadServiceAsync(IStpRepositoryAsync repo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        public Task<IList<TradeSystemLinkRow>> GetSystemLinksAsync(string tradeId)
        {
            // D1.2: koppla mot repo-metod
            return Task.FromResult<IList<TradeSystemLinkRow>>(new List<TradeSystemLinkRow>());
        }

        public Task<IList<TradeWorkflowEventRow>> GetLatestWorkflowEventsAsync(string tradeId, int maxRows)
        {
            // D1.2: koppla mot repo-metod
            return Task.FromResult<IList<TradeWorkflowEventRow>>(new List<TradeWorkflowEventRow>());
        }
    }
}
