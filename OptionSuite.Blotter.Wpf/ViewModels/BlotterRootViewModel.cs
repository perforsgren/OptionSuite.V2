using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using FxTradeHub.Contracts.Dtos;
using FxTradeHub.Services;
using OptionSuite.Blotter.Wpf.Infrastructure;
using System.Windows.Data;
using FxTradeHub.Services.Blotter;
using System.Windows;

namespace OptionSuite.Blotter.Wpf.ViewModels
{
    public sealed class BlotterRootViewModel : INotifyPropertyChanged, IDisposable
    {
        private bool _disposed;

        private readonly IBlotterReadServiceAsync _readService;
        private readonly IBlotterCommandServiceAsync _commandService;

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

        // Bevara IsNew/IsUpdated mellan refreshes
        private readonly Dictionary<string, bool> _isNewFlags = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _isUpdatedFlags = new Dictionary<string, bool>(StringComparer.Ordinal);

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

        private readonly DispatcherTimer _calypsoCountdownTimer;
        private string _calypsoCountdown = "—";

        // Filter properties
        private bool _filterMyTradesOnly;
        private bool _filterTodayOnly = true;  // Default: endast idag
        private bool _autoBookEnabled;
        private string _currentUserTraderId;

        private bool _isRefreshing; // förhindra detail-clear under refresh

        // =========================
        // D2.2C – Details (links/events)
        // =========================
        private bool _isDetailsBusy;
        private string _detailsLastError;

        private readonly ObservableCollection<TradeSystemLinkRow> _selectedTradeSystemLinks = new ObservableCollection<TradeSystemLinkRow>();
        private readonly ObservableCollection<TradeWorkflowEventRow> _selectedTradeWorkflowEvents = new ObservableCollection<TradeWorkflowEventRow>();

        private CancellationTokenSource _detailsCts;

        private int _detailsRequestVersion = 0;  // request version för concurrency

        // UI-skydd mot dubbelklick/race
        private readonly HashSet<long> _inFlightBookings = new HashSet<long>();

        public event PropertyChangedEventHandler PropertyChanged;

        public string Title { get; set; } = "Trade Blotter";
        public string Subtitle { get; set; } = "v2";

        public ObservableCollection<TradeRowViewModel> OptionTrades { get; } = new ObservableCollection<TradeRowViewModel>();

        public ObservableCollection<TradeRowViewModel> LinearTrades { get; } = new ObservableCollection<TradeRowViewModel>();

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
        /// Countdown till nästa Calypso import (var 3:e minut).
        /// </summary>
        public string CalypsoCountdown
        {
            get => _calypsoCountdown;
            private set
            {
                if (!string.Equals(_calypsoCountdown, value, StringComparison.Ordinal))
                {
                    _calypsoCountdown = value;
                    OnPropertyChanged(nameof(CalypsoCountdown));
                }
            }
        }

        /// <summary>
        /// Filtrerar endast trades för inloggad användare.
        /// </summary>
        public bool FilterMyTradesOnly
        {
            get => _filterMyTradesOnly;
            set
            {
                if (_filterMyTradesOnly != value)
                {
                    _filterMyTradesOnly = value;
                    OnPropertyChanged(nameof(FilterMyTradesOnly));
                    _optionTradesView?.Refresh();
                    _linearTradesView?.Refresh();

                    // ✅ FIX: Uppdatera Book-knappen efter filter-ändring
                    RaiseCanExecuteForBookCommands();
                }
            }
        }

        /// <summary>
        /// Filtrerar endast trades från idag (server-side filter).
        /// </summary>
        public bool FilterTodayOnly
        {
            get => _filterTodayOnly;
            set
            {
                if (_filterTodayOnly != value)
                {
                    _filterTodayOnly = value;
                    OnPropertyChanged(nameof(FilterTodayOnly));
                    // Trigga ny refresh med ändrat datum-filter
                    _ = RefreshAsync();
                }
            }
        }

