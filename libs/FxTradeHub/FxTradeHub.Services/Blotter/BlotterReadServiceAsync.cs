using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FxTradeHub.Contracts.Dtos;
using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Enums;
using FxTradeHub.Domain.Interfaces;

namespace FxTradeHub.Services
{
    /// <summary>
    /// Asynkron läs-tjänst mot STP-hubben för blottern.
    /// Använder IStpRepositoryAsync för att hämta TradeSystemSummary
    /// och mappar dessa till BlotterTradeRow DTOs för UI-lagret.
    /// </summary>
    public sealed class BlotterReadServiceAsync : IBlotterReadServiceAsync
    {
        private readonly IStpRepositoryAsync _repository;

        /// <summary>
        /// Skapar en ny instans av BlotterReadServiceAsync.
        /// </summary>
        /// <param name="repository">
        /// Asynkront STP-repository som kan läsa TradeSystemSummary-rader
        /// från databasen.
        /// </param>
        public BlotterReadServiceAsync(IStpRepositoryAsync repository)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            _repository = repository;
        }

        public async Task<List<BlotterTradeRow>> GetBlotterTradesAsync(BlotterFilter filter)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            var summaries = await _repository.GetTradeSystemSummariesAsync(
                filter.FromTradeDate,
                filter.ToTradeDate,
                filter.ProductType,
                filter.SourceType,
                null,
                filter.CounterpartyCode,
                filter.TraderId,
                null,
                filter.MaxRows,
                null)
                .ConfigureAwait(false);

            // v2: En rad per trade (pivot av systemlänkar)
            var rows = new List<BlotterTradeRow>();
            var byTradeId = new Dictionary<long, BlotterTradeRow>();

            foreach (var summary in summaries)
            {
                if (!byTradeId.TryGetValue(summary.StpTradeId, out var row))
                {
                    row = MapToBlotterRow(summary);
                    byTradeId.Add(summary.StpTradeId, row);
                    rows.Add(row);
                }

                // ----------------------------------------------------
                // Pivot: applicera systemfält för aktuell systemlänk
                // ----------------------------------------------------
                switch (summary.SystemCode)
                {
                    case SystemCode.Mx3:
                        row.Mx3TradeId = summary.SystemTradeId;
                        row.Mx3Status = summary.Status.ToString();

                        // Systemlänkens portfolio ska vinna över trade-nivåns fallback
                        if (!string.IsNullOrEmpty(summary.SystemPortfolioCode))
                        {
                            row.PortfolioMx3 = summary.SystemPortfolioCode;
                        }
                        break;

                    case SystemCode.Calypso:
                        row.CalypsoTradeId = summary.SystemTradeId;
                        row.CalypsoStatus = summary.Status.ToString();
                        row.CalypsoPortfolio = summary.SystemPortfolioCode;
                        break;

                    case SystemCode.VolbrokerStp:
                        // (V1) Inga pivotade Volbroker-kolumner i BlotterTradeRow ännu.
                        // Behåll här som hook för framtida UI (ACK-status, external refs etc.)
                        break;

                    case SystemCode.Rtns:
                        // (V1) Hook för RTNS/drop-copy vid behov.
                        break;
                }

                // ----------------------------------------------------
                // CanEdit: om någon systemrad medger edit så tillåt edit
                // (v1-regel). Alternativt kan vi senare låsa den till MX3.
                // ----------------------------------------------------
                if (!row.CanEdit)
                {
                    row.CanEdit = CalculateCanEdit(summary);
                }

                // ----------------------------------------------------
                // StpFlag: plocka upp från första systemlänken (MX3 eller Calypso)
                // ----------------------------------------------------
                if (!row.StpFlag.HasValue && summary.StpFlag.HasValue)
                {
                    row.StpFlag = summary.StpFlag.Value;
                }

                // ----------------------------------------------------
                // "Generella systemfält" i BlotterTradeRow blir nu mindre meningsfulla
                // när vi pivotar. Vi lämnar dem tomma, alternativt kan vi senare välja
                // att de speglar "primärt system" (t.ex. MX3).
                // ----------------------------------------------------
                row.SystemCode = string.Empty;
                row.Status = string.Empty;
                row.SystemStatus = string.Empty;

                row.SystemTradeId = string.Empty;
                row.ExternalTradeId = string.Empty;
                row.SystemLastStatusUtc = null;
                row.SystemLastError = string.Empty;
                row.SystemPortfolioCode = string.Empty;
                row.BookFlag = null;
                row.StpMode = string.Empty;
                row.ImportedBy = string.Empty;
                row.BookedBy = string.Empty;
                row.FirstBookedUtc = null;
                row.LastBookedUtc = null;
                //row.StpFlag = null;
                row.SystemCreatedUtc = null;
                row.SystemLinkIsDeleted = false;

                // LastChangeUtc på row kan sättas som max över alla systemrader (behåll max)
                if (row.LastChangeUtc == null || summary.LastChangeUtc > row.LastChangeUtc.Value)
                {
                    row.LastChangeUtc = summary.LastChangeUtc;
                }
            }

