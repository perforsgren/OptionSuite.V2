using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using FxTradeHub.Contracts.Dtos;
using FxTradeHub.Services;
using OptionSuite.Blotter.Wpf.Infrastructure;
using System.Windows.Data;

namespace OptionSuite.Blotter.Wpf.ViewModels
{
    public sealed class BlotterRootViewModel : INotifyPropertyChanged
    {
        private readonly IBlotterReadServiceAsync _readService;

        private TradeRowViewModel _selectedOptionTrade;
        private TradeRowViewModel _selectedLinearTrade;

        private bool _isBusy;
        private string _lastError;
        private bool _isStale;
        private DateTime? _lastRefreshUtc;     
        private TimeSpan? _lastRefreshDuration;

        private int _totalTrades;
        private int _newCount;
        private int _pendingCount; 
        private int _bookedCount;
        private int _errorCount;

        // grid filtering

        private ICollectionView _optionTradesView;
        private ICollectionView _linearTradesView;
        private string _currentStatusFilter = "ALL";

        // state för diff
        private readonly HashSet<string> _seenTradeIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _lastSignatureByTradeId = new Dictionary<string, string>(StringComparer.Ordinal);

        // reentrancy-skydd (knapp + timer delar samma spärr)
        private int _refreshInFlight; // 0/1

        // user input gate (debounce)
        private bool _isUserInteracting;
        private readonly DispatcherTimer _userInteractionDebounceTimer;

        // Polling
        private readonly DispatcherTimer _pollTimer;

        // Exponential backoff för error retry
        private int _consecutiveErrors = 0;
        private readonly int[] _backoffIntervals = { 2, 4, 8, 16, 30 };  // sekunder
        private readonly TimeSpan _normalPollInterval = TimeSpan.FromSeconds(2);

        public event PropertyChangedEventHandler PropertyChanged;

        public string Title { get; set; } = "Trade Blotter";
        public string Subtitle { get; set; } = "v2";

        public ObservableCollection<TradeRowViewModel> OptionTrades { get; } =
            new ObservableCollection<TradeRowViewModel>();

