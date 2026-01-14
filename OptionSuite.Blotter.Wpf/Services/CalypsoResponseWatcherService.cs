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
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };

                _watcher.Created += OnFileCreated;
                _watcher.Changed += OnFileCreated; // ✅ FIX: Fånga även Changed events
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
                    var expectedFileNames = BuildExpectedResponseFileNames(trade);

                    Debug.WriteLine($"[CalypsoWatcher] Looking for response files for StpTradeId={trade.StpTradeId}, ProductType={trade.ProductType}");
                    foreach (var expected in expectedFileNames)
                    {
                        Debug.WriteLine($"[CalypsoWatcher]    Candidate: {expected}");
                    }

                    var matchingFile = responseFiles.FirstOrDefault(f =>
                        expectedFileNames.Any(expected =>
                            f.FileName.Equals(expected, StringComparison.OrdinalIgnoreCase)));

                    if (matchingFile != null)
                    {
                        Debug.WriteLine($"[CalypsoWatcher] ✅ Found response for StpTradeId={trade.StpTradeId}: {matchingFile.FileName}");
                        await ProcessResponseFileAsync(matchingFile.FullPath);
                        foundResponses++;
                    }
                    else
                    {
                        Debug.WriteLine($"[CalypsoWatcher] ⏳ PENDING trade {trade.StpTradeId} waiting for response");
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

        /// <summary>
        /// ✅ FIX: Returnerar LISTA av möjliga filnamn för att hantera 
        /// olika ProductType-format (SPOT, Spot, FWD, Fwd, FORWARD, etc.)
        /// </summary>
        private List<string> BuildExpectedResponseFileNames(PendingCalypsoTrade trade)
        {
            var results = new List<string>();
            var productUpper = trade.ProductType?.ToUpperInvariant() ?? "";

            // Avgör om det är SPOT eller FORWARD baserat på ProductType
            bool isSpot = productUpper == "SPOT" || productUpper.Contains("SPOT");
            bool isForward = productUpper == "FWD" || productUpper == "FORWARD" ||
                             productUpper.Contains("FWD") || productUpper.Contains("FORWARD");

            // Lägg till båda varianter om oklart
            if (isSpot || (!isSpot && !isForward))
            {
                results.Add($"FX_SPOT_{trade.StpTradeId}_{trade.TradeId}_result.xml");
            }

            if (isForward || (!isSpot && !isForward))
            {
                results.Add($"FX_FORWARD_{trade.StpTradeId}_{trade.TradeId}_result.xml");
            }

            return results;
        }

        // HashSet för att undvika dubbel-processning av samma fil
        private readonly HashSet<string> _processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            var fileName = Path.GetFileName(e.FullPath);

            // ✅ FIX: Filtrera bara relevanta filer (FX_SPOT, FX_FORWARD, FXSWAP)
            if (!IsRelevantCalypsoFile(fileName))
            {
                Debug.WriteLine($"[CalypsoWatcher] ⏭️ Skipping irrelevant file: {fileName}");
                return;
            }

            // ✅ FIX: Undvik dubbel-processning (Created + Changed kan trigga båda)
            lock (_processedFiles)
            {
                if (_processedFiles.Contains(fileName))
                {
                    Debug.WriteLine($"[CalypsoWatcher] ⏭️ Already processing {fileName}, skipping duplicate event");
                    return;
                }
                _processedFiles.Add(fileName);
            }

            try
            {
                Debug.WriteLine($"[CalypsoWatcher] 📁 New file detected: {fileName}");

                // Vänta lite längre för att filen ska bli helt skriven
                await Task.Delay(2000);
                await ProcessResponseFileAsync(e.FullPath);
            }
            finally
            {
                // Ta bort från processed efter en stund för att tillåta re-processning vid behov
                _ = Task.Delay(30000).ContinueWith(_ =>
                {
                    lock (_processedFiles)
                    {
                        _processedFiles.Remove(fileName);
                    }
                });
            }
        }

        /// <summary>
        /// Kontrollerar om filen är relevant för Calypso response processing.
        /// Bara FX_SPOT, FX_FORWARD och FXSWAP ska processas.
        /// </summary>
        private bool IsRelevantCalypsoFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            var upperFileName = fileName.ToUpperInvariant();

            return upperFileName.StartsWith("FX_SPOT_") ||
                   upperFileName.StartsWith("FX_FORWARD_") ||
                   upperFileName.StartsWith("FXSWAP_");
        }

        private async Task ProcessResponseFileAsync(string filePath)
        {
            var fileName = Path.GetFileName(filePath);

            try
            {
                Debug.WriteLine($"[CalypsoWatcher] 🔄 Processing file: {fileName}");

                // ✅ FIX: Kontrollera att filen finns och inte är låst
                if (!File.Exists(filePath))
                {
                    Debug.WriteLine($"[CalypsoWatcher] ⚠️ File does not exist: {fileName}");
                    return;
                }

                var response = CalypsoResponseParserService.Parse(filePath);

                if (response.StpTradeId == 0)
                {
                    Debug.WriteLine($"[CalypsoWatcher] ⚠️ Failed to parse StpTradeId from {fileName}, skipping.");
                    return;
                }

                Debug.WriteLine($"[CalypsoWatcher] Parsed StpTradeId={response.StpTradeId}, IsSuccess={response.IsSuccess}, CalypsoTradeId={response.CalypsoTradeId}");

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
                Debug.WriteLine($"[CalypsoWatcher]    StackTrace: {ex.StackTrace}");
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