            return rows;
        }


        /// <summary>
        /// Hämtar systemlänkar (TradeSystemLink) för en specifik trade (via StpTradeId).
        /// </summary>
        public Task<IReadOnlyList<TradeSystemLinkRow>> GetTradeSystemLinksAsync(long stpTradeId)
        {
            if (stpTradeId <= 0)
            {
                return Task.FromResult<IReadOnlyList<TradeSystemLinkRow>>(
                    Array.Empty<TradeSystemLinkRow>());
            }

            return _repository.GetTradeSystemLinksAsync(stpTradeId);
        }

        /// <summary>
        /// Hämtar senaste workflow events (TradeWorkflowEvent) för en specifik trade (via StpTradeId).
        /// </summary>
        public Task<IReadOnlyList<TradeWorkflowEventRow>> GetTradeWorkflowEventsAsync(long stpTradeId, int maxRows)
        {
            if (stpTradeId <= 0)
            {
                return Task.FromResult<IReadOnlyList<TradeWorkflowEventRow>>(
                    Array.Empty<TradeWorkflowEventRow>());
            }

            if (maxRows <= 0)
            {
                maxRows = 50;
            }

            return _repository.GetTradeWorkflowEventsAsync(stpTradeId, maxRows);
        }

        /// <summary>
        /// Mappar en TradeSystemSummary till en blotter-rad på trade-nivå.
        /// Denna metod fyller endast trade-fält (inte systemlänk-fält),
        /// eftersom systemkolumner pivotas in separat.
        /// </summary>
        /// <param name="s">Sammanfattningsrad från STP-hubben.</param>
        /// <returns>En trade-nivå-rad för blottern.</returns>
        private static BlotterTradeRow MapToBlotterRow(TradeSystemSummary s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));

            var row = new BlotterTradeRow();

            // ----------------------------------------------------
            // Trade-nivå
            // ----------------------------------------------------
            row.StpTradeId = s.StpTradeId;

            row.TradeId = s.TradeId;
            row.ProductType = s.ProductType.ToString().ToUpperInvariant();

            row.SourceType = s.SourceType;
            row.SourceVenueCode = s.SourceVenueCode;

            row.CounterpartyCode = s.CounterpartyCode;
            row.BrokerCode = s.BrokerCode;
            row.TraderId = s.TraderId;
            row.InvId = s.InvId;
            row.ReportingEntityId = s.ReportingEntityId;

            row.CcyPair = s.CurrencyPair;
            row.Mic = s.Mic;
            row.Isin = s.Isin;

            row.TradeDate = s.TradeDate;
            row.ExecutionTimeUtc = s.ExecutionTimeUtc;
            row.BuySell = s.BuySell;

            row.Notional = s.Notional;
            row.NotionalCcy = s.NotionalCurrency;

            row.SettlementDate = s.SettlementDate;
            row.NearSettlementDate = s.NearSettlementDate;

            row.IsNonDeliverable = s.IsNonDeliverable;
            row.FixingDate = s.FixingDate;
            row.SettlementCcy = s.SettlementCurrency;

            row.Uti = s.Uti;
            row.Tvtic = s.Tvtic;
            row.Margin = s.Margin;

            row.HedgeRate = s.HedgeRate;
            row.SpotRate = s.SpotRate;
            row.SwapPoints = s.SwapPoints;
            row.HedgeType = s.HedgeType;

            row.CallPut = s.CallPut;
            row.Strike = s.Strike;
            row.ExpiryDate = s.ExpiryDate;
            row.Cut = s.Cut;

            row.Premium = s.Premium;
            row.PremiumCcy = s.PremiumCurrency;
            row.PremiumDate = s.PremiumDate;

            // Trade-nivå fallback för portfolio (kan ersättas av MX3-länkens portfolio)
            row.PortfolioMx3 = s.PortfolioMx3;

            row.TradeIsDeleted = s.TradeIsDeleted;
            row.TradeLastUpdatedUtc = s.TradeLastUpdatedUtc;
            row.TradeLastUpdatedBy = s.TradeLastUpdatedBy;

            row.LastChangeUtc = s.LastChangeUtc;

            // Initiera pivotade systemfält tomma
            row.Mx3TradeId = string.Empty;
            row.Mx3Status = string.Empty;
            row.CalypsoTradeId = string.Empty;
            row.CalypsoStatus = string.Empty;
            row.CalypsoPortfolio = string.Empty;

            // CanEdit beräknas på första summaryn, kan “OR:as” på senare summaries
            row.CanEdit = CalculateCanEdit(s);

            // SystemLink – generella fält lämnas tomma i pivot-läget
            row.SystemLinkId = 0;
            row.SystemCode = string.Empty;
            row.Status = string.Empty;
            row.SystemStatus = string.Empty;
            row.SystemTradeId = string.Empty;
            row.ExternalTradeId = string.Empty;
            row.SystemLastStatusUtc = null;
            row.SystemLastError = string.Empty;
            row.SystemPortfolioCode = string.Empty;
            row.BookFlag = null;
            row.StpMode = string.Empty;
            row.ImportedBy = string.Empty;
            row.BookedBy = string.Empty;
            row.FirstBookedUtc = null;
            row.LastBookedUtc = null;
            row.StpFlag = s.StpFlag;
            row.SystemCreatedUtc = null;
            row.SystemLinkIsDeleted = false;

            return row;
        }


        /// <summary>
        /// Bestämmer om en trade-rad ska vara editerbar i blottern
        /// baserat på status och delete-flaggor.
        /// v1: icke-deleterad trade + länk och status i "tidigt" läge.
        /// </summary>
        private static bool CalculateCanEdit(TradeSystemSummary summary)
        {
            if (summary == null)
            {
                return false;
            }

            // Om trade eller länk är soft-deletad: aldrig editerbar.
            if (summary.TradeIsDeleted || summary.SystemLinkIsDeleted)
            {
                return false;
            }

            // Enkel v1-regel: tillåt edit i NEW/PENDING/ERROR.
            switch (summary.Status)
            {
                case TradeSystemStatus.New:
                case TradeSystemStatus.Pending:
                case TradeSystemStatus.Error:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// D4.2c: Delegerar till repository för targeted refresh.
        /// </summary>
        public async Task<BlotterTradeRow> GetTradeByIdAsync(long stpTradeId)
        {
            return await _repository.GetTradeByIdAsync(stpTradeId).ConfigureAwait(false);
        }

    }
}
