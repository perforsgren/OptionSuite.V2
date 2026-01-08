using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using FxSharedConfig;
using FxTradeHub.Services.Ingest;
using FxTradeHub.Services.Parsing;

namespace OptionSuite.Blotter.Wpf.Services
{
    /// <summary>
    /// Pollar email inbox folder för nya email-filer från Power Automate.
    /// Använder timer-baserad polling istället för FileSystemWatcher för att hantera OneDrive sync delay.
    /// </summary>
    public sealed class EmailInboxWatcherService : IDisposable
    {
        private readonly FileInboxService _fileInboxService;
        private readonly MessageInParserOrchestrator _parserOrchestrator;
        private readonly string _inboxFolder;
        private DispatcherTimer _pollTimer;
        private bool _isPolling;
        private bool _disposed;

        private readonly HashSet<string> _processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Skapar en ny instans av EmailInboxWatcherService.
        /// </summary>
        /// <param name="fileInboxService">Service för att läsa email-filer och skapa MessageIn.</param>
        /// <param name="parserOrchestrator">Orchestrator för att parsa MessageIn till Trade.</param>
        /// <param name="inboxFolder">Path till folder som ska pollas (från AppPaths.EmailInboxFolder).</param>
        public EmailInboxWatcherService(
            FileInboxService fileInboxService,
            MessageInParserOrchestrator parserOrchestrator,
            string inboxFolder)
        {
            _fileInboxService = fileInboxService ?? throw new ArgumentNullException(nameof(fileInboxService));
            _parserOrchestrator = parserOrchestrator ?? throw new ArgumentNullException(nameof(parserOrchestrator));
            _inboxFolder = inboxFolder ?? throw new ArgumentNullException(nameof(inboxFolder));
        }

        /// <summary>
        /// Startar polling timer och kör initial startup scan.
        /// </summary>
        public void Start()
        {
            if (_isPolling || _disposed)
                return;

            Debug.WriteLine($"[EmailWatcher] Starting polling on: {_inboxFolder}");

            // Skapa folder om den inte finns
            if (!Directory.Exists(_inboxFolder))
            {
                Debug.WriteLine($"[EmailWatcher] Inbox folder does not exist, creating: {_inboxFolder}");
                Directory.CreateDirectory(_inboxFolder);
            }

            // Initial startup scan
            _ = Task.Run(async () => await ScanInboxAsync());

            // Starta polling timer
            _pollTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _pollTimer.Tick += async (s, e) => await ScanInboxAsync();
            _pollTimer.Start();

            _isPolling = true;
        }

        /// <summary>
        /// Stoppar polling timer.
        /// </summary>
        public void Stop()
        {
            if (!_isPolling)
                return;

            Debug.WriteLine("[EmailWatcher] Stopping polling");

            if (_pollTimer != null)
            {
                _pollTimer.Stop();
                _pollTimer = null;
            }

            _isPolling = false;
            _processedFiles.Clear();
        }

        /// <summary>
        /// Scannar inbox folder för nya .txt filer som inte processats än.
        /// </summary>
        private async Task ScanInboxAsync()
        {
            try
            {
                if (!Directory.Exists(_inboxFolder))
                    return;

                var files = Directory.GetFiles(_inboxFolder, "*.txt")
                    .OrderBy(f => File.GetCreationTime(f)) // Processar äldsta först
                    .ToArray();

                if (files.Length == 0)
                    return;

                var newFilesCount = 0;

                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);

                    // Deduplication - skippa redan processade filer
                    if (_processedFiles.Contains(fileName))
                        continue;

                    newFilesCount++;
                    await ProcessEmailFileAsync(file);
                }

                if (newFilesCount > 0)
                {
                    Debug.WriteLine($"[EmailWatcher] Scan complete. Processed {newFilesCount} new file(s).");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EmailWatcher] Scan error: {ex.Message}");
            }
        }

        /// <summary>
        /// Processar en email-fil: FileInboxService → MessageIn → Parser → Trade.
        /// </summary>
        private async Task ProcessEmailFileAsync(string filePath)
        {
            var fileName = Path.GetFileName(filePath);

            // Markera som processad
            _processedFiles.Add(fileName);

            try
            {
                Debug.WriteLine($"[EmailWatcher] Processing file: {fileName}");

                // 1. FileInboxService → MessageIn
                var messageInId = _fileInboxService.ProcessEmailFile(filePath);

                if (messageInId <= 0)
                {
                    Debug.WriteLine($"[EmailWatcher] Failed to create MessageIn from {fileName}");
                    return;
                }

                Debug.WriteLine($"[EmailWatcher] Created MessageIn {messageInId} from {fileName}");

                // 2. MessageInParserOrchestrator → Trade
                await Task.Run(() => _parserOrchestrator.ProcessMessage(messageInId));

                Debug.WriteLine($"[EmailWatcher] Processed MessageIn {messageInId} successfully");

                // 3. Arkivera fil
                ArchiveFile(filePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EmailWatcher] Error processing {fileName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Arkiverar processad fil till EmailArchiveFolder från config.
        /// </summary>
        private void ArchiveFile(string filePath)
        {
            try
            {
                var archiveFolder = AppPaths.EmailArchiveFolder.Replace("{USERNAME}", Environment.UserName);

                if (!Directory.Exists(archiveFolder))
                {
                    Directory.CreateDirectory(archiveFolder);
                }

                var fileName = Path.GetFileName(filePath);
                var archivePath = Path.Combine(archiveFolder, fileName);

                // Om fil redan finns i archive, lägg till timestamp
                if (File.Exists(archivePath))
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    fileName = $"{nameWithoutExt}_{timestamp}{ext}";
                    archivePath = Path.Combine(archiveFolder, fileName);
                }

                File.Move(filePath, archivePath);
                Debug.WriteLine($"[EmailWatcher] Archived {fileName} to {archiveFolder}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EmailWatcher] Failed to archive {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Disposes resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            Stop();
            _disposed = true;
        }
    }
}
