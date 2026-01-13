using FxTradeHub.Contracts.Dtos;
using FxTradeHub.Data.MySql;
using FxTradeHub.Data.MySql.Repositories;
using FxTradeHub.Services.CalypsoExport;
using FxTradeHub.Services.Mx3Export;
using System;
using System.Threading.Tasks;

namespace FxTradeHub.Services.Blotter
{
    /// <summary>
    /// Command Service för write-operationer i blottern.
    /// Hanterar bokning till MX3/Calypso med XML-export + DB-uppdateringar.
    /// </summary>
    public sealed class BlotterCommandServiceAsync : IBlotterCommandServiceAsync
    {
        private readonly MySqlStpRepositoryAsync _repository;
        private readonly Mx3OptionExportService _mx3OptionExportService;
        private readonly Mx3LinearExportService _mx3LinearExportService;
        private readonly CalypsoLinearExportService _calypsoExportService;


        public BlotterCommandServiceAsync(MySqlStpRepositoryAsync repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mx3OptionExportService = new Mx3OptionExportService();
            _mx3LinearExportService = new Mx3LinearExportService();
            _calypsoExportService = new CalypsoLinearExportService();
        }

        /// <summary>
        /// Bokar en option trade till MX3.
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
                var trade = await _repository.GetTradeByIdAsync(stpTradeId).ConfigureAwait(false);

                if (trade == null)
                {
                    return new BookTradeResult
                    {
                        Success = false,
                        ErrorMessage = $"Trade with StpTradeId={stpTradeId} not found"
                    };
                }

                var exportRequest = MapToOptionExportRequest(trade);
                var exportResult = _mx3OptionExportService.CreateXmlFile(exportRequest);

                if (!exportResult.Success)
                {
                    return new BookTradeResult
                    {
                        Success = false,
                        ErrorMessage = $"XML export failed: {exportResult.ErrorMessage}"
                    };
                }

                await _repository.UpdateTradeSystemLinkOnBookingAsync(
                    stpTradeId: stpTradeId,
                    systemCode: "MX3",
                    bookedBy: Environment.UserName
                ).ConfigureAwait(false);

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
        /// Bokar en linear trade till MX3.
        /// </summary>
        public async Task<BookTradeResult> BookLinearToMx3Async(long stpTradeId)
        {
            try
            {
                var trade = await _repository.GetTradeByIdAsync(stpTradeId).ConfigureAwait(false);

                if (trade == null)
                {
                    return new BookTradeResult
                    {
                        Success = false,
                        ErrorMessage = $"Trade with StpTradeId={stpTradeId} not found"
                    };
                }

                var exportRequest = MapToMx3LinearExportRequest(trade);
                var exportResult = _mx3LinearExportService.CreateXmlFile(exportRequest);

                if (!exportResult.Success)
                {
                    return new BookTradeResult
                    {
                        Success = false,
                        ErrorMessage = $"MX3 XML export failed: {exportResult.ErrorMessage}"
                    };
                }

                await _repository.UpdateTradeSystemLinkOnBookingAsync(
                    stpTradeId: stpTradeId,
                    systemCode: "MX3",
                    bookedBy: Environment.UserName
                ).ConfigureAwait(false);

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
                    ErrorMessage = $"Book trade to MX3 failed: {ex.Message}"
                };
            }
        }

        public async Task<BookTradeResult> BookLinearToCalypsoAsync(long stpTradeId)
        {
            try
            {
                var trade = await _repository.GetTradeByIdAsync(stpTradeId).ConfigureAwait(false);

                if (trade == null)
                {
                    return new BookTradeResult
                    {
                        Success = false,
                        ErrorMessage = $"Trade with StpTradeId={stpTradeId} not found"
                    };
                }

                var exportRequest = MapToCalypsoExportRequest(trade);
                var exportResult = _calypsoExportService.CreateCsvFile(exportRequest);

                if (!exportResult.Success)
                {
                    return new BookTradeResult
                    {
                        Success = false,
                        ErrorMessage = $"CSV export failed: {exportResult.ErrorMessage}"
                    };
                }

                await _repository.UpdateTradeSystemLinkOnBookingAsync(
                    stpTradeId: stpTradeId,
                    systemCode: "CALYPSO",
                    bookedBy: Environment.UserName
                ).ConfigureAwait(false);

                await _repository.InsertTradeWorkflowEventAsync(
                    stpTradeId: stpTradeId,
                    eventType: "CalypsoBookingRequested",
                    systemCode: "CALYPSO",
                    userId: Environment.UserName,
                    details: $"CSV file: {exportResult.FileName}, Portfolio: {trade.CalypsoPortfolio}"
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
        /// Mappar BlotterTradeRow till Mx3OptionExportRequest.
        /// </summary>
        private Mx3OptionExportRequest MapToOptionExportRequest(BlotterTradeRow trade)
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
                Counterpart = trade.CounterpartyCode,
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

        /// <summary>
        /// Mappar BlotterTradeRow till Mx3LinearExportRequest.
        /// </summary>
        private Mx3LinearExportRequest MapToMx3LinearExportRequest(BlotterTradeRow trade)
        {
            return new Mx3LinearExportRequest
            {
                TradeId = trade.TradeId,
                StpTradeId = trade.StpTradeId,
                Trader = trade.TraderId,
                Portfolio = trade.PortfolioMx3,
                CurrencyPair = trade.CcyPair,
                BuySell = trade.BuySell,
                Rate = trade.HedgeRate ?? trade.SpotRate ?? 0,
                ProductType = DetermineProductType(trade.ProductType),
                TradeDate = trade.TradeDate ?? DateTime.UtcNow.Date,
                SettlementDate = trade.SettlementDate ?? DateTime.UtcNow.Date,
                Notional = trade.Notional,
                Counterpart = trade.CounterpartyCode,
                CounterpartId = trade.CounterpartyCode
            };
        }

        private CalypsoLinearExportRequest MapToCalypsoExportRequest(BlotterTradeRow trade)
        {
            return new CalypsoLinearExportRequest
            {
                TradeId = trade.TradeId,
                StpTradeId = trade.StpTradeId,
                Trader = trade.TraderId,
                CalypsoBook = trade.CalypsoPortfolio,
                CurrencyPair = trade.CcyPair,
                Counterparty = trade.CounterpartyCode,
                BuySell = trade.BuySell,
                Rate = trade.HedgeRate ?? trade.SpotRate ?? 0,
                ProductType = DetermineProductType(trade.ProductType),
                TradeDate = trade.TradeDate ?? DateTime.UtcNow.Date,
                SettlementDate = trade.SettlementDate ?? DateTime.UtcNow.Date,
                ExecutionTimeUtc = trade.ExecutionTimeUtc ?? DateTime.UtcNow,
                Notional = trade.Notional,
                StpFlag = trade.StpFlag ?? false,
                Mic = trade.Mic,
                Tvtic = trade.Tvtic,
                Uti = trade.Uti,
                Isin = trade.Isin,
                InvestorId = trade.InvId
            };
        }

        private string DetermineProductType(string productType)
        {
            if (string.IsNullOrWhiteSpace(productType))
                return "Spot";

            var upper = productType.ToUpperInvariant();
            if (upper.Contains("SPOT"))
                return "Spot";
            if (upper.Contains("FWD") || upper.Contains("FORWARD"))
                return "Forward";

            return "Spot"; // default
        }
    }
}