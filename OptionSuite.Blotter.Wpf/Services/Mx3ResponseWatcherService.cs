using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Enums;
using FxTradeHub.Domain.Interfaces;
using FxTradeHub.Domain.Entities;

namespace OptionSuite.Blotter.Wpf.Services
{
    /// <summary>
    /// Tittar på MX3 response-mappen och processar filer.
    /// Körs ENDAST när denna instans är master.
    /// </summary>
    public sealed class Mx3ResponseWatcherService : IDisposable
    {
        private readonly IStpRepositoryAsync _repository;
        private readonly string _responseFolder;
        private FileSystemWatcher _watcher;
        private bool _isWatching;

        public Mx3ResponseWatcherService(IStpRepositoryAsync repository, string responseFolder)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _responseFolder = responseFolder ?? throw new ArgumentNullException(nameof(responseFolder));
        }

        public void Start()
        {
            if (_isWatching)
                return;

            Debug.WriteLine($"[Watcher] Starting FileSystemWatcher on: {_responseFolder}");

            // 1. Startup scan (plocka upp filer som missades)
            _ = Task.Run(async () => await StartupScanAsync());

            // 2. Starta FileSystemWatcher
            _watcher = new FileSystemWatcher(_responseFolder)
            {
                Filter = "*_2.xml",  // Bara success-filer (de är avgörande)
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileCreated;
            _watcher.Changed += OnFileChanged;

            _isWatching = true;
        }

        public void Stop()
        {
            if (!_isWatching)
                return;

            Debug.WriteLine("[Watcher] Stopping FileSystemWatcher");

            _watcher?.Dispose();
            _watcher = null;
            _isWatching = false;
        }

        private async Task StartupScanAsync()
        {
            try
            {
                Debug.WriteLine("[Watcher] Running startup scan...");

                var files = Directory.GetFiles(_responseFolder, "*_2.xml");

                foreach (var file in files)
                {
                    await ProcessResponseFileAsync(file);
                }

                Debug.WriteLine($"[Watcher] Startup scan complete. Processed {files.Length} files.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Watcher] Startup scan error: {ex.Message}");
            }
        }

        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            // Vänta lite för att MX3 ska bli klar med att skriva
            await Task.Delay(500);
            await ProcessResponseFileAsync(e.FullPath);
        }

        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            await Task.Delay(500);
            await ProcessResponseFileAsync(e.FullPath);
        }

        private async Task ProcessResponseFileAsync(string filePath)
        {
            try
            {
                Debug.WriteLine($"[Watcher] Processing file: {Path.GetFileName(filePath)}");

                // Parse response
                var response = Mx3ResponseParserService.Parse(_responseFolder, filePath);

                if (response.StpTradeId == 0)
                {
                    Debug.WriteLine($"[Watcher] Failed to parse StpTradeId, skipping.");
                    return;
                }

                // Uppdatera DB endast om status fortfarande är PENDING
                var status = response.IsSuccess
                    ? TradeSystemStatus.Booked
                    : TradeSystemStatus.Error;

                await _repository.UpdateTradeSystemLinkStatusAsync(
                    response.StpTradeId,
                    "MX3",
                    status,
                    response.Mx3TradeId ?? response.Mx3ContractId,
                    response.ErrorMessage
                );

                // Insert WorkflowEvent
                await _repository.InsertTradeWorkflowEventAsync(new TradeWorkflowEvent
                {
                    StpTradeId = response.StpTradeId,
                    EventType = response.IsSuccess ? "BookingConfirmed" : "BookingRejected",
                    SystemCode = SystemCode.Mx3,
                    EventTimeUtc = DateTime.UtcNow,
                    InitiatorId = "MX3_WATCHER",
                    Description = response.IsSuccess
                        ? $"MX3 Trade ID: {response.Mx3TradeId}, Contract ID: {response.Mx3ContractId}"
                        : $"Errors: {response.ErrorMessage}"
                });

                Debug.WriteLine($"[Watcher] ✅ Updated StpTradeId {response.StpTradeId}: {status}");

                // TODO: Arkivera fil? Eller låt MX3 hantera det?
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Watcher] Error processing file: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