        /// <summary>
        /// Auto-bokar nya trades automatiskt när de kommer in.
        /// </summary>
        public bool AutoBookEnabled
        {
            get => _autoBookEnabled;
            set
            {
                if (_autoBookEnabled != value)
                {
                    _autoBookEnabled = value;
                    OnPropertyChanged(nameof(AutoBookEnabled));
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

        /// <summary>
        /// True när högerpanelen håller på att ladda TradeSystemLink/WorkflowEvent för vald trade.
        /// </summary>
        public bool IsDetailsBusy
        {
            get => _isDetailsBusy;
            private set
            {
                if (_isDetailsBusy != value)
                {
                    _isDetailsBusy = value;
                    OnPropertyChanged(nameof(IsDetailsBusy));
                }
            }
        }

        /// <summary>
        /// Senaste felet vid laddning av details (högerpanel). Null/empty = OK.
        /// </summary>
        public string DetailsLastError
        {
            get => _detailsLastError;
            private set
            {
                if (!string.Equals(_detailsLastError, value, StringComparison.Ordinal))
                {
                    _detailsLastError = value;
                    OnPropertyChanged(nameof(DetailsLastError));
                }
            }
        }

        public ObservableCollection<TradeSystemLinkRow> SelectedTradeSystemLinks => _selectedTradeSystemLinks;

        public ObservableCollection<TradeWorkflowEventRow> SelectedTradeWorkflowEvents => _selectedTradeWorkflowEvents;

        public ICommand RefreshCommand { get; }

        // Context Menu Commands
        public ICommand BookTradeCommand { get; }
        public ICommand BulkBookCommand { get; }
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

                    // load details när selection ändras
                    TriggerDetailsLoadForSelection();

                    // ✅ FIX: Uppdatera Book-knappen när selection ändras
                    RaiseCanExecuteForBookCommands();
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

                    // D2.2C: load details när selection ändras
                    TriggerDetailsLoadForSelection();

                    // ✅ FIX: Uppdatera Book-knappen när selection ändras
                    RaiseCanExecuteForBookCommands();
                }
            }
        }

        public TradeRowViewModel SelectedTrade => _selectedOptionTrade ?? _selectedLinearTrade;

        public BlotterRootViewModel(IBlotterReadServiceAsync readService, IBlotterCommandServiceAsync commandService)
        {
            _readService = readService ?? throw new ArgumentNullException(nameof(readService));
            _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            // Hämta inloggad användares TraderId (upper-case för konsistent jämförelse)
            _currentUserTraderId = Environment.UserName.ToUpperInvariant();

            RefreshCommand = new RelayCommand(async () => await RefreshAsync().ConfigureAwait(true));

            BookTradeCommand = new RelayCommand(
                execute: OnBookTrade,
                canExecute: CanExecuteBookTrade
            );

            BulkBookCommand = new RelayCommand(
                execute: OnBulkBook,
                canExecute: CanExecuteBulkBook
            );

            DuplicateTradeCommand = new RelayCommand(() => ExecuteDuplicateTrade(), () => CanExecuteDuplicateTrade());
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

            // debounce-timer som släcker IsUserInteracting efter kort "idle"
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

            // Calypso countdown timer (tick varje sekund)
            _calypsoCountdownTimer = new DispatcherTimer(DispatcherPriority.Background);
            _calypsoCountdownTimer.Interval = TimeSpan.FromSeconds(1);
            _calypsoCountdownTimer.Tick += OnCalypsoCountdownTick;
            _calypsoCountdownTimer.Start();
        }

