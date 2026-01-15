using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using FxTradeHub.Contracts.Dtos;
using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Enums;
using FxTradeHub.Domain.Interfaces;
using MySql.Data.MySqlClient;

namespace FxTradeHub.Data.MySql.Repositories
{
    /// <summary>
    /// Asynkron MySQL-implementation av IStpRepositoryAsync mot schemat trade_stp.
    /// Använder riktiga async-anrop (OpenAsync/ExecuteReaderAsync/ReadAsync)
    /// för läsning av TradeSystemSummary-readmodellen.
    /// </summary>
    public sealed class MySqlStpRepositoryAsync : IStpRepositoryAsync
    {
        private readonly string _connectionString;

        /// <summary>
        /// Skapar ett nytt asynkront repository med given connection string.
        /// </summary>
        /// <param name="connectionString">Connection string mot trade_stp.</param>
        public MySqlStpRepositoryAsync(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("Connection string must not be empty.", "connectionString");

            _connectionString = connectionString;
        }

        /// <summary>
        /// Skapar en ny MySqlConnection. Används internt per operation.
        /// </summary>
        /// <returns>Ny MySqlConnection instans.</returns>
        private MySqlConnection CreateConnection()
        {
            return new MySqlConnection(_connectionString);
        }

        /// <summary>
        /// Asynkront hämtar en lista av TradeSystemSummary-rader baserat på filtreringsparametrar.
        /// Joinar Trade + TradeSystemLink på serversidan och filtrerar bort soft-deletade
        /// trades (Trade.IsDeleted = 0). Sorterar på TradeDate DESC, StpTradeId DESC, SystemCode ASC
        /// och begränsar antalet rader med LIMIT om maxRows är satt.
        /// </summary>
        public async Task<IList<TradeSystemSummary>> GetTradeSystemSummariesAsync(
            DateTime? fromTradeDate,
            DateTime? toTradeDate,
            string productType,
            string sourceType,
            string sourceVenueCode,
            string counterpartyCode,
            string traderId,
            string currencyPair,
            int? maxRows,
            string currentUserId)
        {
            var results = new List<TradeSystemSummary>();

            using (var connection = CreateConnection())
            {
                await connection.OpenAsync().ConfigureAwait(false);

                var sqlBuilder = new System.Text.StringBuilder();
                sqlBuilder.Append(@"
                                    SELECT
                                        t.StpTradeId,
                                        t.TradeId,
                                        t.ProductType,
                                        t.SourceType,
                                        t.SourceVenueCode,
                                        t.CounterpartyCode,
                                        t.BrokerCode,
                                        t.TraderId,
                                        t.InvId,
                                        t.ReportingEntityId,
                                        t.CurrencyPair,
                                        t.Mic,
                                        t.Isin,
                                        t.TradeDate,
                                        t.ExecutionTimeUtc,
                                        t.BuySell,
                                        t.Notional,
                                        t.NotionalCurrency,
                                        t.SettlementDate,
                                        t.NearSettlementDate,
                                        t.IsNonDeliverable,
                                        t.FixingDate,
                                        t.SettlementCurrency,
                                        t.Uti,
                                        t.Tvtic,
                                        t.Margin,
                                        t.HedgeRate,
                                        t.SpotRate,
                                        t.SwapPoints,
                                        t.HedgeType,
                                        t.CallPut,
                                        t.Strike,
                                        t.ExpiryDate,
                                        t.Cut,
                                        t.Premium,
                                        t.PremiumCurrency,
                                        t.PremiumDate,
                                        t.PortfolioMx3,
                                        t.LastUpdatedUtc AS TradeLastUpdatedUtc,
                                        t.LastUpdatedBy AS TradeLastUpdatedBy,

                                        l.SystemLinkId,
                                        l.SystemCode,
                                        l.SystemTradeId,
                                        l.Status,
                                        l.LastStatusUtc,
                                        l.LastError,
                                        l.PortfolioCode,
                                        l.BookFlag,
                                        l.StpMode,
                                        l.ImportedBy,
                                        l.BookedBy,
                                        l.FirstBookedUtc,
                                        l.LastBookedUtc,
                                        l.StpFlag,
                                        l.CreatedUtc AS SystemCreatedUtc,
                                        l.IsDeleted AS SystemLinkIsDeleted
                                    FROM trade_stp.Trade t
                                    INNER JOIN trade_stp.TradeSystemLink l ON l.StpTradeId = t.StpTradeId
                                    WHERE t.IsDeleted = 0
                                    ");

                // Dynamiska filter – exakt samma logik som i den synkrona varianten.
                if (fromTradeDate.HasValue)
                {
                    sqlBuilder.Append("  AND t.TradeDate >= @FromTradeDate\n");
                }

                if (toTradeDate.HasValue)
                {
                    sqlBuilder.Append("  AND t.TradeDate <= @ToTradeDate\n");
                }

                if (!string.IsNullOrEmpty(productType))
                {
                    sqlBuilder.Append("  AND t.ProductType = @ProductType\n");
                }

                if (!string.IsNullOrEmpty(sourceType))
                {
                    sqlBuilder.Append("  AND t.SourceType = @SourceType\n");
                }

                if (!string.IsNullOrEmpty(counterpartyCode))
                {
                    sqlBuilder.Append("  AND t.CounterpartyCode = @CounterpartyCode\n");
                }

                if (!string.IsNullOrEmpty(traderId))
                {
                    sqlBuilder.Append("  AND t.TraderId = @TraderId\n");
                }

                // OBS: currencyPair och sourceVenueCode finns i signaturen men används inte i v1-SQL:en.
                // De kan läggas till som filter i en senare iteration om vi vill.

                // Sortering: senaste affärer först
                sqlBuilder.Append("ORDER BY t.TradeDate DESC, t.StpTradeId DESC, l.SystemCode ASC\n");

                // Limit
                if (maxRows.HasValue)
                {
                    sqlBuilder.Append("LIMIT @MaxRows");
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = sqlBuilder.ToString();
                    command.CommandType = CommandType.Text;

                    if (fromTradeDate.HasValue)
                    {
                        var p = command.Parameters.Add("@FromTradeDate", MySqlDbType.Date);
                        p.Value = fromTradeDate.Value;
                    }

                    if (toTradeDate.HasValue)
                    {
                        var p = command.Parameters.Add("@ToTradeDate", MySqlDbType.Date);
                        p.Value = toTradeDate.Value;
                    }

                    if (!string.IsNullOrEmpty(productType))
                    {
                        var p = command.Parameters.Add("@ProductType", MySqlDbType.VarChar, 30);
                        p.Value = productType;
                    }

                    if (!string.IsNullOrEmpty(sourceType))
                    {
                        var p = command.Parameters.Add("@SourceType", MySqlDbType.VarChar, 20);
                        p.Value = sourceType;
                    }

                    if (!string.IsNullOrEmpty(counterpartyCode))
                    {
                        var p = command.Parameters.Add("@CounterpartyCode", MySqlDbType.VarChar, 100);
                        p.Value = counterpartyCode;
                    }

                    if (!string.IsNullOrEmpty(traderId))
                    {
                        var p = command.Parameters.Add("@TraderId", MySqlDbType.VarChar, 50);
                        p.Value = traderId;
                    }

                    if (maxRows.HasValue)
                    {
                        var p = command.Parameters.Add("@MaxRows", MySqlDbType.Int32);
                        p.Value = maxRows.Value;
                    }

                    using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        // Slå upp ordningar en gång
                        int ordStpTradeId = reader.GetOrdinal("StpTradeId");
                        int ordTradeId = reader.GetOrdinal("TradeId");
                        int ordProductType = reader.GetOrdinal("ProductType");
                        int ordSourceType = reader.GetOrdinal("SourceType");
                        int ordSourceVenueCode = reader.GetOrdinal("SourceVenueCode");
                        int ordCounterpartyCode = reader.GetOrdinal("CounterpartyCode");
                        int ordBrokerCode = reader.GetOrdinal("BrokerCode");
                        int ordTraderId = reader.GetOrdinal("TraderId");
                        int ordInvId = reader.GetOrdinal("InvId");
                        int ordReportingEntityId = reader.GetOrdinal("ReportingEntityId");
                        int ordCurrencyPair = reader.GetOrdinal("CurrencyPair");
                        int ordMic = reader.GetOrdinal("Mic");
                        int ordIsin = reader.GetOrdinal("Isin");
                        int ordTradeDate = reader.GetOrdinal("TradeDate");
                        int ordExecutionTimeUtc = reader.GetOrdinal("ExecutionTimeUtc");
                        int ordBuySell = reader.GetOrdinal("BuySell");
                        int ordNotional = reader.GetOrdinal("Notional");
                        int ordNotionalCurrency = reader.GetOrdinal("NotionalCurrency");
                        int ordSettlementDate = reader.GetOrdinal("SettlementDate");
                        int ordNearSettlementDate = reader.GetOrdinal("NearSettlementDate");
                        int ordIsNonDeliverable = reader.GetOrdinal("IsNonDeliverable");
                        int ordFixingDate = reader.GetOrdinal("FixingDate");
                        int ordSettlementCurrency = reader.GetOrdinal("SettlementCurrency");
                        int ordUti = reader.GetOrdinal("Uti");
                        int ordTvtic = reader.GetOrdinal("Tvtic");
                        int ordMargin = reader.GetOrdinal("Margin");
                        int ordHedgeRate = reader.GetOrdinal("HedgeRate");
                        int ordSpotRate = reader.GetOrdinal("SpotRate");
                        int ordSwapPoints = reader.GetOrdinal("SwapPoints");
                        int ordHedgeType = reader.GetOrdinal("HedgeType");
                        int ordCallPut = reader.GetOrdinal("CallPut");
                        int ordStrike = reader.GetOrdinal("Strike");
                        int ordExpiryDate = reader.GetOrdinal("ExpiryDate");
                        int ordCut = reader.GetOrdinal("Cut");
                        int ordPremium = reader.GetOrdinal("Premium");
                        int ordPremiumCurrency = reader.GetOrdinal("PremiumCurrency");
                        int ordPremiumDate = reader.GetOrdinal("PremiumDate");
                        int ordPortfolioMx3 = reader.GetOrdinal("PortfolioMx3");
                        int ordTradeLastUpdatedUtc = reader.GetOrdinal("TradeLastUpdatedUtc");
                        int ordTradeLastUpdatedBy = reader.GetOrdinal("TradeLastUpdatedBy");

                        int ordSystemLinkId = reader.GetOrdinal("SystemLinkId");
                        int ordSystemCode = reader.GetOrdinal("SystemCode");
                        int ordSystemTradeId = reader.GetOrdinal("SystemTradeId");
                        int ordStatus = reader.GetOrdinal("Status");
                        int ordLastStatusUtc = reader.GetOrdinal("LastStatusUtc");
                        int ordLastError = reader.GetOrdinal("LastError");
                        int ordPortfolioCode = reader.GetOrdinal("PortfolioCode");
                        int ordBookFlag = reader.GetOrdinal("BookFlag");
                        int ordStpMode = reader.GetOrdinal("StpMode");
                        int ordImportedBy = reader.GetOrdinal("ImportedBy");
                        int ordBookedBy = reader.GetOrdinal("BookedBy");
                        int ordFirstBookedUtc = reader.GetOrdinal("FirstBookedUtc");
                        int ordLastBookedUtc = reader.GetOrdinal("LastBookedUtc");
                        int ordStpFlag = reader.GetOrdinal("StpFlag");
                        int ordSystemCreatedUtc = reader.GetOrdinal("SystemCreatedUtc");
                        int ordSystemLinkIsDeleted = reader.GetOrdinal("SystemLinkIsDeleted");

                        while (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            var summary = new TradeSystemSummary
                            {
                                StpTradeId = reader.GetInt64(ordStpTradeId),
                                TradeId = reader.GetString(ordTradeId),
                                ProductType = MapProductTypeFromDatabaseValue(reader.GetString(ordProductType)),
                                SourceType = reader.IsDBNull(ordSourceType) ? string.Empty : reader.GetString(ordSourceType),
                                SourceVenueCode = reader.IsDBNull(ordSourceVenueCode) ? string.Empty : reader.GetString(ordSourceVenueCode),
                                CounterpartyCode = reader.IsDBNull(ordCounterpartyCode) ? string.Empty : reader.GetString(ordCounterpartyCode),
                                BrokerCode = reader.IsDBNull(ordBrokerCode) ? string.Empty : reader.GetString(ordBrokerCode),
                                TraderId = reader.IsDBNull(ordTraderId) ? string.Empty : reader.GetString(ordTraderId),
                                InvId = reader.IsDBNull(ordInvId) ? string.Empty : reader.GetString(ordInvId),
                                ReportingEntityId = reader.IsDBNull(ordReportingEntityId) ? string.Empty : reader.GetString(ordReportingEntityId),
                                CurrencyPair = reader.IsDBNull(ordCurrencyPair) ? string.Empty : reader.GetString(ordCurrencyPair),
                                Mic = reader.IsDBNull(ordMic) ? string.Empty : reader.GetString(ordMic),
                                Isin = reader.IsDBNull(ordIsin) ? string.Empty : reader.GetString(ordIsin),
                                TradeDate = reader.GetDateTime(ordTradeDate),
                                ExecutionTimeUtc = reader.GetDateTime(ordExecutionTimeUtc),
                                BuySell = reader.IsDBNull(ordBuySell) ? string.Empty : reader.GetString(ordBuySell),
                                Notional = reader.GetDecimal(ordNotional),
                                NotionalCurrency = reader.IsDBNull(ordNotionalCurrency) ? string.Empty : reader.GetString(ordNotionalCurrency),
                                SettlementDate = reader.GetDateTime(ordSettlementDate),
                                NearSettlementDate = reader.IsDBNull(ordNearSettlementDate) ? (DateTime?)null : reader.GetDateTime(ordNearSettlementDate),
                                IsNonDeliverable = reader.IsDBNull(ordIsNonDeliverable) ? (bool?)null : reader.GetBoolean(ordIsNonDeliverable),
                                FixingDate = reader.IsDBNull(ordFixingDate) ? (DateTime?)null : reader.GetDateTime(ordFixingDate),
                                SettlementCurrency = reader.IsDBNull(ordSettlementCurrency) ? string.Empty : reader.GetString(ordSettlementCurrency),
                                Uti = reader.IsDBNull(ordUti) ? string.Empty : reader.GetString(ordUti),
                                Tvtic = reader.IsDBNull(ordTvtic) ? string.Empty : reader.GetString(ordTvtic),
                                Margin = reader.IsDBNull(ordMargin) ? (decimal?)null : reader.GetDecimal(ordMargin),
                                HedgeRate = reader.IsDBNull(ordHedgeRate) ? (decimal?)null : reader.GetDecimal(ordHedgeRate),
                                SpotRate = reader.IsDBNull(ordSpotRate) ? (decimal?)null : reader.GetDecimal(ordSpotRate),
                                SwapPoints = reader.IsDBNull(ordSwapPoints) ? (decimal?)null : reader.GetDecimal(ordSwapPoints),
                                HedgeType = reader.IsDBNull(ordHedgeType) ? string.Empty : reader.GetString(ordHedgeType),
                                CallPut = reader.IsDBNull(ordCallPut) ? string.Empty : reader.GetString(ordCallPut),
                                Strike = reader.IsDBNull(ordStrike) ? (decimal?)null : reader.GetDecimal(ordStrike),
                                ExpiryDate = reader.IsDBNull(ordExpiryDate) ? (DateTime?)null : reader.GetDateTime(ordExpiryDate),
                                Cut = reader.IsDBNull(ordCut) ? string.Empty : reader.GetString(ordCut),
                                Premium = reader.IsDBNull(ordPremium) ? (decimal?)null : reader.GetDecimal(ordPremium),
                                PremiumCurrency = reader.IsDBNull(ordPremiumCurrency) ? string.Empty : reader.GetString(ordPremiumCurrency),
                                PremiumDate = reader.IsDBNull(ordPremiumDate) ? (DateTime?)null : reader.GetDateTime(ordPremiumDate),
                                PortfolioMx3 = reader.IsDBNull(ordPortfolioMx3) ? string.Empty : reader.GetString(ordPortfolioMx3),
                                TradeLastUpdatedUtc = reader.GetDateTime(ordTradeLastUpdatedUtc),
                                TradeLastUpdatedBy = reader.IsDBNull(ordTradeLastUpdatedBy) ? string.Empty : reader.GetString(ordTradeLastUpdatedBy),

                                SystemLinkId = reader.GetInt64(ordSystemLinkId),
                                SystemCode = MapSystemCodeFromDatabaseValue(reader.GetString(ordSystemCode)),
                                Status = MapTradeSystemStatusFromDatabaseValue(reader.GetString(ordStatus)),
                                SystemTradeId = reader.IsDBNull(ordSystemTradeId) ? string.Empty : reader.GetString(ordSystemTradeId),
                                ExternalTradeId = reader.IsDBNull(ordSystemTradeId) ? string.Empty : reader.GetString(ordSystemTradeId),
                                SystemLastStatusUtc = reader.GetDateTime(ordLastStatusUtc),
                                SystemLastError = reader.IsDBNull(ordLastError) ? string.Empty : reader.GetString(ordLastError),
                                SystemPortfolioCode = reader.IsDBNull(ordPortfolioCode) ? string.Empty : reader.GetString(ordPortfolioCode),
                                BookFlag = reader.IsDBNull(ordBookFlag) ? (bool?)null : reader.GetBoolean(ordBookFlag),
                                StpMode = reader.IsDBNull(ordStpMode) ? string.Empty : reader.GetString(ordStpMode),
                                ImportedBy = reader.IsDBNull(ordImportedBy) ? string.Empty : reader.GetString(ordImportedBy),
                                BookedBy = reader.IsDBNull(ordBookedBy) ? string.Empty : reader.GetString(ordBookedBy),
                                FirstBookedUtc = reader.IsDBNull(ordFirstBookedUtc) ? (DateTime?)null : reader.GetDateTime(ordFirstBookedUtc),
                                LastBookedUtc = reader.IsDBNull(ordLastBookedUtc) ? (DateTime?)null : reader.GetDateTime(ordLastBookedUtc),
                                StpFlag = reader.IsDBNull(ordStpFlag) ? (bool?)null : reader.GetBoolean(ordStpFlag),
                                SystemCreatedUtc = reader.GetDateTime(ordSystemCreatedUtc),
                                SystemLinkIsDeleted = reader.GetBoolean(ordSystemLinkIsDeleted)
                            };

                            results.Add(summary);
                        }
                    }
                }
            }

            return results;
        }



