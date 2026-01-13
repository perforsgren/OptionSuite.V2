// libs/FxTradeHub/FxTradeHub.Services/CalypsoExport/CalypsoLinearExportService.cs

using FxSharedConfig;
using FxTradeHub.Contracts.Dtos;
using FxTradeHub.Services.Mx3Export;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FxTradeHub.Services.CalypsoExport
{
    /// <summary>
    /// Service för att exportera linear trades (Spot/Forward) till Calypso CSV-format.
    /// Hanterar STP-logik: StpFlag=1 → endast MX3_SHADOW, StpFlag=0 → MX3_SHADOW + Counterparty.
    /// </summary>
    public sealed class CalypsoLinearExportService
    {
        public Mx3OptionExportResult CreateCsvFile(CalypsoLinearExportRequest request)
        {
            try
            {
                var exportFolder = AppPaths.CalypsoImportFolder;

                // ✅ FIX: Använd samma filnamnsstruktur som MX3
                var prefix = request.ProductType == "Spot" ? "FX_SPOT_" : "FX_FORWARD_";
                var fileName = $"{request.StpTradeId}_{prefix}{request.TradeId}.csv";
                // Exempel: "127_FX_SPOT_20260109_155433_AUDUSD_H1.csv"

                var fullPath = Path.Combine(exportFolder, fileName);

                var csvContent = request.ProductType == "Spot"
                    ? BuildSpotCsv(request)
                    : BuildForwardCsv(request);

                File.WriteAllText(fullPath, csvContent);

                return new Mx3OptionExportResult
                {
                    Success = true,
                    FileName = fileName,
                    FilePath = fullPath
                };
            }
            catch (Exception ex)
            {
                return new Mx3OptionExportResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private string BuildSpotCsv(CalypsoLinearExportRequest req)
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("Action,Counterparty,Book,MirrorBook,BuySell,Trade Date,TraderName,keyword.MirrorTrader,ProductType,ProductSubType,PrimaryCurrency,SecondaryCurrency,PrimaryAmount,SettlementDate,SpotRate");

            var timestamp = FormatTimestamp(req.ExecutionTimeUtc);
            var buySell = req.BuySell.ToUpper();
            var ccy1 = req.CurrencyPair.Substring(0, 3);
            var ccy2 = req.CurrencyPair.Substring(3, 3);

            // Row 1: MX3_SHADOW leg (always included)
            sb.Append("NEW,");
            sb.Append("SWEDBANKAB,");
            sb.Append("MX3_SHADOW,");
            sb.Append($"{req.CalypsoBook},");
            sb.Append($"{buySell},");
            sb.Append($"{timestamp},");
            sb.Append($"{req.Trader.ToLower()},");
            sb.Append($"{req.Trader.ToLower()},");
            sb.Append("FX,");
            sb.Append("FXSpot,");
            sb.Append($"{ccy1},");
            sb.Append($"{ccy2},");
            sb.Append($"{req.Notional},");
            sb.Append($"{req.SettlementDate:yyyyMMdd},");
            sb.AppendLine($"{req.Rate}");

            // Row 2: Counterparty leg (only if StpFlag = false)
            if (!req.StpFlag)
            {
                sb.Append("NEW,");
                sb.Append($"{req.Counterparty},");
                sb.Append($"{req.CalypsoBook},");
                sb.Append(","); // No mirror book
                sb.Append($"{buySell},");
                sb.Append($"{timestamp},");
                sb.Append($"{req.Trader.ToLower()},");
                sb.Append(","); // No mirror trader
                sb.Append("FX,");
                sb.Append("FXSpot,");
                sb.Append($"{ccy1},");
                sb.Append($"{ccy2},");
                sb.Append($"{req.Notional},");
                sb.Append($"{req.SettlementDate:yyyyMMdd},");
                sb.AppendLine($"{req.Rate}");
            }

            return sb.ToString().TrimEnd('\r', '\n');
        }

        private string BuildForwardCsv(CalypsoLinearExportRequest req)
        {
            var sb = new StringBuilder();

            // Header (with EMIR fields)
            sb.AppendLine("Action,Counterparty,Book,MirrorBook,BuySell,Trade Date,TraderName,keyword.MirrorTrader,ProductType,ProductSubType,PrimaryCurrency,SecondaryCurrency,PrimaryAmount,SettlementDate,Forward Rate,keyword.MeansOfPayment,keyword.ExecutionDateTime,keyword.ExecutionVenueMIC,keyword.ReportingTVTIC,keyword.ReportingESMAUTIValue,keyword.InstrumentISIN,keyword.SWB_EP,keyword.SWB_IP");

            var timestamp = FormatTimestamp(req.ExecutionTimeUtc);
            var buySell = req.BuySell.ToUpper();
            var ccy1 = req.CurrencyPair.Substring(0, 3);
            var ccy2 = req.CurrencyPair.Substring(3, 3);

            // Row 1: MX3_SHADOW leg (always included)
            sb.Append("NEW,");
            sb.Append("SWEDBANKAB,");
            sb.Append("MX3_SHADOW,");
            sb.Append($"{req.CalypsoBook},");
            sb.Append($"{buySell},");
            sb.Append($"{timestamp},");
            sb.Append($"{req.Trader.ToLower()},");
            sb.Append($"{req.Trader.ToLower()},");
            sb.Append("FX,");
            sb.Append("FXForward,");
            sb.Append($"{ccy1},");
            sb.Append($"{ccy2},");
            sb.Append($"{req.Notional},");
            sb.Append($"{req.SettlementDate:yyyyMMdd},");
            sb.AppendLine($"{req.Rate}");

            // Row 2: Counterparty leg (only if StpFlag = false)
            if (!req.StpFlag)
            {
                sb.Append("NEW,");
                sb.Append($"{req.Counterparty},");
                sb.Append($"{req.CalypsoBook},");
                sb.Append(",");
                sb.Append($"{buySell},");
                sb.Append($"{timestamp},");
                sb.Append($"{req.Trader.ToLower()},");
                sb.Append(",");
                sb.Append("FX,");
                sb.Append("FXForward,");
                sb.Append($"{ccy1},");
                sb.Append($"{ccy2},");
                sb.Append($"{req.Notional},");
                sb.Append($"{req.SettlementDate:yyyyMMdd},");
                sb.Append($"{req.Rate},");

                // EMIR fields
                sb.Append("N,"); // MeansOfPayment
                sb.Append($"{FormatExecutionDateTime(req.ExecutionTimeUtc)},");
                sb.Append($"{(req.Mic == "SWBI" ? "XOFF" : req.Mic)},");
                sb.Append($"{req.Tvtic},");
                sb.Append($"{req.Uti},");
                sb.Append($"{req.Isin},");
                sb.Append($"{req.InvestorId},");
                sb.AppendLine($"{req.InvestorId}");
            }

            return sb.ToString().TrimEnd('\r', '\n');
        }

        private string FormatTimestamp(DateTime utc)
        {
            var local = utc.ToLocalTime();
            return local.ToString("yyyyMMdd'T'HH:mm:ss");
        }

        private string FormatExecutionDateTime(DateTime utc)
        {
            return utc.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }
    }
}
