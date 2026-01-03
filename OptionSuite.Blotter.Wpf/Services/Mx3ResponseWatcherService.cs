using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FxTradeHub.Domain.Entities;
using FxTradeHub.Domain.Enums;
using FxTradeHub.Domain.Interfaces;
using FxSharedConfig;

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

                // Uppdatera status (4 parametrar: stpTradeId, systemCode, status, lastError)
                var status = response.IsSuccess ? "BOOKED" : "ERROR";
                var errorMsg = response.IsSuccess ? null : response.ErrorMessage;

                await _repository.UpdateTradeSystemLinkStatusAsync(
                    response.StpTradeId,
                    "MX3",
                    status,
                    errorMsg
                );

                // Insert WorkflowEvent (5 parametrar: stpTradeId, eventType, systemCode, userId, details)
                var eventType = response.IsSuccess ? "BookingConfirmed" : "BookingRejected";
                var details = response.IsSuccess
                    ? $"MX3 Trade ID: {response.Mx3TradeId}, Contract ID: {response.Mx3ContractId}"
                    : $"Errors: {response.ErrorMessage}";

                await _repository.InsertTradeWorkflowEventAsync(
                    response.StpTradeId,
                    eventType,
                    "MX3",
                    "MX3_WATCHER",
                    details
                );

                Debug.WriteLine($"[Watcher] ✅ Updated StpTradeId {response.StpTradeId}: {status}");

                // Arkivera alla 3 filer för denna trade
                ArchiveResponseFiles(response.StpTradeId, filePath);

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Watcher] Error processing file: {ex.Message}");
            }
        }

        private void ArchiveResponseFiles(long stpTradeId, string processedFileName)
        {
            try
            {
                var archiveFolder = AppPaths.Mx3ArchiveFolder;

                // Skapa archive-mapp om den inte finns
                if (!Directory.Exists(archiveFolder))
                {
                    Directory.CreateDirectory(archiveFolder);
                }

                // Extrahera original filename från processed file
                var fileName = Path.GetFileName(processedFileName);
                var underscoreIndex = fileName.IndexOf('_');
                if (underscoreIndex <= 0)
                {
                    Debug.WriteLine($"[Watcher] Cannot determine base pattern from: {fileName}");
                    return;
                }

                var originalFileName = fileName.Substring(0, underscoreIndex);

                // Hitta alla 3 filer för denna trade
                var basePattern = $"{originalFileName}_*";
                var allFiles = Directory.GetFiles(_responseFolder, basePattern);

                foreach (var file in allFiles)
                {
                    var fn = Path.GetFileName(file);
                    var archivePath = Path.Combine(archiveFolder, fn);

                    // Flytta till archive (overwrite om den redan finns)
                    File.Move(file, archivePath, overwrite: true);
                    Debug.WriteLine($"[Watcher] Archived: {fn}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Watcher] Archive failed: {ex.Message}");
            }
        }




        public void Dispose()
        {
            Stop();
        }
    }
}
