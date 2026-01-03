using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FxTradeHub.Domain.Interfaces;

namespace OptionSuite.Blotter.Wpf.Services
{
    /// <summary>
    /// Kör leader election var 10-15:e sekund.
    /// Avgör om denna instans ska vara master.
    /// </summary>
    public sealed class MasterElectionService : IDisposable
    {
        private readonly IStpRepositoryAsync _repository;
        private readonly string _userName;
        private readonly string _machineName;
        private readonly Timer _electionTimer;

        private bool _isMaster;

        public bool IsMaster => _isMaster;

        /// <summary>
        /// Event som triggas när master-status ändras.
        /// </summary>
        public event EventHandler<bool> MasterStatusChanged;

        public MasterElectionService(IStpRepositoryAsync repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _userName = Environment.UserName;
            _machineName = Environment.MachineName;

            // Timer: 12 sekunder intervall (mellan 10-15)
            _electionTimer = new Timer(
                callback: async _ => await RunElectionAsync(),
                state: null,
                dueTime: TimeSpan.FromSeconds(2),  // vänta lite innan första election
                period: TimeSpan.FromSeconds(12)
            );
        }

        private async Task RunElectionAsync()
        {
            try
            {
                // Steg 1: Hämta online-användare
                var onlineUsers = await _repository.GetOnlineUsersAsync();

                // Steg 2: Hämta prio-lista
                var priorityChain = await _repository.GetMasterPriorityAsync();

                // Steg 3: Hitta första online-användare i kedjan
                var candidateMaster = priorityChain.FirstOrDefault(u => onlineUsers.Contains(u));

                if (string.IsNullOrEmpty(candidateMaster))
                {
                    Debug.WriteLine("[Election] No candidate found (no one online?)");
                    UpdateMasterStatus(false);
                    return;
                }

                // Steg 4: Försök ta locket om vi är kandidaten
                if (candidateMaster.Equals(_userName, StringComparison.OrdinalIgnoreCase))
                {
                    var acquired = await _repository.TryAcquireMasterLockAsync(
                        "BookingStatusWatcher",
                        _userName,
                        _machineName
                    );

                    if (acquired)
                    {
                        Debug.WriteLine($"[Election] ✅ I AM MASTER ({_userName})");
                        UpdateMasterStatus(true);
                    }
                    else
                    {
                        Debug.WriteLine($"[Election] Failed to acquire lock (race condition?)");
                        UpdateMasterStatus(false);
                    }
                }
                else
                {
                    Debug.WriteLine($"[Election] Not master. Candidate is: {candidateMaster}");
                    UpdateMasterStatus(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Election] Error: {ex.Message}");
                UpdateMasterStatus(false);
            }
        }

        private void UpdateMasterStatus(bool isMaster)
        {
            if (_isMaster != isMaster)
            {
                _isMaster = isMaster;
                MasterStatusChanged?.Invoke(this, isMaster);
            }
        }

        public void Dispose()
        {
            _electionTimer?.Dispose();
        }
    }
}
