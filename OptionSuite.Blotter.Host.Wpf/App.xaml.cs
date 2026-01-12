using System;
using System.Windows;
using FxTradeHub.Data.MySql.Repositories;
using FxSharedConfig;
using OptionSuite.Blotter.Wpf.Services;
using FxTradeHub.Services.Ingest;
using FxTradeHub.Services.Parsing;
using FxTradeHub.Domain.Parsing;
using System.Collections.Generic;
using FxTradeHub.Domain.Interfaces;

namespace OptionSuite.Blotter.Host.Wpf
{
    public partial class App : Application
    {
        private BlotterPresenceService _presenceService;
        private MasterElectionService _electionService;
        private Mx3ResponseWatcherService _responseWatcher;
        private CalypsoResponseWatcherService _calypsoResponseWatcher;
        private EmailInboxWatcherService _emailWatcher;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // 1. Skapa repositories (både sync och async)
                var connectionString = AppDbConfig.GetConnectionString("trade_stp");
                var repositoryAsync = new MySqlStpRepositoryAsync(connectionString);
                var repositorySync = new MySqlStpRepository(connectionString);
                var messageInRepo = new MessageInRepository(connectionString);
                var lookupRepo = new MySqlStpLookupRepository(connectionString);

                // 2. Starta Presence Service (heartbeat)
                _presenceService = new BlotterPresenceService(repositoryAsync);

                // 3. Starta Election Service
                _electionService = new MasterElectionService(repositoryAsync);

                // 4. Skapa Mx3 FileWatcher (startas endast när vi blir master)
                var responseFolder = AppPaths.Mx3ResponseFolder;
                _responseWatcher = new Mx3ResponseWatcherService(repositoryAsync, responseFolder);

                // 5. Skapa Calypso FileWatcher (startas endast när vi blir master)
                var calypsoResponseFolder = AppPaths.CalypsoResponseFolder;
                _calypsoResponseWatcher = new CalypsoResponseWatcherService(repositoryAsync, calypsoResponseFolder);

                // 6. Skapa Email FileWatcher (startas ALLTID - varje user har sitt eget OneDrive)
                var emailInboxFolder = AppPaths.EmailInboxFolder.Replace("{USERNAME}", Environment.UserName);
                var messageInService = new MessageInService(messageInRepo);
                var fileInboxService = new FileInboxService(messageInService);

                // Skapa parsers för MessageInParserOrchestrator
                var parsers = new List<IInboundMessageParser>
                {
                    new VolbrokerFixAeParser(lookupRepo),
                    new JpmSpotConfirmationParser(lookupRepo),
                    new BarclaysSpotConfirmationParser(lookupRepo),
                    new NatWestSpotConfirmationParser(lookupRepo)
                };

                var parserOrchestrator = new MessageInParserOrchestrator(messageInRepo, repositorySync, parsers);

                _emailWatcher = new EmailInboxWatcherService(fileInboxService, parserOrchestrator, emailInboxFolder);
                _emailWatcher.Start();  // Starta ALLTID

                // 7. Lyssna på master-status ändringar (för Mx3 watcher)
                _electionService.MasterStatusChanged += OnMasterStatusChanged;

                System.Diagnostics.Debug.WriteLine("[App] All services initialized successfully");
            }

            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to initialize blotter services:\n\n{ex.Message}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                Shutdown(1);
            }
        }

        private void OnMasterStatusChanged(object sender, bool isMaster)
        {
            if (isMaster)
            {
                System.Diagnostics.Debug.WriteLine("[App] ✅ I AM MASTER - Starting Mx3 & Calyso FileWatcher");
                _responseWatcher?.Start();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[App] ❌ NOT MASTER - Stopping Mx3 & Calypso  FileWatcher");
                _responseWatcher?.Stop();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[App] Shutting down services...");

            // Stoppa alla services
            _emailWatcher?.Stop();
            _emailWatcher?.Dispose();

            _responseWatcher?.Stop();
            _responseWatcher?.Dispose();

            _calypsoResponseWatcher?.Stop();
            _calypsoResponseWatcher?.Dispose();

            _electionService?.Dispose();
            _presenceService?.Dispose();

            base.OnExit(e);
        }
    }
}
