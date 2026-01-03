using System;
using System.Windows;
using FxTradeHub.Data.MySql.Repositories;
using FxSharedConfig;
using OptionSuite.Blotter.Wpf.Services;

namespace OptionSuite.Blotter.Host.Wpf
{
    public partial class App : Application
    {
        private BlotterPresenceService _presenceService;
        private MasterElectionService _electionService;
        private Mx3ResponseWatcherService _responseWatcher;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // 1. Skapa repository (shared av alla services)
                var connectionString = AppDbConfig.GetConnectionString("trade_stp");
                var repository = new MySqlStpRepositoryAsync(connectionString);

                // 2. Starta Presence Service (heartbeat)
                _presenceService = new BlotterPresenceService(repository);

                // 3. Starta Election Service
                _electionService = new MasterElectionService(repository);

                // 4. Skapa FileWatcher (startas endast när vi blir master)
                var responseFolder = AppPaths.Mx3ResponseFolder;
                _responseWatcher = new Mx3ResponseWatcherService(repository, responseFolder);

                // 5. Lyssna på master-status ändringar
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
                System.Diagnostics.Debug.WriteLine("[App] ✅ I AM MASTER - Starting FileWatcher");
                _responseWatcher?.Start();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[App] ❌ NOT MASTER - Stopping FileWatcher");
                _responseWatcher?.Stop();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("[App] Shutting down services...");

            // Stoppa alla services
            _responseWatcher?.Stop();
            _responseWatcher?.Dispose();

            _electionService?.Dispose();
            _presenceService?.Dispose();

            base.OnExit(e);
        }
    }
}
