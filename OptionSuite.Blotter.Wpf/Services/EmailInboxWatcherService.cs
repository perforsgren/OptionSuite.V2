using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FxTradeHub.Services.Ingest;
using FxTradeHub.Services.Parsing;
using FxSharedConfig;

namespace OptionSuite.Blotter.Wpf.Services
{
    /// <summary>
    /// Watchas email inbox folder för nya email-filer från Power Automate.
    /// Processar filer genom FileInboxService och MessageInParserOrchestrator.
    /// </summary>
    public sealed class EmailInboxWatcherService : IDisposable
    {
        private readonly FileInboxService _fileInboxService;
        private readonly MessageInParserOrchestrator _parserOrchestrator;
        private readonly string _inboxFolder;
        private FileSystemWatcher _watcher;
        private bool _isWatching;
        private bool _disposed;

        private readonly HashSet<string> _processedFiles = new HashSet<string>();

        /// <summary>
        /// Skapar en ny instans av EmailInboxWatcherService.
        /// </summary>
        /// <param name="fileInboxService">Service för att läsa email-filer och skapa MessageIn.</param>
        /// <param name="parserOrchestrator">Orchestrator för att parsa MessageIn till Trade.</param>
        /// <param name="inboxFolder">Path till folder som ska watchas (från AppPaths.EmailInboxFolder).</param>
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
        /// Startar FileSystemWatcher och kör startup scan för att plocka upp missade filer.
        /// </summary>
        public void Start()
        {
            if (_isWatching || _disposed)
                return;

            Debug.WriteLine($"[EmailWatcher] Starting FileSystemWatcher on: {_inboxFolder}");

            // Skapa folder om den inte finns
            if (!Directory.Exists(_inboxFolder))
            {
                Debug.WriteLine($"[EmailWatcher] Inbox folder does not exist, creating: {_inboxFolder}");
                Directory.CreateDirectory(_inboxFolder);
            }

            // Startup scan (plocka upp filer som missades)
            _ = Task.Run(async () => await StartupScanAsync());

            // Starta FileSystemWatcher
            _watcher = new FileSystemWatcher(_inboxFolder)
            {
                Filter = "*.txt",
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileCreated;

            _isWatching = true;
        }

        /// <summary>
        /// Stoppar FileSystemWatcher.
        /// </summary>
        public void Stop()
        {
            if (!_isWatching)
                return;

            Debug.WriteLine("[EmailWatcher] Stopping FileSystemWatcher");

            if (_watcher != null)
            {
                _watcher.Created -= OnFileCreated;
                _watcher.Dispose();
                _watcher = null;
            }

            _isWatching = false;
            _processedFiles.Clear();
        }

        /// <summary>
        /// Scannar inbox folder vid startup för att plocka upp filer som missades.
        /// </summary>
        private async Task StartupScanAsync()
        {
            try
            {
                Debug.WriteLine("[EmailWatcher] Running startup scan...");

                var files = Directory.GetFiles(_inboxFolder, "*.txt");

                foreach (var file in files)
                {
                    await ProcessEmailFileAsync(file);
                }

                Debug.WriteLine($"[EmailWatcher] Startup scan complete. Processed {files.Length} files.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EmailWatcher] Startup scan error: {ex.Message}");
            }
        }

        /// <summary>
        /// Event handler för FileSystemWatcher.Created.
        /// </summary>
        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            // Vänta lite för att Power Automate ska bli klar med att skriva
            await Task.Delay(500);
            await ProcessEmailFileAsync(e.FullPath);
        }

        /// <summary>
        /// Processar en email-fil: FileInboxService → MessageIn → Parser → Trade.
        /// </summary>
        private async Task ProcessEmailFileAsync(string filePath)
        {
            var fileName = Path.GetFileName(filePath);

            // Deduplication
            lock (_processedFiles)
            {
                if (_processedFiles.Contains(fileName))
                {
                    Debug.WriteLine($"[EmailWatcher] Already processed: {fileName}, skipping.");
                    return;
                }
                _processedFiles.Add(fileName);
            }

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

                // 3. Arkivera eller radera fil (optional)
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
