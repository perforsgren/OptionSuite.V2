using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FxTradeHub.Domain.Interfaces;

namespace OptionSuite.Blotter.Wpf.Services
{
    public sealed class CalypsoResponseWatcherService : IDisposable
    {
        private readonly IStpRepositoryAsync _repository;
        private readonly string _responseFolder;
        private FileSystemWatcher _watcher;
        private bool _isWatching;

        public CalypsoResponseWatcherService(IStpRepositoryAsync repository, string responseFolder)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _responseFolder = responseFolder ?? throw new ArgumentNullException(nameof(responseFolder));
        }

        public void Start()
        {
            if (_isWatching)
                return;

            try
            {
                // ✅ Validera att mappen finns INNAN FileSystemWatcher skapas
                if (!Directory.Exists(_responseFolder))
                {
                    Debug.WriteLine($"[CalypsoWatcher] ⚠️ WARNING: Response folder does not exist or is not accessible:");
                    Debug.WriteLine($"[CalypsoWatcher]    Path: {_responseFolder}");
                    Debug.WriteLine($"[CalypsoWatcher]    FileSystemWatcher will NOT be started.");
                    Debug.WriteLine($"[CalypsoWatcher]    Please check network connectivity and permissions.");
                    return;
                }

                Debug.WriteLine($"[CalypsoWatcher] 🚀 Starting FileSystemWatcher on: {_responseFolder}");

                // 1. Startup scan - kolla PENDING trades mot response-folder
                _ = Task.Run(async () => await StartupReconciliationAsync());

                // 2. Starta FileSystemWatcher för nya responses
                _watcher = new FileSystemWatcher(_responseFolder)
                {
                    Filter = "*_result.xml",
                    NotifyFilter = NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };

                _watcher.Created += OnFileCreated;
                _watcher.Error += OnFileSystemWatcherError;

                _isWatching = true;
                Debug.WriteLine("[CalypsoWatcher] ✅ FileSystemWatcher started successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CalypsoWatcher] ❌ Failed to start FileSystemWatcher: {ex.Message}");
                Debug.WriteLine($"[CalypsoWatcher]    Path: {_responseFolder}");
                Debug.WriteLine($"[CalypsoWatcher]    Exception: {ex.GetType().Name}");

                _watcher?.Dispose();
                _watcher = null;
                _isWatching = false;
            }
        }

        public void Stop()
        {
            if (!_isWatching)
                return;

            Debug.WriteLine("[CalypsoWatcher] 🛑 Stopping FileSystemWatcher");

            _watcher?.Dispose();
            _watcher = null;
            _isWatching = false;
        }

