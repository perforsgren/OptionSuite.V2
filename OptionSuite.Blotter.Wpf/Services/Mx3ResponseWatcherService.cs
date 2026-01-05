using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FxTradeHub.Domain.Interfaces;
using FxTradeHub.Domain.Entities;
using FxSharedConfig;

namespace OptionSuite.Blotter.Wpf.Services
{
    public sealed class Mx3ResponseWatcherService : IDisposable
    {
        private readonly IStpRepositoryAsync _repository;
        private readonly string _responseFolder;
        private FileSystemWatcher _watcher;
        private bool _isWatching;

        // Deduplication - spara redan processade filer
        private readonly HashSet<string> _processedFiles = new HashSet<string>();

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
                Filter = "*_2.xml",
                NotifyFilter = NotifyFilters.FileName,  // BARA FileName, INTE LastWrite
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileCreated;  // BARA Created, INTE Changed

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
            _processedFiles.Clear();
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
                    Debug.WriteLine($"[Watcher] Already processed: {fileName}, skipping.");
                    return;
                }
                _processedFiles.Add(fileName);
            }

            try
            {
                Debug.WriteLine($"[Watcher] Processing file: {fileName}");

                // Parse response
                var response = Mx3ResponseParserService.Parse(_responseFolder, filePath);

                if (response.StpTradeId == 0)
                {
                    Debug.WriteLine($"[Watcher] Failed to parse StpTradeId, skipping.");
                    return;
                }

                // VIKTIGT: Kolla om trade finns i DB först
                var tradeExists = await _repository.GetTradeByIdAsync(response.StpTradeId);
                if (tradeExists == null)
                {
                    Debug.WriteLine($"[Watcher] ⚠️ StpTradeId {response.StpTradeId} does NOT exist in database. Skipping.");
                    // Arkivera ändå så vi inte processar om och om igen
                    ArchiveResponseFiles(response.StpTradeId, filePath);
                    return;
                }

                // Uppdatera status
                var status = response.IsSuccess ? "BOOKED" : "ERROR";
                //var errorMsg = response.IsSuccess ? null : response.ErrorMessage;

                //await _repository.UpdateTradeSystemLinkStatusAsync(
                //    response.StpTradeId,
                //    "MX3",
                //    status,
                //    errorMsg
                //);

                await _repository.UpdateTradeSystemLinkOnResponseAsync(
                    stpTradeId: response.StpTradeId,
                    systemCode: "MX3",
                    status: response.IsSuccess ? "BOOKED" : "ERROR",
                    systemTradeId: response.Mx3ContractId,  // <- NYTT: MX3 ContractID
                    lastError: response.IsSuccess ? null : response.ErrorMessage
                );

                // Insert WorkflowEvent
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
                Debug.WriteLine($"[Watcher] ❌ Error processing file: {ex.Message}");

                // Ta bort från processed-listan så vi kan försöka igen
                lock (_processedFiles)
                {
                    _processedFiles.Remove(fileName);
                }
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
                    Debug.WriteLine($"[Watcher] 📁 Archived: {fn}");
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
