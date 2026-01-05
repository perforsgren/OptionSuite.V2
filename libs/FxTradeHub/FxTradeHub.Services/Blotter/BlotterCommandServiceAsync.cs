using System;
using System.Threading.Tasks;
using FxTradeHub.Contracts.Dtos;
using FxTradeHub.Data.MySql;
using FxTradeHub.Data.MySql.Repositories;
using FxTradeHub.Services.Mx3Export;

namespace FxTradeHub.Services.Blotter
{
    /// <summary>
    /// D4.2a: Command Service för write-operationer i blottern.
    /// Hanterar bokning till MX3/Calypso med XML-export + DB-uppdateringar.
    /// </summary>
    public sealed class BlotterCommandServiceAsync : IBlotterCommandServiceAsync
    {
        private readonly MySqlStpRepositoryAsync _repository;
        private readonly Mx3OptionExportService _mx3ExportService;

        public BlotterCommandServiceAsync(MySqlStpRepositoryAsync repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mx3ExportService = new Mx3OptionExportService();
        }

        /// <summary>
        /// D4.2a: Bokar en option trade till MX3.
        /// 
        /// Workflow:
        /// 1. Hämta trade från DB
        /// 2. Bygg Mx3OptionExportRequest
        /// 3. Skapa XML-fil
        /// 4. Uppdatera TradeSystemLink: Status = PENDING
        /// 5. Skapa TradeWorkflowEvent: Mx3BookingRequested
        /// </summary>
        public async Task<BookTradeResult> BookOptionToMx3Async(long stpTradeId)
        {
            try
            {
                // 1. Hämta trade från DB
                // TODO: Implementera GetTradeByIdAsync i repository (D4.2b)
                var trade = await _repository.GetTradeByIdAsync(stpTradeId).ConfigureAwait(false);

                if (trade == null)
                {
                    return new BookTradeResult
                    {
                        Success = false,
                        ErrorMessage = $"Trade with StpTradeId={stpTradeId} not found"
                    };
                }

                // 2. Bygg Mx3OptionExportRequest från trade data
                var exportRequest = MapToExportRequest(trade);

                // 3. Skapa XML-fil
                var exportResult = _mx3ExportService.CreateXmlFile(exportRequest);

                if (!exportResult.Success)
                {
                    return new BookTradeResult
                    {
                        Success = false,
                        ErrorMessage = $"XML export failed: {exportResult.ErrorMessage}"
                    };
                }

                // 4. Uppdatera TradeSystemLink: Status = PENDING
                //await _repository.UpdateTradeSystemLinkStatusAsync(
                //    stpTradeId: stpTradeId,
                //    systemCode: "MX3",
                //    status: "PENDING",
                //    lastError: null
                //).ConfigureAwait(false);

                await _repository.UpdateTradeSystemLinkOnBookingAsync(
                    stpTradeId: stpTradeId,
                    systemCode: "MX3",
                    bookedBy: Environment.UserName
                ).ConfigureAwait(false);

                // 5. Skapa TradeWorkflowEvent: Mx3BookingRequested
                await _repository.InsertTradeWorkflowEventAsync(
                    stpTradeId: stpTradeId,
                    eventType: "Mx3BookingRequested",
                    systemCode: "MX3",
                    userId: Environment.UserName,
                    details: $"XML file: {exportResult.FileName}, Portfolio: {trade.PortfolioMx3}"
                ).ConfigureAwait(false);

                return new BookTradeResult
                {
                    Success = true,
                    XmlFileName = exportResult.FileName
                };
            }
            catch (Exception ex)
            {
                return new BookTradeResult
                {
                    Success = false,
                    ErrorMessage = $"Book trade failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// D4.2a: Mappar BlotterTradeRow till Mx3OptionExportRequest.
        /// </summary>
        private Mx3OptionExportRequest MapToExportRequest(BlotterTradeRow trade)
        {
            return new Mx3OptionExportRequest
            {
                TradeId = trade.TradeId,
                StpTradeId = trade.StpTradeId,
                Trader = trade.TraderId,
                Portfolio = trade.PortfolioMx3,
                CurrencyPair = trade.CcyPair,
                Strike = trade.Strike ?? 0,
                BuySell = trade.BuySell,
                CallPut = trade.CallPut,
                Cut = trade.Cut,
                TradeDate = trade.TradeDate ?? DateTime.MinValue,
                ExpiryDate = trade.ExpiryDate ?? DateTime.MinValue,
                SettlementDate = trade.SettlementDate ?? DateTime.MinValue,
                PremiumDate = trade.PremiumDate ?? DateTime.MinValue,
                Notional = trade.Notional,
                NotionalCurrency = trade.NotionalCcy,
                Premium = trade.Premium ?? 0,
                PremiumCurrency = trade.PremiumCcy,
                Counterpart = trade.CounterpartyCode, // TODO: Finns det longname ID-fält?
                CounterpartId = trade.CounterpartyCode, 
                ExecutionTime = trade.ExecutionTimeUtc?.ToString("yyyy-MM-dd HH:mm:ss:fff"),
                ISIN = trade.Isin,
                MIC = trade.Mic,
                InvID = trade.InvId,
                TVTIC = trade.Tvtic,
                Margin = trade.Margin,
                Broker = trade.BrokerCode,
                ReportingEntity = trade.ReportingEntityId
            };
        }

    }
}