        public async Task<IReadOnlyList<TradeSystemLinkRow>> GetTradeSystemLinksAsync(long stpTradeId)
        {
            const string sql = @"
                                SELECT
                                    tsl.SystemLinkId,
                                    tsl.StpTradeId,
                                    tsl.SystemCode,
                                    tsl.SystemTradeId,
                                    tsl.Status,
                                    tsl.LastStatusUtc,
                                    tsl.LastError,
                                    tsl.CreatedUtc,
                                    tsl.PortfolioCode,
                                    tsl.BookFlag,
                                    tsl.StpMode,
                                    tsl.ImportedBy,
                                    tsl.BookedBy,
                                    tsl.FirstBookedUtc,
                                    tsl.LastBookedUtc,
                                    tsl.StpFlag,
                                    tsl.IsDeleted
                                FROM trade_stp.TradeSystemLink tsl
                                WHERE tsl.StpTradeId = @stpTradeId
                                  AND tsl.IsDeleted = 0
                                ORDER BY tsl.SystemCode;
                                ";

            var rows = new List<TradeSystemLinkRow>();

            using (var conn = CreateConnection())
            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@stpTradeId", stpTradeId);

                await conn.OpenAsync().ConfigureAwait(false);

                using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    int ordSystemLinkId = reader.GetOrdinal("SystemLinkId");
                    int ordStpTradeId = reader.GetOrdinal("StpTradeId");
                    int ordSystemCode = reader.GetOrdinal("SystemCode");
                    int ordSystemTradeId = reader.GetOrdinal("SystemTradeId");
                    int ordStatus = reader.GetOrdinal("Status");
                    int ordLastStatusUtc = reader.GetOrdinal("LastStatusUtc");
                    int ordLastError = reader.GetOrdinal("LastError");
                    int ordCreatedUtc = reader.GetOrdinal("CreatedUtc");
                    int ordPortfolioCode = reader.GetOrdinal("PortfolioCode");
                    int ordBookFlag = reader.GetOrdinal("BookFlag");
                    int ordStpMode = reader.GetOrdinal("StpMode");
                    int ordImportedBy = reader.GetOrdinal("ImportedBy");
                    int ordBookedBy = reader.GetOrdinal("BookedBy");
                    int ordFirstBookedUtc = reader.GetOrdinal("FirstBookedUtc");
                    int ordLastBookedUtc = reader.GetOrdinal("LastBookedUtc");
                    int ordStpFlag = reader.GetOrdinal("StpFlag");
                    int ordIsDeleted = reader.GetOrdinal("IsDeleted");

                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        rows.Add(new TradeSystemLinkRow
                        {
                            SystemLinkId = reader.GetInt64(ordSystemLinkId),
                            StpTradeId = reader.GetInt64(ordStpTradeId),
                            SystemCode = reader.IsDBNull(ordSystemCode) ? string.Empty : reader.GetString(ordSystemCode),
                            SystemTradeId = reader.IsDBNull(ordSystemTradeId) ? string.Empty : reader.GetString(ordSystemTradeId),
                            Status = reader.IsDBNull(ordStatus) ? string.Empty : reader.GetString(ordStatus),
                            LastStatusUtc = reader.IsDBNull(ordLastStatusUtc) ? (DateTime?)null : reader.GetDateTime(ordLastStatusUtc),
                            LastError = reader.IsDBNull(ordLastError) ? string.Empty : reader.GetString(ordLastError),
                            CreatedUtc = reader.IsDBNull(ordCreatedUtc) ? (DateTime?)null : reader.GetDateTime(ordCreatedUtc),
                            PortfolioCode = reader.IsDBNull(ordPortfolioCode) ? string.Empty : reader.GetString(ordPortfolioCode),
                            BookFlag = reader.IsDBNull(ordBookFlag) ? (bool?)null : reader.GetBoolean(ordBookFlag),
                            StpMode = reader.IsDBNull(ordStpMode) ? string.Empty : reader.GetString(ordStpMode),
                            ImportedBy = reader.IsDBNull(ordImportedBy) ? string.Empty : reader.GetString(ordImportedBy),
                            BookedBy = reader.IsDBNull(ordBookedBy) ? string.Empty : reader.GetString(ordBookedBy),
                            FirstBookedUtc = reader.IsDBNull(ordFirstBookedUtc) ? (DateTime?)null : reader.GetDateTime(ordFirstBookedUtc),
                            LastBookedUtc = reader.IsDBNull(ordLastBookedUtc) ? (DateTime?)null : reader.GetDateTime(ordLastBookedUtc),
                            StpFlag = reader.IsDBNull(ordStpFlag) ? (bool?)null : reader.GetBoolean(ordStpFlag),
                            IsDeleted = reader.GetBoolean(ordIsDeleted)
                        });
                    }
                }
            }

            return rows;
        }

        public async Task<IReadOnlyList<TradeWorkflowEventRow>> GetTradeWorkflowEventsAsync(long stpTradeId, int maxRows)
        {
            const string sql = @"
                                SELECT
                                    twe.WorkflowEventId,
                                    twe.StpTradeId,
                                    twe.TimestampUtc,
                                    twe.EventType,
                                    twe.SystemCode,
                                    twe.UserId,
                                    twe.Details
                                FROM trade_stp.TradeWorkflowEvent twe
                                WHERE twe.StpTradeId = @stpTradeId
                                ORDER BY twe.TimestampUtc DESC
                                LIMIT @maxRows;
                                ";

            var rows = new List<TradeWorkflowEventRow>();

            using (var conn = CreateConnection())
            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@stpTradeId", stpTradeId);
                cmd.Parameters.AddWithValue("@maxRows", maxRows);

                await conn.OpenAsync().ConfigureAwait(false);

                using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    int ordWorkflowEventId = reader.GetOrdinal("WorkflowEventId");
                    int ordStpTradeId = reader.GetOrdinal("StpTradeId");
                    int ordTimestampUtc = reader.GetOrdinal("TimestampUtc");
                    int ordEventType = reader.GetOrdinal("EventType");
                    int ordSystemCode = reader.GetOrdinal("SystemCode");
                    int ordUserId = reader.GetOrdinal("UserId");
                    int ordDetails = reader.GetOrdinal("Details");

                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        rows.Add(new TradeWorkflowEventRow
                        {
                            WorkflowEventId = reader.GetInt64(ordWorkflowEventId),
                            StpTradeId = reader.GetInt64(ordStpTradeId),
                            TimestampUtc = reader.GetDateTime(ordTimestampUtc),
                            EventType = reader.IsDBNull(ordEventType) ? string.Empty : reader.GetString(ordEventType),
                            SystemCode = reader.IsDBNull(ordSystemCode) ? string.Empty : reader.GetString(ordSystemCode),
                            UserId = reader.IsDBNull(ordUserId) ? string.Empty : reader.GetString(ordUserId),
                            Details = reader.IsDBNull(ordDetails) ? string.Empty : reader.GetString(ordDetails)
                        });
                    }
                }
            }

            return rows;
        }


        /// <summary>
        /// Hämtar en trade med systemlänk-info för bokning.
        /// Joinar Trade + TradeSystemLink för att få komplett BlotterTradeRow.
        /// </summary>
        public async Task<BlotterTradeRow> GetTradeByIdAsync(long stpTradeId)
        {
            const string sql = @"
SELECT 
    t.StpTradeId,
    t.TradeId,
    t.MessageInId,
    t.ProductType,
    t.SourceType,
    t.SourceVenueCode,
    t.CounterpartyCode,
    t.BrokerCode,
    t.TraderId,
    t.InvId,
    t.ReportingEntityId,
    t.CurrencyPair AS CcyPair,
    t.BuySell,
    t.CallPut,
    t.Notional,
    t.NotionalCurrency AS NotionalCcy,
    t.Strike,
    t.Cut,
    t.TradeDate,
    t.ExpiryDate,
    t.SettlementDate,
    t.NearSettlementDate,        
    t.IsNonDeliverable,          
    t.FixingDate,                  
    t.SettlementCurrency AS SettlementCcy,  
    t.ExecutionTimeUtc,
    t.Mic,
    t.Isin,
    t.Premium,
    t.PremiumCurrency AS PremiumCcy,
    t.PremiumDate,
    t.PortfolioMx3,
    t.Margin,
    t.Tvtic,
    t.HedgeRate,
    t.SpotRate,
    t.SwapPoints,
    t.HedgeType,
    t.Uti,
    t.IsDeleted AS TradeIsDeleted,
    t.LastUpdatedUtc AS TradeLastUpdatedUtc,
    t.LastUpdatedBy AS TradeLastUpdatedBy,
    mx3.SystemLinkId,
    mx3.SystemCode,
    mx3.Status,
    mx3.Status AS SystemStatus,
    mx3.SystemTradeId,
    mx3.SystemTradeId AS ExternalTradeId,
    mx3.LastStatusUtc,
    COALESCE(calypso.PortfolioCode, t.CalypsoBook) AS CalypsoPortfolio,
    COALESCE(calypso.StpFlag, mx3.StpFlag) AS StpFlag,
    mx3.Status AS Mx3Status,
    calypso.Status AS CalypsoStatus,
    GREATEST(
        COALESCE(t.LastUpdatedUtc, '1970-01-01'), 
        COALESCE(mx3.LastStatusUtc, '1970-01-01'),
        COALESCE(calypso.LastStatusUtc, '1970-01-01')
    ) AS LastChangeUtc
FROM trade_stp.Trade t
LEFT JOIN trade_stp.TradeSystemLink mx3 ON t.StpTradeId = mx3.StpTradeId 
    AND mx3.SystemCode = 'MX3' AND mx3.IsDeleted = 0
LEFT JOIN trade_stp.TradeSystemLink calypso ON t.StpTradeId = calypso.StpTradeId 
    AND calypso.SystemCode = 'CALYPSO' AND calypso.IsDeleted = 0
WHERE t.StpTradeId = @StpTradeId
  AND t.IsDeleted = 0
LIMIT 1;
";

            using (var conn = CreateConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@StpTradeId", stpTradeId);

                    using (var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            return MapBlotterTradeRow(reader);
                        }
                    }
                }
            }

            return null;
        }



        /// <summary>
        /// D4.2b: Uppdaterar status för TradeSystemLink.
        /// </summary>
        public async Task UpdateTradeSystemLinkStatusAsync(long stpTradeId, string systemCode, string status, string lastError)
        {
            const string sql = @"
UPDATE trade_stp.TradeSystemLink
SET Status = @Status,
    LastStatusUtc = UTC_TIMESTAMP(),
    LastError = @LastError
WHERE StpTradeId = @StpTradeId
  AND SystemCode = @SystemCode
  AND IsDeleted = 0;
";

            using (var conn = CreateConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@StpTradeId", stpTradeId);
                    cmd.Parameters.AddWithValue("@SystemCode", systemCode);
                    cmd.Parameters.AddWithValue("@Status", status);
                    cmd.Parameters.AddWithValue("@LastError", (object)lastError ?? DBNull.Value);

                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// D4.2b: Uppdaterar TradeSystemLink vid bokning.
        /// Sätter Status=PENDING, BookedBy=userId, och uppdaterar timestamp.
        /// Atomisk guard: endast om Status är NEW eller ERROR.
        /// </summary>
        public async Task UpdateTradeSystemLinkOnBookingAsync(long stpTradeId, string systemCode, string bookedBy)
        {
            const string sql = @"
UPDATE trade_stp.TradeSystemLink
SET Status = 'PENDING',
    BookedBy = @BookedBy,
    LastStatusUtc = UTC_TIMESTAMP()
WHERE StpTradeId = @StpTradeId
  AND SystemCode = @SystemCode
  AND IsDeleted = 0
  AND Status IN ('NEW', 'ERROR');
";

            using (var conn = CreateConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@StpTradeId", stpTradeId);
                    cmd.Parameters.AddWithValue("@SystemCode", systemCode);
                    cmd.Parameters.AddWithValue("@BookedBy", bookedBy);

                    var rowsAffected = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);

                    if (rowsAffected == 0)
                    {
                        throw new InvalidOperationException(
                            $"Trade {stpTradeId} / {systemCode} already booked or not found");
                    }
                }
            }
        }


        /// <summary>
        /// D4.3: Uppdaterar TradeSystemLink efter MX3 response.
        /// Sätter Status=BOOKED/ERROR, SystemTradeId (ContractID), och uppdaterar timestamp.
        /// </summary>
        public async Task UpdateTradeSystemLinkOnResponseAsync(
            long stpTradeId,
            string systemCode,
            string status,
            string systemTradeId,
            string lastError)
        {
            const string sql = @"
UPDATE trade_stp.TradeSystemLink
SET Status = @Status,
    SystemTradeId = @SystemTradeId,
    LastStatusUtc = UTC_TIMESTAMP(),
    LastError = @LastError,
    LastBookedUtc = CASE WHEN @Status = 'BOOKED' THEN UTC_TIMESTAMP() ELSE LastBookedUtc END
WHERE StpTradeId = @StpTradeId
  AND SystemCode = @SystemCode
  AND IsDeleted = 0;
";

            using (var conn = CreateConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@StpTradeId", stpTradeId);
                    cmd.Parameters.AddWithValue("@SystemCode", systemCode);
                    cmd.Parameters.AddWithValue("@Status", status);
                    cmd.Parameters.AddWithValue("@SystemTradeId", (object)systemTradeId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@LastError", (object)lastError ?? DBNull.Value);

                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }


        /// <summary>
        /// D4.2b: Skapar ett nytt TradeWorkflowEvent.
        /// </summary>
        public async Task InsertTradeWorkflowEventAsync(long stpTradeId, string eventType, string systemCode, string userId, string details)
        {
            const string sql = @"
INSERT INTO trade_stp.TradeWorkflowEvent 
(StpTradeId, EventType, SystemCode, TimestampUtc, UserId, Details)
VALUES 
(@StpTradeId, @EventType, @SystemCode, UTC_TIMESTAMP(), @UserId, @Details);
";

            using (var conn = CreateConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@StpTradeId", stpTradeId);
                    cmd.Parameters.AddWithValue("@EventType", eventType);
                    cmd.Parameters.AddWithValue("@SystemCode", systemCode);
                    cmd.Parameters.AddWithValue("@UserId", userId);
                    cmd.Parameters.AddWithValue("@Details", (object)details ?? DBNull.Value);

                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        public async Task<bool> UpdateTradeRoutingFieldsAsync(
    long stpTradeId,
    string portfolioMx3,
    string calypsoBook,
    DateTime expectedLastUpdatedUtc)
        {
            var setParts = new List<string>();
            var parameters = new DynamicParameters();

            parameters.Add("@StpTradeId", stpTradeId);
            parameters.Add("@ExpectedLastUpdatedUtc", expectedLastUpdatedUtc);

            if (portfolioMx3 != null)
            {
                setParts.Add("PortfolioMx3 = @PortfolioMx3");
                parameters.Add("@PortfolioMx3", portfolioMx3);
            }

            if (calypsoBook != null)
            {
                setParts.Add("CalypsoBook = @CalypsoBook");
                parameters.Add("@CalypsoBook", calypsoBook);
            }

            if (setParts.Count == 0)
            {
                // Nothing to update
                return true;
            }

            setParts.Add("LastUpdatedUtc = UTC_TIMESTAMP()");

            var sql = $@"
        UPDATE trade_stp.trade
        SET {string.Join(", ", setParts)}
        WHERE StpTradeId = @StpTradeId
          AND LastUpdatedUtc = @ExpectedLastUpdatedUtc
          AND IsDeleted = 0";

            using (var conn = new MySqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var rowsAffected = await conn.ExecuteAsync(sql, parameters);
                return rowsAffected > 0;
            }
        }


        /// <summary>
        /// Helper för att mappa DataReader till BlotterTradeRow.
        /// </summary>
        private BlotterTradeRow MapBlotterTradeRow(MySqlDataReader reader)
        {
            return new BlotterTradeRow
            {
                StpTradeId = reader.GetInt64("StpTradeId"),
                SystemLinkId = reader.IsDBNull(reader.GetOrdinal("SystemLinkId")) ? 0 : reader.GetInt64("SystemLinkId"),
                TradeId = reader.GetString("TradeId"),
                MessageInId = reader.IsDBNull(reader.GetOrdinal("MessageInId")) ? (long?)null : reader.GetInt64("MessageInId"),
                ProductType = reader.IsDBNull(reader.GetOrdinal("ProductType")) ? null : reader.GetString("ProductType"),
                SourceType = reader.IsDBNull(reader.GetOrdinal("SourceType")) ? null : reader.GetString("SourceType"),
                SourceVenueCode = reader.IsDBNull(reader.GetOrdinal("SourceVenueCode")) ? null : reader.GetString("SourceVenueCode"),
                CounterpartyCode = reader.IsDBNull(reader.GetOrdinal("CounterpartyCode")) ? null : reader.GetString("CounterpartyCode"),
                BrokerCode = reader.IsDBNull(reader.GetOrdinal("BrokerCode")) ? null : reader.GetString("BrokerCode"),
                TraderId = reader.IsDBNull(reader.GetOrdinal("TraderId")) ? null : reader.GetString("TraderId"),
                InvId = reader.IsDBNull(reader.GetOrdinal("InvId")) ? null : reader.GetString("InvId"),
                ReportingEntityId = reader.IsDBNull(reader.GetOrdinal("ReportingEntityId")) ? null : reader.GetString("ReportingEntityId"),
                CcyPair = reader.IsDBNull(reader.GetOrdinal("CcyPair")) ? null : reader.GetString("CcyPair"),
                BuySell = reader.IsDBNull(reader.GetOrdinal("BuySell")) ? null : reader.GetString("BuySell"),
                CallPut = reader.IsDBNull(reader.GetOrdinal("CallPut")) ? null : reader.GetString("CallPut"),
                Notional = reader.GetDecimal("Notional"),
                NotionalCcy = reader.IsDBNull(reader.GetOrdinal("NotionalCcy")) ? null : reader.GetString("NotionalCcy"),
                Strike = reader.IsDBNull(reader.GetOrdinal("Strike")) ? (decimal?)null : reader.GetDecimal("Strike"),
                Cut = reader.IsDBNull(reader.GetOrdinal("Cut")) ? null : reader.GetString("Cut"),
                TradeDate = reader.IsDBNull(reader.GetOrdinal("TradeDate")) ? (DateTime?)null : reader.GetDateTime("TradeDate"),
                ExpiryDate = reader.IsDBNull(reader.GetOrdinal("ExpiryDate")) ? (DateTime?)null : reader.GetDateTime("ExpiryDate"),
                SettlementDate = reader.IsDBNull(reader.GetOrdinal("SettlementDate")) ? (DateTime?)null : reader.GetDateTime("SettlementDate"),
                NearSettlementDate = reader.IsDBNull(reader.GetOrdinal("NearSettlementDate")) ? (DateTime?)null : reader.GetDateTime("NearSettlementDate"),  // ✅ LÄGG TILL
                IsNonDeliverable = reader.IsDBNull(reader.GetOrdinal("IsNonDeliverable")) ? (bool?)null : reader.GetBoolean("IsNonDeliverable"),  // ✅ LÄGG TILL
                FixingDate = reader.IsDBNull(reader.GetOrdinal("FixingDate")) ? (DateTime?)null : reader.GetDateTime("FixingDate"),  // ✅ LÄGG TILL
                SettlementCcy = reader.IsDBNull(reader.GetOrdinal("SettlementCcy")) ? null : reader.GetString("SettlementCcy"),  // ✅ LÄGG TILL
                ExecutionTimeUtc = reader.IsDBNull(reader.GetOrdinal("ExecutionTimeUtc")) ? (DateTime?)null : reader.GetDateTime("ExecutionTimeUtc"),
                Mic = reader.IsDBNull(reader.GetOrdinal("Mic")) ? null : reader.GetString("Mic"),
                Isin = reader.IsDBNull(reader.GetOrdinal("Isin")) ? null : reader.GetString("Isin"),
                Premium = reader.IsDBNull(reader.GetOrdinal("Premium")) ? (decimal?)null : reader.GetDecimal("Premium"),
                PremiumCcy = reader.IsDBNull(reader.GetOrdinal("PremiumCcy")) ? null : reader.GetString("PremiumCcy"),
                PremiumDate = reader.IsDBNull(reader.GetOrdinal("PremiumDate")) ? (DateTime?)null : reader.GetDateTime("PremiumDate"),
                PortfolioMx3 = reader.IsDBNull(reader.GetOrdinal("PortfolioMx3")) ? null : reader.GetString("PortfolioMx3"),
                CalypsoPortfolio = reader.IsDBNull(reader.GetOrdinal("CalypsoPortfolio")) ? null : reader.GetString("CalypsoPortfolio"),
                Margin = reader.IsDBNull(reader.GetOrdinal("Margin")) ? (decimal?)null : reader.GetDecimal("Margin"),
                Tvtic = reader.IsDBNull(reader.GetOrdinal("Tvtic")) ? null : reader.GetString("Tvtic"),
                Uti = reader.IsDBNull(reader.GetOrdinal("Uti")) ? null : reader.GetString("Uti"),  // ✅ LÄGG TILL
                HedgeRate = reader.IsDBNull(reader.GetOrdinal("HedgeRate")) ? (decimal?)null : reader.GetDecimal("HedgeRate"),
                SpotRate = reader.IsDBNull(reader.GetOrdinal("SpotRate")) ? (decimal?)null : reader.GetDecimal("SpotRate"),
                SwapPoints = reader.IsDBNull(reader.GetOrdinal("SwapPoints")) ? (decimal?)null : reader.GetDecimal("SwapPoints"),
                HedgeType = reader.IsDBNull(reader.GetOrdinal("HedgeType")) ? null : reader.GetString("HedgeType"),
                StpFlag = reader.IsDBNull(reader.GetOrdinal("StpFlag")) ? (bool?)null : reader.GetBoolean("StpFlag"),
                TradeIsDeleted = reader.GetBoolean("TradeIsDeleted"),
                TradeLastUpdatedUtc = reader.IsDBNull(reader.GetOrdinal("TradeLastUpdatedUtc")) ? (DateTime?)null : reader.GetDateTime("TradeLastUpdatedUtc"),
                TradeLastUpdatedBy = reader.IsDBNull(reader.GetOrdinal("TradeLastUpdatedBy")) ? null : reader.GetString("TradeLastUpdatedBy"),
                SystemCode = reader.IsDBNull(reader.GetOrdinal("SystemCode")) ? null : reader.GetString("SystemCode"),
                Status = reader.IsDBNull(reader.GetOrdinal("Status")) ? null : reader.GetString("Status"),
                SystemStatus = reader.IsDBNull(reader.GetOrdinal("SystemStatus")) ? null : reader.GetString("SystemStatus"),
                SystemTradeId = reader.IsDBNull(reader.GetOrdinal("SystemTradeId")) ? null : reader.GetString("SystemTradeId"),
                ExternalTradeId = reader.IsDBNull(reader.GetOrdinal("ExternalTradeId")) ? null : reader.GetString("ExternalTradeId"),
                LastChangeUtc = reader.IsDBNull(reader.GetOrdinal("LastChangeUtc")) ? (DateTime?)null : reader.GetDateTime("LastChangeUtc"),
                Mx3Status = reader.IsDBNull(reader.GetOrdinal("Mx3Status")) ? null : reader.GetString("Mx3Status"),
                CalypsoStatus = reader.IsDBNull(reader.GetOrdinal("CalypsoStatus")) ? null : reader.GetString("CalypsoStatus")
            };
        }


        /// <summary>
        /// Mappar databaskod (VARCHAR) till ProductType-enum.
        /// </summary>
        private static ProductType MapProductTypeFromDatabaseValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("ProductType database value cannot be null or empty.", nameof(value));

            switch (value.ToUpperInvariant())
            {
                case "SPOT":
                    return ProductType.Spot;
                case "FWD":
                    return ProductType.Fwd;
                case "SWAP":
                    return ProductType.Swap;
                case "NDF":
                    return ProductType.Ndf;
                case "OPTION_VANILLA":
                    return ProductType.OptionVanilla;
                case "OPTION_NDO":
                    return ProductType.OptionNdo;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown ProductType database value.");
            }
        }

        /// <summary>
        /// Mappar databaskod (VARCHAR) till SystemCode-enum.
        /// </summary>
        private static SystemCode MapSystemCodeFromDatabaseValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("SystemCode database value cannot be null or empty.", nameof(value));

            switch (value.ToUpperInvariant())
            {
                case "MX3":
                    return SystemCode.Mx3;
                case "CALYPSO":
                    return SystemCode.Calypso;
                case "VOLBROKER_STP":
                    return SystemCode.VolbrokerStp;
                case "RTNS":
                    return SystemCode.Rtns;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown SystemCode database value.");
            }
        }

        /// <summary>
        /// Mappar databaskod (VARCHAR) till TradeSystemStatus-enum.
        /// </summary>
        private static TradeSystemStatus MapTradeSystemStatusFromDatabaseValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("TradeSystemStatus database value cannot be null or empty.", nameof(value));

            switch (value.ToUpperInvariant())
            {
                case "NEW":
                    return TradeSystemStatus.New;
                case "PENDING":
                    return TradeSystemStatus.Pending;
                case "BOOKED":
                    return TradeSystemStatus.Booked;
                case "ERROR":
                    return TradeSystemStatus.Error;
                case "CANCELLED":
                    return TradeSystemStatus.Cancelled;
                case "READY_TO_ACK":
                    return TradeSystemStatus.ReadyToAck;
                case "ACK_SENT":
                    return TradeSystemStatus.AckSent;
                case "ACK_ERROR":
                    return TradeSystemStatus.AckError;
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown TradeSystemStatus database value.");
            }
        }

        // ==========================================
        // LEADER ELECTION METHODS 
        // ==========================================

        public async Task UpdatePresenceAsync(string nodeId, string userName, string machineName)
        {
            const string sql = @"
        INSERT INTO stp_blotter_presence (NodeId, UserName, MachineName, LastSeen)
        VALUES (@NodeId, @UserName, @MachineName, UTC_TIMESTAMP())
        ON DUPLICATE KEY UPDATE LastSeen = UTC_TIMESTAMP()";

            using (var conn = new MySqlConnection(_connectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@NodeId", nodeId);
                    cmd.Parameters.AddWithValue("@UserName", userName);
                    cmd.Parameters.AddWithValue("@MachineName", machineName);
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        public async Task<List<string>> GetOnlineUsersAsync()
        {
            const string sql = @"
        SELECT DISTINCT UserName 
        FROM stp_blotter_presence
        WHERE LastSeen > DATE_SUB(UTC_TIMESTAMP(), INTERVAL 30 SECOND)";

            var users = new List<string>();

            using (var conn = new MySqlConnection(_connectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = new MySqlCommand(sql, conn))
                using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        users.Add(reader.GetString(0));
                    }
                }
            }

            return users;
        }

        public async Task<List<string>> GetMasterPriorityAsync()
        {
            const string sql = @"
        SELECT UserName 
        FROM stp_blotter_master_priority
        ORDER BY OrderNo ASC";

            var priority = new List<string>();

            using (var conn = new MySqlConnection(_connectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = new MySqlCommand(sql, conn))
                using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        priority.Add(reader.GetString(0));
                    }
                }
            }

            return priority;
        }

        public async Task<bool> TryAcquireMasterLockAsync(string lockName, string candidateUser, string machineName)
        {
            const string sql = @"
        UPDATE stp_blotter_master_lock
        SET HeldByUser = @CandidateUser,
            HeldByMachine = @MachineName,
            LastHeartbeat = UTC_TIMESTAMP(),
            ExpiresAt = DATE_ADD(UTC_TIMESTAMP(), INTERVAL 30 SECOND)
        WHERE LockName = @LockName
          AND (ExpiresAt < UTC_TIMESTAMP() 
               OR HeldByUser = @CandidateUser)";

            using (var conn = new MySqlConnection(_connectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@LockName", lockName);
                    cmd.Parameters.AddWithValue("@CandidateUser", candidateUser);
                    cmd.Parameters.AddWithValue("@MachineName", machineName);

                    var affectedRows = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    return affectedRows > 0;
                }
            }
        }

        public async Task<string> GetCurrentMasterAsync(string lockName)
        {
            const string sql = @"
        SELECT HeldByUser 
        FROM stp_blotter_master_lock
        WHERE LockName = @LockName
          AND ExpiresAt > UTC_TIMESTAMP()";

            using (var conn = new MySqlConnection(_connectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@LockName", lockName);

                    var result = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                    return result?.ToString();
                }
            }
        }

        /// <summary>
        /// Hämtar alla trades med PENDING status för ett specifikt system.
        /// Joinar Trade + TradeSystemLink för att få produkttyp och trade-id.
        /// </summary>
        public async Task<IReadOnlyList<PendingTradeSystemLink>> GetPendingTradeSystemLinksAsync(string systemCode)
        {
            const string sql = @"
SELECT 
    tsl.StpTradeId,
    t.TradeId,
    t.ProductType,
    tsl.SystemCode,
    tsl.LastStatusUtc
FROM trade_stp.TradeSystemLink tsl
INNER JOIN trade_stp.Trade t ON t.StpTradeId = tsl.StpTradeId
WHERE tsl.SystemCode = @SystemCode
  AND tsl.Status = 'PENDING'
  AND tsl.IsDeleted = 0
  AND t.IsDeleted = 0
ORDER BY tsl.LastStatusUtc DESC;
";

            var results = new List<PendingTradeSystemLink>();

            using (var conn = CreateConnection())
            {
                await conn.OpenAsync().ConfigureAwait(false);

                using (var cmd = new MySqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@SystemCode", systemCode);

                    using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        int ordStpTradeId = reader.GetOrdinal("StpTradeId");
                        int ordTradeId = reader.GetOrdinal("TradeId");
                        int ordProductType = reader.GetOrdinal("ProductType");
                        int ordSystemCode = reader.GetOrdinal("SystemCode");
                        int ordLastStatusUtc = reader.GetOrdinal("LastStatusUtc");

                        while (await reader.ReadAsync().ConfigureAwait(false))
                        {
                            results.Add(new PendingTradeSystemLink
                            {
                                StpTradeId = reader.GetInt64(ordStpTradeId),
                                TradeId = reader.GetString(ordTradeId),
                                ProductType = reader.GetString(ordProductType),
                                SystemCode = reader.GetString(ordSystemCode),
                                LastUpdatedUtc = reader.GetDateTime(ordLastStatusUtc)
                            });
                        }
                    }
                }
            }

            return results;
        }


    }
}
