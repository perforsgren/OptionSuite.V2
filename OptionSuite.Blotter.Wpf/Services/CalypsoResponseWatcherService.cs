// OptionSuite.Blotter.Wpf/Services/CalypsoResponseWatcherService.cs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        // Deduplication - spara redan processade filer
        private readonly HashSet<string> _processedFiles = new HashSet<string>();

        public CalypsoResponseWatcherService(IStpRepositoryAsync repository, string responseFolder)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _responseFolder = responseFolder ?? throw new ArgumentNullException(nameof(responseFolder));
        }

        public void Start()
        {
            if (_isWatching)
                return;

            Debug.WriteLine($"[CalypsoWatcher] Starting FileSystemWatcher on: {_responseFolder}");

            // 1. Startup scan (plocka upp filer som missades)
            _ = Task.Run(async () => await StartupScanAsync());

            // 2. Starta FileSystemWatcher
            _watcher = new FileSystemWatcher(_responseFolder)
            {
                Filter = "*_result.xml",
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileCreated;

            _isWatching = true;
        }

        public void Stop()
        {
            if (!_isWatching)
                return;

            Debug.WriteLine("[CalypsoWatcher] Stopping FileSystemWatcher");

            _watcher?.Dispose();
            _watcher = null;
            _isWatching = false;
            _processedFiles.Clear();
        }

        private async Task StartupScanAsync()
        {
            try
            {
                Debug.WriteLine("[CalypsoWatcher] Running startup scan...");

                var files = Directory.GetFiles(_responseFolder, "*_result.xml");

                foreach (var file in files)
                {
                    await ProcessResponseFileAsync(file);
                }

                Debug.WriteLine($"[CalypsoWatcher] Startup scan complete. Processed {files.Length} files.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CalypsoWatcher] Startup scan error: {ex.Message}");
            }
        }

        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            // Vänta lite för att Calypso ska bli klar med att skriva
            await Task.Delay(1000);
            await ProcessResponseFileAsync(e.FullPath);
        }

        private async Task ProcessResponseFileAsync(string filePath)
        {
            var fileName = Path.GetFileName(filePath);

            // Deduplication - skippa om redan processat
            lock (_processedFiles)
            {
                if (_processedFiles.Contains(fileName))
                {
                    Debug.WriteLine($"[CalypsoWatcher] Already processed: {fileName}, skipping.");
                    return;
                }
                _processedFiles.Add(fileName);
            }

            try
            {
                Debug.WriteLine($"[CalypsoWatcher] Processing file: {fileName}");

                // Parse response
                var response = CalypsoResponseParserService.Parse(filePath);

                // ✅ ÄNDRAT: Kolla StpTradeId istället för TradeId
                if (response.StpTradeId == 0)
                {
                    Debug.WriteLine($"[CalypsoWatcher] Failed to parse StpTradeId, skipping.");
                    return;
                }

                // ✅ ÄNDRAT: Använd GetTradeByIdAsync direkt (samma som MX3)
                var trade = await _repository.GetTradeByIdAsync(response.StpTradeId);
                if (trade == null)
                {
                    Debug.WriteLine($"[CalypsoWatcher] ⚠️ StpTradeId {response.StpTradeId} does NOT exist in database. Skipping.");
                    return;
                }

                // Uppdatera status
                await _repository.UpdateTradeSystemLinkOnResponseAsync(
                    stpTradeId: response.StpTradeId,  // ✅ Från response
                    systemCode: "CALYPSO",
                    status: response.IsSuccess ? "BOOKED" : "ERROR",
                    systemTradeId: response.CalypsoTradeId,
                    lastError: response.IsSuccess ? null : response.ErrorMessage
                );

                // Insert WorkflowEvent
                var eventType = response.IsSuccess ? "BookingConfirmed" : "BookingRejected";
                var details = response.IsSuccess
                    ? $"Calypso Trade ID: {response.CalypsoTradeId}"
                    : $"Errors: {response.ErrorMessage}";

                await _repository.InsertTradeWorkflowEventAsync(
                    response.StpTradeId,  // ✅ Från response
                    eventType,
                    "CALYPSO",
                    "CALYPSO_WATCHER",
                    details
                );

                Debug.WriteLine($"[CalypsoWatcher] ✅ Updated StpTradeId {response.StpTradeId}: {(response.IsSuccess ? "BOOKED" : "ERROR")}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CalypsoWatcher] ❌ Error processing file: {ex.Message}");

                // Ta bort från processed-listan så vi kan försöka igen
                lock (_processedFiles)
                {
                    _processedFiles.Remove(fileName);
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