        public ObservableCollection<TradeRowViewModel> LinearTrades { get; } =
            new ObservableCollection<TradeRowViewModel>();

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    OnPropertyChanged(nameof(IsBusy));
                }
            }
        }

        public string LastError
        {
            get => _lastError;
            private set
            {
                if (!string.Equals(_lastError, value, StringComparison.Ordinal))
                {
                    _lastError = value;
                    OnPropertyChanged(nameof(LastError));
                }
            }
        }

        public bool IsStale
        {
            get => _isStale;
            private set
            {
                if (_isStale != value)
                {
                    _isStale = value;
                    OnPropertyChanged(nameof(IsStale));
                }
            }
        }

        public DateTime? LastRefreshUtc
        {
            get => _lastRefreshUtc;
            private set
            {
                if (_lastRefreshUtc != value)
                {
                    _lastRefreshUtc = value;
                    OnPropertyChanged(nameof(LastRefreshUtc));
                }
            }
        }

        public TimeSpan? LastRefreshDuration
        {
            get => _lastRefreshDuration;
            private set
            {
                if (_lastRefreshDuration != value)
                {
                    _lastRefreshDuration = value;
                    OnPropertyChanged(nameof(LastRefreshDuration));
                }
            }
        }


        /// <summary>
        /// True när användaren scrollar/klickar/editerar. Polling skippar då refresh.
        /// Den här flaggan sätts via NotifyUserInteraction() (från View).
        /// </summary>
        public bool IsUserInteracting
        {
            get => _isUserInteracting;
            private set
            {
                if (_isUserInteracting != value)
                {
                    _isUserInteracting = value;
                    OnPropertyChanged(nameof(IsUserInteracting));
                }
            }
        }

        public int TotalTrades
        {
            get => _totalTrades;
            private set
            {
                if (_totalTrades != value)
                {
                    _totalTrades = value;
                    OnPropertyChanged(nameof(TotalTrades));
                }
            }
        }

        public int NewCount
        {
            get => _newCount;
            private set
            {
                if (_newCount != value)
                {
                    _newCount = value;
                    OnPropertyChanged(nameof(NewCount));
                }
            }
        }

        public int BookedCount
        {
            get => _bookedCount;
            private set
            {
                if (_bookedCount != value)
                {
                    _bookedCount = value;
                    OnPropertyChanged(nameof(BookedCount));
                }
            }
        }

        public int ErrorCount
        {
            get => _errorCount;
            private set
            {
                if (_errorCount != value)
                {
                    _errorCount = value;
                    OnPropertyChanged(nameof(ErrorCount));
                }
            }
        }

        public int PendingCount
        {
            get => _pendingCount;
            private set
            {
                if (_pendingCount != value)
                {
                    _pendingCount = value;
                    OnPropertyChanged(nameof(PendingCount));
                }
            }
        }

        public ICollectionView OptionTradesView
        {
            get => _optionTradesView;
            private set
            {
                if (_optionTradesView != value)
                {
                    _optionTradesView = value;
                    OnPropertyChanged(nameof(OptionTradesView));
                }
            }
        }

        public ICollectionView LinearTradesView
        {
            get => _linearTradesView;
            private set
            {
                if (_linearTradesView != value)
                {
                    _linearTradesView = value;
                    OnPropertyChanged(nameof(LinearTradesView));
                }
            }
        }


        public ICommand RefreshCommand { get; }

        // Context Menu Commands
        public ICommand BookTradeCommand { get; }
        public ICommand DuplicateTradeCommand { get; }
        public ICommand CopyToManualInputCommand { get; }
        public ICommand BookAsLiveTradeCommand { get; }
        public ICommand CancelCalypsoTradeCommand { get; }
        public ICommand DeleteRowCommand { get; }
        public ICommand CheckIfBookedCommand { get; }
        public ICommand OpenErrorLogCommand { get; }

        public ICommand SetFilterAllCommand { get; }
        public ICommand SetFilterNewCommand { get; }
        public ICommand SetFilterPendingCommand { get; }
        public ICommand SetFilterBookedCommand { get; }
        public ICommand SetFilterErrorsCommand { get; }


        public TradeRowViewModel SelectedOptionTrade
        {
            get => _selectedOptionTrade;
            set
            {
                if (_selectedOptionTrade != value)
                {
                    _selectedOptionTrade = value;
                    OnPropertyChanged(nameof(SelectedOptionTrade));
                    OnPropertyChanged(nameof(SelectedTrade));

                    if (value != null && _selectedLinearTrade != null)
                    {
                        SelectedLinearTrade = null;
                    }
                }
            }
        }

        public TradeRowViewModel SelectedLinearTrade
        {
            get => _selectedLinearTrade;
            set
            {
                if (_selectedLinearTrade != value)
                {
                    _selectedLinearTrade = value;
                    OnPropertyChanged(nameof(SelectedLinearTrade));
                    OnPropertyChanged(nameof(SelectedTrade));

                    if (value != null && _selectedOptionTrade != null)
                    {
                        SelectedOptionTrade = null;
                    }
                }
            }
        }

        public TradeRowViewModel SelectedTrade => _selectedOptionTrade ?? _selectedLinearTrade;

        public BlotterRootViewModel(IBlotterReadServiceAsync readService)
        {
            _readService = readService ?? throw new ArgumentNullException(nameof(readService));

            RefreshCommand = new RelayCommand(async () => await RefreshAsync().ConfigureAwait(true));

            // Context Menu Commands
            BookTradeCommand = new RelayCommand(() => ExecuteBookTrade(), () => CanExecuteBookTrade());
            DuplicateTradeCommand = new RelayCommand(() => ExecuteDuplicateTrade(), () => CanExecuteDuplicateTrade());
            CopyToManualInputCommand = new RelayCommand(() => ExecuteCopyToManualInput(), () => CanExecuteCopyToManualInput());
            BookAsLiveTradeCommand = new RelayCommand(() => ExecuteBookAsLiveTrade(), () => CanExecuteBookAsLiveTrade());
            CancelCalypsoTradeCommand = new RelayCommand(() => ExecuteCancelCalypsoTrade(), () => CanExecuteCancelCalypsoTrade());
            DeleteRowCommand = new RelayCommand(() => ExecuteDeleteRow(), () => CanExecuteDeleteRow());
            CheckIfBookedCommand = new RelayCommand(() => ExecuteCheckIfBooked(), () => CanExecuteCheckIfBooked());
            OpenErrorLogCommand = new RelayCommand(() => ExecuteOpenErrorLog(), () => CanExecuteOpenErrorLog());

            SetFilterAllCommand = new RelayCommand(() => SetStatusFilter("ALL"));
            SetFilterNewCommand = new RelayCommand(() => SetStatusFilter("NEW"));
            SetFilterPendingCommand = new RelayCommand(() => SetStatusFilter("PENDING"));
            SetFilterBookedCommand = new RelayCommand(() => SetStatusFilter("BOOKED"));
            SetFilterErrorsCommand = new RelayCommand(() => SetStatusFilter("ERRORS"));

            // Skapa collection views för filtrering
            _optionTradesView = CollectionViewSource.GetDefaultView(OptionTrades);
            _linearTradesView = CollectionViewSource.GetDefaultView(LinearTrades);
            _optionTradesView.Filter = FilterTrade;
            _linearTradesView.Filter = FilterTrade;


            // debounce-timer som släcker IsUserInteracting efter kort “idle”
            _userInteractionDebounceTimer = new DispatcherTimer(DispatcherPriority.Background);
            _userInteractionDebounceTimer.Interval = TimeSpan.FromMilliseconds(800);
            _userInteractionDebounceTimer.Tick += (s, e) =>
            {
                _userInteractionDebounceTimer.Stop();
                IsUserInteracting = false;
            };

            // poll-timer (tick = samma refresh som knappen)
            _pollTimer = new DispatcherTimer(DispatcherPriority.Background);
            _pollTimer.Interval = TimeSpan.FromSeconds(2);
            _pollTimer.Tick += async (s, e) =>
            {
                // defensiva gates direkt i tick (snabb exit)
                if (IsBusy) return;
                if (IsUserInteracting) return;
                if (Volatile.Read(ref _refreshInFlight) != 0) return;

                await RefreshAsync().ConfigureAwait(true);
            };
        }

        public Task InitialLoadAsync()
        {
            return RefreshAsync();
        }

        public void StartPolling(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(interval));

            _pollTimer.Interval = interval;

            if (!_pollTimer.IsEnabled)
                _pollTimer.Start();
        }

        public void StopPolling()
        {
            if (_pollTimer.IsEnabled)
                _pollTimer.Stop();
        }

        /// <summary>
        /// Kallas från View vid scroll/mouse/keyboard/edit för att pausa polling kort.
        /// </summary>
        public void NotifyUserInteraction()
        {
            IsUserInteracting = true;

            // restart debounce
            _userInteractionDebounceTimer.Stop();
            _userInteractionDebounceTimer.Start();
        }

        private async Task RefreshAsync()
        {
            // gemensam spärr för knapp + timer
            if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0)
            {
                return;
            }

            try
            {
                // defensiva gates (även om knappen trycks)
                if (IsBusy) return;
                if (IsUserInteracting) return;

                // kom ihåg selection (id + vilken grid)
                var selectedOptionId = SelectedOptionTrade?.TradeId;
                var selectedLinearId = SelectedLinearTrade?.TradeId;

                try
                {
                    IsBusy = true;

                    // Reset counts
                    int newCount = 0;
                    int pendingCount = 0;
                    int bookedCount = 0;
                    int errorCount = 0;

                    // ═══════════════════════════════════════════════════════════
                    // MÄTNING - starta stopwatch
                    // ═══════════════════════════════════════════════════════════
                    var sw = Stopwatch.StartNew();

                    var filter = new BlotterFilter
                    {
                        FromTradeDate = DateTime.UtcNow.Date.AddDays(-30),
                        ToTradeDate = DateTime.UtcNow.Date.AddDays(1),
                        MaxRows = 500
                    };

                    // FETCH (kan kasta exception)
                    var rows = await _readService.GetBlotterTradesAsync(filter).ConfigureAwait(true);

                    // ═══════════════════════════════════════════════════════════
                    // APPLY - Bygg nya listor LOKALT först (non-destructive!)
                    // ═══════════════════════════════════════════════════════════

                    var newOptionTrades = new List<TradeRowViewModel>();
                    var newLinearTrades = new List<TradeRowViewModel>();

                    // bygg nästa "snapshot" av signatures
                    var nextSignatures = new Dictionary<string, string>(StringComparer.Ordinal);

                    var isColdStart = _seenTradeIds.Count == 0;

                    foreach (var r in rows)
                    {
                        var tradeId = r.TradeId ?? string.Empty;

                        var time = (r.ExecutionTimeUtc ?? r.TradeDate ?? DateTime.UtcNow);

                        var system = !string.IsNullOrWhiteSpace(r.SourceVenueCode)
                            ? r.SourceVenueCode
                            : r.SourceType;

                        var fallbackStatus = !string.IsNullOrWhiteSpace(r.Mx3Status)
                            ? r.Mx3Status
                            : r.SystemStatus;

                        var signature = BuildSignature(r, time, system, fallbackStatus);
                        nextSignatures[tradeId] = signature;

                        var isNew = !isColdStart && !_seenTradeIds.Contains(tradeId);

                        var isUpdated =
                            !isNew &&
                            _lastSignatureByTradeId.TryGetValue(tradeId, out var prevSig) &&
                            !string.Equals(prevSig, signature, StringComparison.Ordinal);

                        var trade = new TradeRowViewModel(
                            tradeId: tradeId,
                            counterparty: r.CounterpartyCode,
                            ccyPair: r.CcyPair,
                            buySell: r.BuySell,
                            callPut: r.CallPut,
                            strike: r.Strike,
                            expiryDate: r.ExpiryDate,
                            notional: r.Notional,
                            notionalCcy: r.NotionalCcy,
                            premium: r.Premium,
                            premiumCcy: r.PremiumCcy,
                            portfolioMx3: r.PortfolioMx3,
                            trader: r.TraderId,
                            status: fallbackStatus,
                            time: time,
                            system: system,
                            product: r.ProductType,
                            spotRate: r.SpotRate,
                            swapPoints: r.SwapPoints,
                            settlementDate: r.SettlementDate,
                            hedgeRate: r.HedgeRate,
                            hedgeType: r.HedgeType,
                            calypsoPortfolio: r.CalypsoPortfolio,
                            mx3Status: r.Mx3Status ?? "New",
                            calypsoStatus: r.CalypsoStatus ?? "New",
                            mic: r.Mic,
                            tvtic: r.Tvtic,
                            isin: r.Isin,
                            invDecisionId: r.InvId,
                            isNew: isNew,
                            isUpdated: isUpdated
                        );

                        if (IsOptionProduct(r.ProductType))
                        {
                            newOptionTrades.Add(trade);
                        }
                        else
                        {
                            newLinearTrades.Add(trade);
                        }

                        // "släck" highlight efter en stund
                        if (trade.IsNew)
                        {
                            _ = ClearFlagLaterAsync(trade, clearNew: true, delayMs: 20000);
                        }

                        if (trade.IsUpdated)
                        {
                            _ = ClearFlagLaterAsync(trade, clearNew: false, delayMs: 2000);
                        }

                        // Räkna status för filter badges
                        var status = trade.Status?.ToUpperInvariant() ?? "";

                        // DEBUG: Logga första 5 trades för att se vad status faktiskt är
                        if (newOptionTrades.Count + newLinearTrades.Count <= 5)
                        {
                            System.Diagnostics.Debug.WriteLine($"[DEBUG] Trade {tradeId}: Status='{trade.Status}' (upper='{status}'), IsNew={trade.IsNew}");
                        }

                        if (status == "NEW")
                        {
                            newCount++;  // ← Lokal variabel!
                        }
                        else if (status == "PENDING" || status == "PARTIAL")
                        {
                            pendingCount++;  // ← Lokal variabel!
                        }
                        else if (status == "BOOKED")
                        {
                            bookedCount++;  // ← Lokal variabel!
                        }
                        else if (status.Contains("ERROR") || status == "REJECTED" || status == "FAILED")
                        {
                            errorCount++;  // ← Lokal variabel!
                        }



                    }

                    // ═══════════════════════════════════════════════════════════
                    // COMMIT - Nu är allt OK, uppdatera UI collections
                    // ═══════════════════════════════════════════════════════════

                    OptionTrades.Clear();
                    LinearTrades.Clear();

                    foreach (var trade in newOptionTrades)
                    {
                        OptionTrades.Add(trade);
                    }

                    foreach (var trade in newLinearTrades)
                    {
                        LinearTrades.Add(trade);
                    }

                    // commit "snapshot"
                    _seenTradeIds.Clear();
                    foreach (var id in nextSignatures.Keys)
                    {
                        _seenTradeIds.Add(id);
                    }

                    _lastSignatureByTradeId.Clear();
                    foreach (var kvp in nextSignatures)
                    {
                        _lastSignatureByTradeId[kvp.Key] = kvp.Value;
                    }

                    // återställ selection (utan att "cleara" den i onödan)
                    RestoreSelection(selectedOptionId, selectedLinearId);

                    // ═══════════════════════════════════════════════════════════
                    // SUCCESS - commit health metrics + counts
                    // ═══════════════════════════════════════════════════════════

                    sw.Stop();

                    TotalTrades = newOptionTrades.Count + newLinearTrades.Count;
                    NewCount = newCount;        // ← Från lokal variabel!
                    PendingCount = pendingCount;  // ← Från lokal variabel!
                    BookedCount = bookedCount;   // ← Från lokal variabel!
                    ErrorCount = errorCount;     // ← Från lokal variabel!


                    // DEBUG: Logga counts
                    System.Diagnostics.Debug.WriteLine($"[DEBUG] Counts - Total:{TotalTrades}, New:{NewCount}, Pending:{PendingCount}, Booked:{BookedCount}, Error:{ErrorCount}");

                    LastRefreshUtc = DateTime.UtcNow;
                    LastRefreshDuration = sw.Elapsed;
                    LastError = null;
                    IsStale = false;

                    // Refresh filter views
                    _optionTradesView?.Refresh();
                    _linearTradesView?.Refresh();


                    if (_consecutiveErrors > 0)
                    {
                        _consecutiveErrors = 0;
                        if (_pollTimer.IsEnabled)
                        {
                            _pollTimer.Interval = _normalPollInterval;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // ═══════════════════════════════════════════════════════════
                    // FAILURE - behåll gamla trades, markera som stale
                    // ═══════════════════════════════════════════════════════════

                    _consecutiveErrors++;

                    // Exponential backoff: 2s → 4s → 8s → 16s → 30s
                    var backoffIndex = Math.Min(_consecutiveErrors - 1, _backoffIntervals.Length - 1);
                    var backoffSeconds = _backoffIntervals[backoffIndex];
                    var newInterval = TimeSpan.FromSeconds(backoffSeconds);

                    // Uppdatera poll interval
                    if (_pollTimer.IsEnabled)
                    {
                        _pollTimer.Interval = newInterval;
                    }

                    LastError = $"Refresh failed (retry #{_consecutiveErrors} in {backoffSeconds}s): {ex.Message}";
                    IsStale = true;

                    System.Diagnostics.Debug.WriteLine($"[BlotterVM] Error #{_consecutiveErrors}, backoff to {backoffSeconds}s: {ex}");

                    // VIKTIGT: Vi clearar INTE OptionTrades/LinearTrades här!
                    // Gamla data stannar kvar tills nästa lyckade refresh.

                    // Notera: LastRefreshUtc/Duration och counts ändras INTE vid fel
                    // så statusraden kan visa "last OK refresh" timestamp
                }
                finally
                {
                    IsBusy = false;
                }
            }
            finally
            {
                Interlocked.Exchange(ref _refreshInFlight, 0);
            }
        }



        private void RestoreSelection(string selectedOptionId, string selectedLinearId)
        {
            if (!string.IsNullOrWhiteSpace(selectedOptionId))
            {
                var vm = OptionTrades.FirstOrDefault(x => string.Equals(x.TradeId, selectedOptionId, StringComparison.Ordinal));
                if (vm != null)
                {
                    SelectedOptionTrade = vm;
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(selectedLinearId))
            {
                var vm = LinearTrades.FirstOrDefault(x => string.Equals(x.TradeId, selectedLinearId, StringComparison.Ordinal));
                if (vm != null)
                {
                    SelectedLinearTrade = vm;
                }
            }
        }

        private static async Task ClearFlagLaterAsync(TradeRowViewModel trade, bool clearNew, int delayMs)
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(true);
                if (clearNew)
                {
                    trade.IsNew = false;
                }
                else
                {
                    trade.IsUpdated = false;
                }
            }
            catch
            {
                // no-op
            }
        }

        private static string BuildSignature(dynamic r, DateTime time, string system, string fallbackStatus)
        {
            return string.Join("|",
                (string)(r.TradeId ?? ""),
                (string)(r.ProductType ?? ""),
                (string)(r.CcyPair ?? ""),
                (string)(r.BuySell ?? ""),
                (string)(r.CallPut ?? ""),
                (r.Strike?.ToString() ?? ""),
                (r.ExpiryDate?.ToString("yyyy-MM-dd") ?? ""),
                r.Notional.ToString(),
                (string)(r.NotionalCcy ?? ""),
                (r.Premium?.ToString() ?? ""),
                (string)(r.PremiumCcy ?? ""),
                (string)(r.Mx3Status ?? ""),
                (string)(r.CalypsoStatus ?? ""),
                (string)(r.SystemStatus ?? ""),
                fallbackStatus ?? "",
                time.ToString("o"),
                system ?? ""
            );
        }

        private static bool IsOptionProduct(string productType)
        {
            if (string.IsNullOrWhiteSpace(productType))
                return false;

            var upper = productType.ToUpperInvariant();
            return upper.Contains("OPTION") || upper.Contains("NDO");
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // === CONTEXT MENU COMMAND HANDLERS ===

        private bool CanExecuteBookTrade()
        {
            var trade = GetSelectedTrade();
            return trade != null && trade.Status == "New";
        }

        private void ExecuteBookTrade()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            System.Diagnostics.Debug.WriteLine($"Booking trade: {trade.TradeId}");
        }

        private bool CanExecuteDuplicateTrade()
        {
            return GetSelectedTrade() != null;
        }

        private void ExecuteDuplicateTrade()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            System.Diagnostics.Debug.WriteLine($"Duplicating trade: {trade.TradeId}");
        }

        private bool CanExecuteCopyToManualInput()
        {
            return GetSelectedTrade() != null;
        }

        private void ExecuteCopyToManualInput()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            // TODO: Implementera copy to manual input
            System.Diagnostics.Debug.WriteLine($"Copy to manual input: {trade.TradeId}");
        }

        private bool CanExecuteBookAsLiveTrade()
        {
            return GetSelectedTrade() != null;
        }

        private void ExecuteBookAsLiveTrade()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            // TODO
            System.Diagnostics.Debug.WriteLine($"Book as live trade: {trade.TradeId}");
        }

        private bool CanExecuteCancelCalypsoTrade()
        {
            var trade = GetSelectedTrade();
            return trade != null &&
                   string.IsNullOrEmpty(trade.CallPut) &&
                   trade.CalypsoStatus == "Pending";
        }

        private void ExecuteCancelCalypsoTrade()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            // TODO
            System.Diagnostics.Debug.WriteLine($"Cancelling Calypso trade: {trade.TradeId}");
        }

        private bool CanExecuteDeleteRow()
        {
            return GetSelectedTrade() != null;
        }

        private void ExecuteDeleteRow()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            if (OptionTrades.Contains(trade))
            {
                OptionTrades.Remove(trade);
            }
            else if (LinearTrades.Contains(trade))
            {
                LinearTrades.Remove(trade);
            }
        }

        private bool CanExecuteCheckIfBooked()
        {
            return GetSelectedTrade() != null;
        }

        private void ExecuteCheckIfBooked()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            // TODO
            System.Diagnostics.Debug.WriteLine($"Checking if booked: {trade.TradeId}");
        }

        private bool CanExecuteOpenErrorLog()
        {
            var trade = GetSelectedTrade();
            return trade != null && trade.Status == "Rejected";
        }

        private void ExecuteOpenErrorLog()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            // TODO
            System.Diagnostics.Debug.WriteLine($"Opening error log for: {trade.TradeId}");
        }

        private TradeRowViewModel GetSelectedTrade()
        {
            return SelectedOptionTrade ?? SelectedLinearTrade;
        }

        private bool FilterTrade(object obj)
        {
            if (obj is not TradeRowViewModel trade) return false;
            if (_currentStatusFilter == "ALL") return true;

            var status = trade.Status?.ToUpperInvariant() ?? "";

            return _currentStatusFilter switch
            {
                "NEW" => status == "NEW",
                "PENDING" => status == "PENDING" || status == "PARTIAL",
                "BOOKED" => status == "BOOKED",
                "ERRORS" => status.Contains("ERROR") || status == "REJECTED" || status == "FAILED",
                _ => true
            };
        }

        public void SetStatusFilter(string filter)
        {
            _currentStatusFilter = filter?.ToUpperInvariant() ?? "ALL";
            _optionTradesView?.Refresh();
            _linearTradesView?.Refresh();
        }

    }
}