        /// <summary>
        /// Hanterar FileSystemWatcher errors (t.ex. network disconnect).
        /// </summary>
        private void OnFileSystemWatcherError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            Debug.WriteLine($"[CalypsoWatcher] ⚠️ FileSystemWatcher error: {ex?.Message ?? "Unknown error"}");
            Debug.WriteLine($"[CalypsoWatcher]    This may indicate network connectivity issues.");
        }

        private async Task StartupReconciliationAsync()
        {
            try
            {
                Debug.WriteLine("[CalypsoWatcher] 🔍 Running startup reconciliation...");

                if (!Directory.Exists(_responseFolder))
                {
                    Debug.WriteLine($"[CalypsoWatcher] ⚠️ Response folder not accessible, skipping reconciliation.");
                    return;
                }

                var pendingTrades = await GetPendingCalypsoTradesAsync();

                Debug.WriteLine($"[CalypsoWatcher] Found {pendingTrades.Count} PENDING Calypso trades in DB");

                if (pendingTrades.Count == 0)
                {
                    Debug.WriteLine("[CalypsoWatcher] ✅ No PENDING trades, startup reconciliation complete.");
                    return;
                }

                var responseFiles = Directory.GetFiles(_responseFolder, "*_result.xml")
                    .Select(f => new {
                        FullPath = f,
                        FileName = Path.GetFileName(f)
                    })
                    .ToList();

                Debug.WriteLine($"[CalypsoWatcher] Found {responseFiles.Count} response files in folder");

                int foundResponses = 0;
                int missingResponses = 0;

                foreach (var trade in pendingTrades)
                {
                    var expectedFileName = BuildExpectedResponseFileName(trade);

                    var matchingFile = responseFiles.FirstOrDefault(f =>
                        f.FileName.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase));

                    if (matchingFile != null)
                    {
                        Debug.WriteLine($"[CalypsoWatcher] ✅ Found response for StpTradeId={trade.StpTradeId}: {expectedFileName}");
                        await ProcessResponseFileAsync(matchingFile.FullPath);
                        foundResponses++;
                    }
                    else
                    {
                        Debug.WriteLine($"[CalypsoWatcher] ⏳ PENDING trade {trade.StpTradeId} waiting for response: {expectedFileName}");
                        missingResponses++;
                    }
                }

                Debug.WriteLine($"[CalypsoWatcher] 📊 Startup reconciliation complete:");
                Debug.WriteLine($"[CalypsoWatcher]    - Processed responses: {foundResponses}");
                Debug.WriteLine($"[CalypsoWatcher]    - Still waiting: {missingResponses}");
            }
            catch (IOException ioEx)
            {
                Debug.WriteLine($"[CalypsoWatcher] ❌ I/O error during reconciliation: {ioEx.Message}");
            }
            catch (UnauthorizedAccessException uaEx)
            {
                Debug.WriteLine($"[CalypsoWatcher] ❌ Access denied during reconciliation: {uaEx.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CalypsoWatcher] ❌ Startup reconciliation error: {ex.Message}");
            }
        }

        private async Task<List<PendingCalypsoTrade>> GetPendingCalypsoTradesAsync()
        {
            try
            {
                var links = await _repository.GetPendingTradeSystemLinksAsync("CALYPSO");

                return links.Select(l => new PendingCalypsoTrade
                {
                    StpTradeId = l.StpTradeId,
                    TradeId = l.TradeId,
                    ProductType = l.ProductType
                }).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CalypsoWatcher] ❌ Failed to get PENDING trades: {ex.Message}");
                return new List<PendingCalypsoTrade>();
            }
        }

        private string BuildExpectedResponseFileName(PendingCalypsoTrade trade)
        {
            var prefix = trade.ProductType.ToUpperInvariant().Contains("SPOT") ? "FX_SPOT_" : "FX_FORWARD_";
            return $"{trade.StpTradeId}_{prefix}{trade.TradeId}_result.xml";
        }

        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            Debug.WriteLine($"[CalypsoWatcher] 📁 New file detected: {Path.GetFileName(e.FullPath)}");

            await Task.Delay(1000);
            await ProcessResponseFileAsync(e.FullPath);
        }

        private async Task ProcessResponseFileAsync(string filePath)
        {
            var fileName = Path.GetFileName(filePath);

            try
            {
                Debug.WriteLine($"[CalypsoWatcher] 🔄 Processing file: {fileName}");

                var response = CalypsoResponseParserService.Parse(filePath);

                if (response.StpTradeId == 0)
                {
                    Debug.WriteLine($"[CalypsoWatcher] ⚠️ Failed to parse StpTradeId from {fileName}, skipping.");
                    return;
                }

                Debug.WriteLine($"[CalypsoWatcher] Parsed StpTradeId={response.StpTradeId} from {fileName}");

                if (await IsAlreadyProcessedAsync(response.StpTradeId))
                {
                    Debug.WriteLine($"[CalypsoWatcher] ⏭️ Already processed StpTradeId {response.StpTradeId}, skipping.");
                    return;
                }

                var trade = await _repository.GetTradeByIdAsync(response.StpTradeId);
                if (trade == null)
                {
                    Debug.WriteLine($"[CalypsoWatcher] ⚠️ StpTradeId {response.StpTradeId} does NOT exist in database. Skipping.");
                    return;
                }

                Debug.WriteLine($"[CalypsoWatcher] Updating TradeSystemLink for StpTradeId={response.StpTradeId}, Status={response.IsSuccess}");

                await _repository.UpdateTradeSystemLinkOnResponseAsync(
                    stpTradeId: response.StpTradeId,
                    systemCode: "CALYPSO",
                    status: response.IsSuccess ? "BOOKED" : "ERROR",
                    systemTradeId: response.CalypsoTradeId,
                    lastError: response.IsSuccess ? null : response.ErrorMessage
                );

                var eventType = response.IsSuccess ? "BookingConfirmed" : "BookingRejected";
                var details = response.IsSuccess
                    ? $"Calypso Trade ID: {response.CalypsoTradeId}"
                    : $"Errors: {response.ErrorMessage}";

                await _repository.InsertTradeWorkflowEventAsync(
                    response.StpTradeId,
                    eventType,
                    "CALYPSO",
                    "CALYPSO_WATCHER",
                    details
                );

                Debug.WriteLine($"[CalypsoWatcher] ✅ Successfully updated StpTradeId {response.StpTradeId}: {(response.IsSuccess ? "BOOKED" : "ERROR")}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CalypsoWatcher] ❌ Error processing {fileName}: {ex.Message}");
            }
        }

        private async Task<bool> IsAlreadyProcessedAsync(long stpTradeId)
        {
            try
            {
                var links = await _repository.GetTradeSystemLinksAsync(stpTradeId);
                var calypsoLink = links.FirstOrDefault(l => l.SystemCode == "CALYPSO");

                if (calypsoLink == null)
                    return false;

                return calypsoLink.Status == "BOOKED" || calypsoLink.Status == "ERROR";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CalypsoWatcher] ⚠️ Error checking if already processed: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private class PendingCalypsoTrade
        {
            public long StpTradeId { get; set; }
            public string TradeId { get; set; }
            public string ProductType { get; set; }
        }
    }
}