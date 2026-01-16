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
        private readonly Mx3NdfExportService _mx3NdfExportService;
        private readonly CalypsoLinearExportService _calypsoExportService;

        public BlotterCommandServiceAsync(MySqlStpRepositoryAsync repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _mx3OptionExportService = new Mx3OptionExportService();
            _mx3LinearExportService = new Mx3LinearExportService();
            _mx3NdfExportService = new Mx3NdfExportService();
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

        public async Task UpdateTradeRoutingFieldsAsync(long stpTradeId, string portfolioMx3, string calypsoBook, string userId)
        {
            if (portfolioMx3 == null && calypsoBook == null)
            {
                // Nothing to update
                return;
            }

            // Hämta nuvarande trade för concurrency check
            var trade = await _repository.GetTradeByIdAsync(stpTradeId);
            if (trade == null)
            {
                throw new InvalidOperationException($"Trade {stpTradeId} not found");
            }

            // Update trade with optimistic concurrency
            var success = await _repository.UpdateTradeRoutingFieldsAsync(
                stpTradeId,
                portfolioMx3,
                calypsoBook,
                trade.TradeLastUpdatedUtc ?? DateTime.UtcNow);

            if (!success)
            {
                throw new InvalidOperationException("Concurrency conflict - trade was updated by another user");
            }

            // ✅ NY: Uppdatera TradeSystemLink.PortfolioCode för respektive system
            if (portfolioMx3 != null)
            {
                await _repository.UpdateTradeSystemLinkPortfolioCodeAsync(stpTradeId, "MX3", portfolioMx3);
            }

            if (calypsoBook != null)
            {
                await _repository.UpdateTradeSystemLinkPortfolioCodeAsync(stpTradeId, "CALYPSO", calypsoBook);
            }

            // Audit event
            var changedFields = new System.Text.StringBuilder();
            if (portfolioMx3 != null)
            {
                changedFields.Append($"PortfolioMx3: {trade.PortfolioMx3} → {portfolioMx3}");
            }
            if (calypsoBook != null)
            {
                if (changedFields.Length > 0)
                    changedFields.Append("; ");
                changedFields.Append($"CalypsoBook: {trade.CalypsoPortfolio} → {calypsoBook}");
            }

            await _repository.InsertTradeWorkflowEventAsync(
                stpTradeId,
                eventType: "TradeInlineEdited",
                systemCode: "BLOTTER",
                userId: userId,
                details: changedFields.ToString());
        }

        /// <summary>
        /// Updates an existing trade with new values.
        /// Only allowed for trades with Status = New or Error.
        /// Currently only updates routing fields (portfolioMx3 and calypsoBook).
        /// Full trade editing will be implemented later.
        /// </summary>
        public async Task UpdateTradeAsync(
            long stpTradeId,
            string counterpartyCode,
            string currencyPair,
            string buySell,
            decimal notional,
            string notionalCurrency,
            decimal? hedgeRate,
            DateTime? settlementDate,
            string callPut,
            decimal? strike,
            DateTime? expiryDate,
            decimal? premium,
            string premiumCurrency,
            string portfolioMx3,
            string calypsoBook,
            string userId)
        {
            // For now, only update routing fields
            // Full trade editing will be added when repository methods are implemented
            await UpdateTradeRoutingFieldsAsync(stpTradeId, portfolioMx3, calypsoBook, userId);

            // TODO: Implement full trade update when repository supports it
            // This would include updating:
            // - counterpartyCode, currencyPair, buySell, notional, notionalCurrency
            // - hedgeRate, settlementDate
            // - option fields: callPut, strike, expiryDate, premium, premiumCurrency
        }

        /// <summary>
        /// Creates a new trade based on an existing trade (duplicate).
        /// The new trade will have:
        /// - New StpTradeId (auto-generated)
        /// - New TradeId (original TradeId + "_DUP_" + timestamp)
        /// - Status = "New"
        /// - All other fields copied from source trade
        /// - Updated routing fields if provided
        /// </summary>
        public async Task<DuplicateTradeResult> DuplicateTradeAsync(
            long sourceStpTradeId,
            string portfolioMx3,
            string calypsoBook,
            string userId)
        {
            try
            {
                // 1. Hämta original trade
                var sourceTrade = await _repository.GetTradeByIdAsync(sourceStpTradeId).ConfigureAwait(false);

                if (sourceTrade == null)
                {
                    return new DuplicateTradeResult
                    {
                        Success = false,
                        ErrorMessage = $"Source trade {sourceStpTradeId} not found"
                    };
                }

                // 2. Generera nytt TradeId
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var newTradeId = $"{sourceTrade.TradeId}_DUP_{timestamp}";

                // 3. Skapa ny trade (kopierar alla fält från source)
                // TODO: Implementera repository.DuplicateTradeAsync när det finns
                // För nu returnerar vi ett placeholder-resultat

                // Placeholder implementation - returnera framgång men ingen riktig duplicate skapas än
                await _repository.InsertTradeWorkflowEventAsync(
                    sourceStpTradeId,
                    eventType: "TradeDuplicationAttempted",
                    systemCode: "BLOTTER",
                    userId: userId,
                    details: $"Attempted to duplicate as {newTradeId}. Full duplicate not yet implemented."
                ).ConfigureAwait(false);

                return new DuplicateTradeResult
                {
                    Success = false,
                    ErrorMessage = "Duplicate trade not yet fully implemented. Repository method needed.",
                    NewStpTradeId = 0,
                    NewTradeId = newTradeId
                };

                // TODO: När repository.DuplicateTradeAsync finns:
                // var newStpTradeId = await _repository.DuplicateTradeAsync(
                //     sourceStpTradeId,
                //     newTradeId,
                //     portfolioMx3 ?? sourceTrade.PortfolioMx3,
                //     calypsoBook ?? sourceTrade.CalypsoPortfolio,
                //     userId
                // ).ConfigureAwait(false);
                //
                // return new DuplicateTradeResult
                // {
                //     Success = true,
                //     NewStpTradeId = newStpTradeId,
                //     NewTradeId = newTradeId
                // };
            }
            catch (Exception ex)
            {
                return new DuplicateTradeResult
                {
                    Success = false,
                    ErrorMessage = $"Duplicate failed: {ex.Message}"
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

        /// <summary>
        /// Bokar en NDF trade till MX3.
        /// </summary>
        public async Task<BookTradeResult> BookNdfToMx3Async(long stpTradeId)
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

                var exportRequest = MapToMx3NdfExportRequest(trade);
                var exportResult = _mx3NdfExportService.CreateXmlFile(exportRequest);

                if (!exportResult.Success)
                {
                    return new BookTradeResult
                    {
                        Success = false,
                        ErrorMessage = $"MX3 NDF XML export failed: {exportResult.ErrorMessage}"
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
                    ErrorMessage = $"Book NDF to MX3 failed: {ex.Message}"
                };
            }
        }

        private Mx3NdfExportRequest MapToMx3NdfExportRequest(BlotterTradeRow trade)
        {
            return new Mx3NdfExportRequest
            {
                TradeId = trade.TradeId,
                StpTradeId = trade.StpTradeId,
                Trader = trade.TraderId,
                Portfolio = trade.PortfolioMx3,
                CurrencyPair = trade.CcyPair,
                BuySell = trade.BuySell,
                Rate = trade.HedgeRate ?? 0,
                TradeDate = trade.TradeDate ?? DateTime.UtcNow.Date,
                SettlementDate = trade.SettlementDate ?? DateTime.UtcNow.Date,
                FixingDate = trade.FixingDate ?? trade.SettlementDate ?? DateTime.UtcNow.Date,
                Notional = trade.Notional,
                NotionalCurrency = trade.NotionalCcy,
                SettlementCurrency = string.IsNullOrEmpty(trade.SettlementCcy) ? "USD" : trade.SettlementCcy,
                FixingSource = "NDF_group",
                Counterparty = trade.CounterpartyCode,

                // MiFID fields
                Mic = trade.Mic,
                Isin = trade.Isin,
                InvId = trade.InvId,
                ReportingEntityId = trade.ReportingEntityId,
                ExecutionTimeUtc = trade.ExecutionTimeUtc,

                // Sales margin
                Margin = trade.Margin
            };
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
            // För linear trades: använd SpotRate för Spot, HedgeRate för Forward
            var productType = DetermineProductType(trade.ProductType);
            var rate = productType == "Spot"
                ? (trade.SpotRate ?? trade.HedgeRate ?? 0)
                : (trade.HedgeRate ?? trade.SpotRate ?? 0);

            return new Mx3LinearExportRequest
            {
                TradeId = trade.TradeId,
                StpTradeId = trade.StpTradeId,
                Trader = trade.TraderId,
                Portfolio = trade.PortfolioMx3,
                CurrencyPair = trade.CcyPair,
                BuySell = trade.BuySell,
                Rate = rate,
                ProductType = productType,
                TradeDate = trade.TradeDate ?? DateTime.UtcNow.Date,
                SettlementDate = trade.SettlementDate ?? DateTime.UtcNow.Date,
                Notional = trade.Notional,
                Counterpart = trade.CounterpartyCode,
                CounterpartId = trade.CounterpartyCode
            };
        }

        private CalypsoLinearExportRequest MapToCalypsoExportRequest(BlotterTradeRow trade)
        {
            // För linear trades: använd SpotRate för Spot, HedgeRate för Forward
            var productType = DetermineProductType(trade.ProductType);
            var rate = productType == "Spot"
                ? (trade.SpotRate ?? trade.HedgeRate ?? 0)
                : (trade.HedgeRate ?? trade.SpotRate ?? 0);

            return new CalypsoLinearExportRequest
            {
                TradeId = trade.TradeId,
                StpTradeId = trade.StpTradeId,
                Trader = trade.TraderId,
                CalypsoBook = trade.CalypsoPortfolio,
                CurrencyPair = trade.CcyPair,
                Counterparty = trade.CounterpartyCode,
                BuySell = trade.BuySell,
                Rate = rate,
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