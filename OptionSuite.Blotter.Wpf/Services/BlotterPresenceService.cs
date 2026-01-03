using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FxTradeHub.Domain.Interfaces;

namespace OptionSuite.Blotter.Wpf.Services
{
    /// <summary>
    /// Skickar heartbeat till DB var 10:e sekund.
    /// </summary>
    public sealed class BlotterPresenceService : IDisposable
    {
        private readonly IStpRepositoryAsync _repository;
        private readonly string _nodeId;
        private readonly string _userName;
        private readonly string _machineName;
        private readonly Timer _heartbeatTimer;

        public BlotterPresenceService(IStpRepositoryAsync repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));

            // NodeId = UserName@MachineName
            _userName = Environment.UserName;
            _machineName = Environment.MachineName;
            _nodeId = $"{_userName}@{_machineName}";

            // Timer: 10 sekunder intervall
            _heartbeatTimer = new Timer(
                callback: async _ => await SendHeartbeatAsync(),
                state: null,
                dueTime: TimeSpan.Zero,  // starta direkt
                period: TimeSpan.FromSeconds(10)
            );
        }

        private async Task SendHeartbeatAsync()
        {
            try
            {
                await _repository.UpdatePresenceAsync(_nodeId, _userName, _machineName);
                Debug.WriteLine($"[Presence] Heartbeat sent: {_nodeId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Presence] Heartbeat failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _heartbeatTimer?.Dispose();
        }
    }
}