        /// <summary>
        /// ✅ FIX: Helper method för att uppdatera CanExecute på Book-commands.
        /// Kallas efter data-ändringar (refresh/filter/booking).
        /// </summary>
        private void RaiseCanExecuteForBookCommands()
        {
            (BookTradeCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (BulkBookCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnBookTrade()
        {
            if (SelectedTrade == null)
                return;

            // Hämta systemlänkar för vald trade från details-panelen
            var systemLinks = _selectedTradeSystemLinks
                .Where(l => l.BookFlag == true &&
                            (l.Status == "New" || l.Status == "Error"))
                .ToList();

            if (systemLinks.Count == 0)
            {
                MessageBox.Show("No systems configured for booking.", "Book Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Boka till alla relevanta system
            foreach (var link in systemLinks)
            {
                _ = ExecuteBookTradeAsync(SelectedTrade.StpTradeId, link.SystemCode);
            }
        }

        private void OnBulkBook()
        {
            var currentUser = Environment.UserName.ToUpper();

            // Filtrera: endast Options (för nu), Status=New, mitt TraderID
            var tradesToBook = OptionTrades
                .Where(t => t.Status == "New" &&
                            t.Trader == currentUser &&
                            !string.IsNullOrEmpty(t.CallPut))
                .ToList();

            if (tradesToBook.Count == 0)
            {
                MessageBox.Show("No trades to book.", "Bulk Book",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Book alla trades asynkront
            _ = ExecuteBulkBookAsync(tradesToBook);
        }

        private async Task ExecuteBulkBookAsync(List<TradeRowViewModel> trades)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var trade in trades)
            {
                try
                {
                    await ExecuteBookTradeAsync(trade.StpTradeId, "MX3").ConfigureAwait(true);
                    successCount++;
                }
                catch
                {
                    failCount++;
                }
            }
        }

        public async Task InitialLoadAsync()
        {
            await RefreshAsync().ConfigureAwait(true);

            // Auto-select första raden i Options grid om det finns trades
            if (OptionTrades.Count > 0)
            {
                SelectedOptionTrade = OptionTrades[0];
            }
            else if (LinearTrades.Count > 0)
            {
                SelectedLinearTrade = LinearTrades[0];
            }
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

        /// <summary>
        /// Hämtar nya trades från backend och uppdaterar OptionTrades/LinearTrades collections.
        /// D2.2D-fix: Använder _isRefreshing flagga för att förhindra detail-clear under collection rebuild.
        /// Återställer selection efter clear genom att hitta samma TradeId i nya collections.
        /// Triggar piggyback detail-refresh efter lyckad refresh för att hålla högerpanel uppdaterad.
        /// </summary>
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

                    var sw = Stopwatch.StartNew();

                    // Server-side filter: datum-range baserat på FilterTodayOnly
                    var filter = new BlotterFilter
                    {
                        FromTradeDate = _filterTodayOnly
                            ? DateTime.UtcNow.Date
                            : DateTime.UtcNow.Date.AddDays(-90),
                        ToTradeDate = DateTime.UtcNow.Date.AddDays(1),
                        MaxRows = 500
                    };

                    var rows = await _readService.GetBlotterTradesAsync(filter).ConfigureAwait(true);

                    var newOptionTrades = new List<TradeRowViewModel>();
                    var newLinearTrades = new List<TradeRowViewModel>();

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

                        if (_isNewFlags.ContainsKey(tradeId))
                        {
                            isNew = false;
                        }

                        var isUpdated = !isNew &&
                                        _lastSignatureByTradeId.TryGetValue(tradeId, out var prevSig) &&
                                        !string.Equals(prevSig, signature, StringComparison.Ordinal);

                        if (_isUpdatedFlags.ContainsKey(tradeId))
                            isUpdated = false;

                        var trade = new TradeRowViewModel(
                            stpTradeId: r.StpTradeId,
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
                            premiumDate: r.PremiumDate,
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
                            settlementCurrency: r.SettlementCcy,
                            isNonDeliverable: r.IsNonDeliverable,
                            fixingDate: r.FixingDate,
                            mx3Status: r.Mx3Status ?? "New",
                            calypsoStatus: r.CalypsoStatus ?? "New",
                            mic: r.Mic,
                            tvtic: r.Tvtic,
                            isin: r.Isin,
                            invDecisionId: r.InvId,
                            reportingEntityId: r.ReportingEntityId,
                            margin: r.Margin,
                            stpFlag: r.StpFlag ?? false,
                            isNew: isNew,
                            isUpdated: isUpdated
                        );

                        if (isNew && !_isNewFlags.ContainsKey(tradeId))
                        {
                            _isNewFlags[tradeId] = true;
                            FireAndForget(ClearFlagLaterAsync(trade, clearNew: true, delayMs: 5000));
                        }

                        if (isUpdated && !_isUpdatedFlags.ContainsKey(tradeId))
                        {
                            _isUpdatedFlags[tradeId] = true;
                            FireAndForget(ClearFlagLaterAsync(trade, clearNew: false, delayMs: 2000));
                        }

                        if (IsOptionProduct(r.ProductType))
                        {
                            newOptionTrades.Add(trade);
                        }
                        else
                        {
                            newLinearTrades.Add(trade);
                        }

                        var status = trade.Status?.ToUpperInvariant() ?? "";

                        if (status == "NEW")
                        {
                            newCount++;
                        }
                        else if (status == "PENDING" || status == "PARTIAL")
                        {
                            pendingCount++;
                        }
                        else if (status == "BOOKED")
                        {
                            bookedCount++;
                        }
                        else if (status.Contains("ERROR") || status == "REJECTED" || status == "FAILED")
                        {
                            errorCount++;
                        }
                    }

                    _isRefreshing = true;
                    try
                    {
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

                        RestoreSelection(selectedOptionId, selectedLinearId);
                    }
                    finally
                    {
                        _isRefreshing = false;
                    }

                    sw.Stop();

                    TotalTrades = newOptionTrades.Count + newLinearTrades.Count;
                    NewCount = newCount;
                    PendingCount = pendingCount;
                    BookedCount = bookedCount;
                    ErrorCount = errorCount;

                    LastRefreshUtc = DateTime.UtcNow;
                    LastRefreshDuration = sw.Elapsed;
                    LastError = null;
                    IsStale = false;

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

                    TriggerDetailsLoadForSelection();

                    // ✅ FIX: Uppdatera Book-knappen efter refresh
                    RaiseCanExecuteForBookCommands();
                }
                catch (Exception ex)
                {
                    _consecutiveErrors++;

                    var backoffIndex = Math.Min(_consecutiveErrors - 1, _backoffIntervals.Length - 1);
                    var backoffSeconds = _backoffIntervals[backoffIndex];
                    var newInterval = TimeSpan.FromSeconds(backoffSeconds);

                    if (_pollTimer.IsEnabled)
                    {
                        _pollTimer.Interval = newInterval;
                    }

                    LastError = $"Refresh failed (retry #{_consecutiveErrors} in {backoffSeconds}s): {ex.Message}";
                    IsStale = true;

                    Debug.WriteLine($"[BlotterVM] Error #{_consecutiveErrors}, backoff to {backoffSeconds}s: {ex}");
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

        private void TriggerDetailsLoadForSelection()
        {
            var selected = SelectedTrade;
            if (selected == null)
            {
                if (!_isRefreshing)
                {
                    ClearDetailsCollections();
                }
                return;
            }

            _ = LoadDetailsForSelectedTradeAsync(selected);
        }

        private void ClearDetailsCollections()
        {
            _selectedTradeSystemLinks.Clear();
            _selectedTradeWorkflowEvents.Clear();
            DetailsLastError = null;
            IsDetailsBusy = false;
        }

        private async Task LoadDetailsForSelectedTradeAsync(TradeRowViewModel selected)
        {
            if (selected == null) return;

            CancelDetailsLoadInFlight();

            _detailsCts = new CancellationTokenSource();
            var token = _detailsCts.Token;

            var thisVersion = Interlocked.Increment(ref _detailsRequestVersion);

            var stpTradeId = selected.StpTradeId;

            try
            {
                IsDetailsBusy = true;
                DetailsLastError = null;

                if (stpTradeId <= 0)
                {
                    ClearDetailsCollections();
                    return;
                }

                var linksTask = _readService.GetTradeSystemLinksAsync(stpTradeId);
                var eventsTask = _readService.GetTradeWorkflowEventsAsync(stpTradeId, maxRows: 50);

                await Task.WhenAll(linksTask, eventsTask).ConfigureAwait(true);

                if (token.IsCancellationRequested)
                {
                    return;
                }
                if (Volatile.Read(ref _detailsRequestVersion) != thisVersion)
                {
                    return;
                }

                var links = await linksTask.ConfigureAwait(true) ?? new List<TradeSystemLinkRow>();
                var eventsList = await eventsTask.ConfigureAwait(true) ?? new List<TradeWorkflowEventRow>();

                _selectedTradeSystemLinks.Clear();
                foreach (var l in links)
                {
                    l.Status = ToProperCase(l.Status);
                    _selectedTradeSystemLinks.Add(l);
                }

                _selectedTradeWorkflowEvents.Clear();
                foreach (var ev in eventsList)
                {
                    _selectedTradeWorkflowEvents.Add(ev);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[Details] CANCELLED for Version={thisVersion}");
            }
            catch (Exception ex)
            {
                DetailsLastError = $"Details load failed: {ex.Message}";
                Debug.WriteLine($"[BlotterVM] Details load failed: {ex}");
            }
            finally
            {
                IsDetailsBusy = false;
            }
        }

        private static string ToProperCase(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "New";

            var normalized = status.Trim().ToUpper();

            return normalized switch
            {
                "NEW" => "New",
                "PENDING" => "Pending",
                "BOOKED" => "Booked",
                "ERROR" => "Error",
                "REJECTED" => "Rejected",
                "PARTIAL" => "Partial",
                "CANCELLED" => "Cancelled",
                "FAILED" => "Failed",
                _ => char.ToUpper(status.Trim()[0]) + status.Trim().Substring(1).ToLower()
            };
        }

        private void CancelDetailsLoadInFlight()
        {
            var oldCts = _detailsCts;
            _detailsCts = null;

            if (oldCts == null)
                return;

            try
            {
                oldCts.Cancel();
            }
            catch
            {
                // no-op
            }

            Task.Delay(100).ContinueWith(_ =>
            {
                try
                {
                    oldCts.Dispose();
                }
                catch
                {
                    // no-op
                }
            }, TaskScheduler.Default);
        }

        private async Task ClearFlagLaterAsync(TradeRowViewModel trade, bool clearNew, int delayMs)
        {
            try
            {
                await Task.Delay(delayMs).ConfigureAwait(true);

                if (trade == null)
                    return;

                if (clearNew)
                {
                    trade.IsNew = false;
                    _isNewFlags.Remove(trade.TradeId);
                }
                else
                {
                    trade.IsUpdated = false;
                    _isUpdatedFlags.Remove(trade.TradeId);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BlotterVM] ClearFlagLaterAsync failed for trade {trade?.TradeId}: {ex.Message}");
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

        private void FireAndForget(Task task, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            _ = task.ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    Debug.WriteLine($"[BlotterVM] Fire-and-forget task failed in {caller}: {t.Exception.InnerException?.Message ?? t.Exception.Message}");
                }
            }, TaskScheduler.Default);
        }

        public async Task ExecuteBookTradeAsync(long stpTradeId, string systemCode)
        {
            if (_inFlightBookings.Contains(stpTradeId))
                return;

            _inFlightBookings.Add(stpTradeId);

            try
            {
                BookTradeResult result = null;

                // ✅ FIX: Avgör om traden är option eller linear baserat på CallPut
                var isOption = !string.IsNullOrEmpty(SelectedTrade?.CallPut);

                if (systemCode == "MX3")
                {
                    if (isOption)
                    {
                        result = await _commandService.BookOptionToMx3Async(stpTradeId).ConfigureAwait(true);
                    }
                    else
                    {
                        result = await _commandService.BookLinearToMx3Async(stpTradeId).ConfigureAwait(true);
                    }
                }
                else if (systemCode == "CALYPSO")
                {
                    result = await _commandService.BookLinearToCalypsoAsync(stpTradeId).ConfigureAwait(true);
                }
                else
                {
                    MessageBox.Show($"System {systemCode} not yet supported.", "Book Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!result.Success)
                {
                    MessageBox.Show($"Book failed: {result.ErrorMessage}", "Book Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                await RefreshSingleTradeAsync(stpTradeId).ConfigureAwait(true);
                RaiseCanExecuteForBookCommands();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Book failed: {ex.Message}", "Book Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _inFlightBookings.Remove(stpTradeId);
            }
        }

        private async Task RefreshSingleTradeAsync(long stpTradeId)
        {
            try
            {
                var updatedTrade = await _readService.GetTradeByIdAsync(stpTradeId).ConfigureAwait(true);

                if (updatedTrade == null)
                    return;

                bool wasSelected = SelectedTrade?.StpTradeId == stpTradeId;

                var optionIndex = -1;
                var linearIndex = -1;

                for (int i = 0; i < OptionTrades.Count; i++)
                {
                    if (OptionTrades[i].StpTradeId == stpTradeId)
                    {
                        optionIndex = i;
                        break;
                    }
                }

                if (optionIndex == -1)
                {
                    for (int i = 0; i < LinearTrades.Count; i++)
                    {
                        if (LinearTrades[i].StpTradeId == stpTradeId)
                        {
                            linearIndex = i;
                            break;
                        }
                    }
                }

                var newVm = MapToTradeRowViewModel(updatedTrade);

                if (optionIndex >= 0)
                {
                    OptionTrades[optionIndex] = newVm;

                    if (wasSelected)
                    {
                        SelectedOptionTrade = newVm;
                    }
                }
                else if (linearIndex >= 0)
                {
                    LinearTrades[linearIndex] = newVm;

                    if (wasSelected)
                    {
                        SelectedLinearTrade = newVm;
                    }
                }

                if (wasSelected)
                {
                    _ = LoadDetailsForSelectedTradeAsync(newVm);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BlotterVM] RefreshSingleTradeAsync failed: {ex.Message}");
            }
        }

        private TradeRowViewModel MapToTradeRowViewModel(FxTradeHub.Contracts.Dtos.BlotterTradeRow trade)
        {
            return new TradeRowViewModel(
                stpTradeId: trade.StpTradeId,
                tradeId: trade.TradeId,
                counterparty: trade.CounterpartyCode,
                ccyPair: trade.CcyPair,
                buySell: trade.BuySell,
                callPut: trade.CallPut,
                strike: trade.Strike,
                expiryDate: trade.ExpiryDate,
                notional: trade.Notional,
                notionalCcy: trade.NotionalCcy,
                premium: trade.Premium,
                premiumCcy: trade.PremiumCcy,
                premiumDate: trade.PremiumDate,
                portfolioMx3: trade.PortfolioMx3,
                trader: trade.TraderId,
                status: trade.Status,
                time: trade.TradeDate ?? DateTime.MinValue,
                system: trade.SystemCode,
                product: trade.ProductType,
                spotRate: trade.SpotRate,
                swapPoints: trade.SwapPoints,
                settlementDate: trade.SettlementDate,
                hedgeRate: trade.HedgeRate,
                hedgeType: trade.HedgeType,
                calypsoPortfolio: trade.CalypsoPortfolio,
                settlementCurrency: trade.SettlementCcy,
                isNonDeliverable: trade.IsNonDeliverable,
                fixingDate: trade.FixingDate,
                mx3Status: trade.SystemCode == "MX3" ? trade.Status : null,
                calypsoStatus: trade.SystemCode == "CALYPSO" ? trade.Status : null,
                mic: trade.Mic,
                tvtic: trade.Tvtic,
                isin: trade.Isin,
                invDecisionId: trade.InvId,
                reportingEntityId: trade.ReportingEntityId,
                margin: trade.Margin,
                stpFlag: trade.StpFlag ?? false,
                isNew: false,
                isUpdated: false
            );
        }

        // === CONTEXT MENU COMMAND HANDLERS ===

        private bool CanExecuteBookTrade()
        {
            if (SelectedTrade == null)
                return false;

            if (SelectedTrade.Status != "New" && SelectedTrade.Status != "Error")
                return false;

            if (_inFlightBookings.Contains(SelectedTrade.StpTradeId))
                return false;

            return true;
        }

        /// <summary>
        /// ✅ FIX: CanExecuteBulkBook kollar nu BÅDA grids OCH filtrerar på current user.
        /// </summary>
        private bool CanExecuteBulkBook()
        {
            var currentUser = Environment.UserName.ToUpper();

            return OptionTrades.Any(t =>
                t.Status == "New" &&
                t.Trader == currentUser &&
                !string.IsNullOrEmpty(t.CallPut)) ||
                   LinearTrades.Any(t =>
                t.Status == "New" &&
                t.Trader == currentUser);
        }

        private bool CanExecuteDuplicateTrade()
        {
            return GetSelectedTrade() != null;
        }

        private void ExecuteDuplicateTrade()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            Debug.WriteLine($"Duplicating trade: {trade.TradeId}");
        }

        private bool CanExecuteBookAsLiveTrade()
        {
            return GetSelectedTrade() != null;
        }

        private void ExecuteBookAsLiveTrade()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            Debug.WriteLine($"Book as live trade: {trade.TradeId}");
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

            Debug.WriteLine($"Cancelling Calypso trade: {trade.TradeId}");
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

            // ✅ FIX: Uppdatera Book-knappen efter delete
            RaiseCanExecuteForBookCommands();
        }

        private bool CanExecuteCheckIfBooked()
        {
            return GetSelectedTrade() != null;
        }

        private void ExecuteCheckIfBooked()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            Debug.WriteLine($"Checking if booked: {trade.TradeId}");
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

            Debug.WriteLine($"Opening error log for: {trade.TradeId}");
        }

        private TradeRowViewModel GetSelectedTrade()
        {
            return SelectedOptionTrade ?? SelectedLinearTrade;
        }

        private bool FilterTrade(object obj)
        {
            if (obj is not TradeRowViewModel trade) return false;

            if (_filterMyTradesOnly)
            {
                if (!string.Equals(trade.Trader, _currentUserTraderId, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

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

            // ✅ FIX: Uppdatera Book-knappen efter status-filter ändring
            RaiseCanExecuteForBookCommands();
        }

        private void OnCalypsoCountdownTick(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            var currentMinute = now.Minute;

            int minutesToNext = 0;
            do
            {
                minutesToNext++;
            } while ((currentMinute + minutesToNext) % 3 != 0);

            DateTime targetTime;
            int targetMinute = currentMinute + minutesToNext;

            if (targetMinute >= 60)
            {
                targetMinute = 0;
                targetTime = now.Date.AddHours(now.Hour + 1);
            }
            else
            {
                targetTime = now.Date.AddHours(now.Hour).AddMinutes(targetMinute);
            }

            var remaining = targetTime - now;
            CalypsoCountdown = remaining.ToString(@"mm\:ss");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                if (_pollTimer != null && _pollTimer.IsEnabled)
                {
                    _pollTimer.Stop();
                }
            }
            catch { /* no-op */ }

            try
            {
                if (_userInteractionDebounceTimer != null && _userInteractionDebounceTimer.IsEnabled)
                {
                    _userInteractionDebounceTimer.Stop();
                }
            }
            catch { /* no-op */ }

            try
            {
                if (_calypsoCountdownTimer != null && _calypsoCountdownTimer.IsEnabled)
                {
                    _calypsoCountdownTimer.Stop();
                }
            }
            catch { /* no-op */ }

            OptionTrades.Clear();
            LinearTrades.Clear();

            _seenTradeIds.Clear();
            _lastSignatureByTradeId.Clear();
            _isNewFlags.Clear();
            _isUpdatedFlags.Clear();
        }
    }
}