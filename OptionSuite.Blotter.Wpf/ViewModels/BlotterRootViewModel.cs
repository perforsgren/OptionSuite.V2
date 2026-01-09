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

        // =========================
        // D2.2C – Details properties
        // =========================

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
                }
            }
        }

        public TradeRowViewModel SelectedTrade => _selectedOptionTrade ?? _selectedLinearTrade;

        public BlotterRootViewModel(IBlotterReadServiceAsync readService, IBlotterCommandServiceAsync commandService)
        {
            _readService = readService ?? throw new ArgumentNullException(nameof(readService));
            _commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            RefreshCommand = new RelayCommand(async () => await RefreshAsync().ConfigureAwait(true));

            //BookTradeCommand = new RelayCommand(
            //    execute: OnBookTrade,
            //    canExecute: () => SelectedTrade != null && SelectedTrade.Status == "New"
            //);


            BookTradeCommand = new RelayCommand(
                execute: OnBookTrade,
                canExecute: CanExecuteBookTrade
            );

            BulkBookCommand = new RelayCommand(
                execute: OnBulkBook,
                canExecute: () => {
                    var currentUser = Environment.UserName.ToUpper();
                    return OptionTrades.Any(t =>
                        t.Status == "New" &&
                        t.Trader == currentUser) ||
                           LinearTrades.Any(t =>
                        t.Status == "New" &&
                        t.Trader == currentUser);
                }
            );

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

        private void OnBookTrade()
        {
            if (SelectedTrade == null)
                return;

            // Endast Options för nu
            if (string.IsNullOrEmpty(SelectedTrade.CallPut))
            {
                MessageBox.Show("Linear booking not yet implemented.", "Not Supported",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var systemCode = "MX3";
            _ = ExecuteBookTradeAsync(SelectedTrade.StpTradeId, systemCode);
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

            //var result = MessageBox.Show(
            //    $"Book {tradesToBook.Count} trade(s) to MX3?",
            //    "Bulk Book Confirmation",
            //    MessageBoxButton.YesNo,
            //    MessageBoxImage.Question);

            //if (result != MessageBoxResult.Yes)
            //    return;

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

            //MessageBox.Show(
            //    $"Bulk book completed:\n✓ Success: {successCount}\n✗ Failed: {failCount}",
            //    "Bulk Book Result",
            //    MessageBoxButton.OK,
            //    MessageBoxImage.Information);
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

                    var filter = new BlotterFilter
                    {
                        FromTradeDate = DateTime.UtcNow.Date.AddDays(-90),
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

                        // EFTER (endast första gången):
                        var isNew = !isColdStart && !_seenTradeIds.Contains(tradeId);

                        // Om vi redan visat NEW en gång för denna trade, trigga inte igen
                        if (_isNewFlags.ContainsKey(tradeId))
                        {
                            isNew = false;
                        }

                        // EFTER:
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
                            isNew: isNew,
                            isUpdated: isUpdated
                        );

                        // Spara flags för nästa refresh
                        // Starta timer för clear ENDAST om detta är första gången
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

                    // D2.2D-fix: Sätt flagga för att förhindra detail-clear från selection bindings
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

                    // D2.2C: efter refresh kan SelectedTrade peka på ny VM-instans,
                    // så vi triggar details-load igen (men den är debounced via cancel/spärr).
                    TriggerDetailsLoadForSelection();
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

                    // Vi clearar INTE trades vid fel.
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

        /// <summary>
        /// Triggar async load av details för currently selected trade.
        /// Skippar clear om vi är mitt i refresh (förhindrar race condition).
        /// </summary>
        private void TriggerDetailsLoadForSelection()
        {
            var selected = SelectedTrade;
            if (selected == null)
            {
                // Cleara INTE om vi är mitt i refresh (selection blir tillfälligt null när grid clearas)
                if (!_isRefreshing)
                {
                    ClearDetailsCollections();
                }
                return;
            }

            // fire-and-forget (cancellation + spärr gör det stabilt)
            _ = LoadDetailsForSelectedTradeAsync(selected);
        }


        private void ClearDetailsCollections()
        {
            _selectedTradeSystemLinks.Clear();
            _selectedTradeWorkflowEvents.Clear();
            DetailsLastError = null;
            IsDetailsBusy = false;
        }

        /// <summary>
        /// Laddar TradeSystemLinks och TradeWorkflowEvents för vald trade asynkront,
        /// och bygger workflow-chips för summary-kortet.
        /// Använder cancellation token och request version pattern för att hantera snabba selection-ändringar.
        /// </summary>

        private async Task LoadDetailsForSelectedTradeAsync(TradeRowViewModel selected)
        {
            if (selected == null) return;

            // Cancellera tidigare fetch
            CancelDetailsLoadInFlight();

            _detailsCts = new CancellationTokenSource();
            var token = _detailsCts.Token;

            // Increment version BEFORE fetch - detta är vår ENDA concurrency protection (och det räcker!)
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

                // Hämta båda parallellt för bättre performance
                var linksTask = _readService.GetTradeSystemLinksAsync(stpTradeId);
                var eventsTask = _readService.GetTradeWorkflowEventsAsync(stpTradeId, maxRows: 50);

                await Task.WhenAll(linksTask, eventsTask).ConfigureAwait(true);

                // Check både cancellation OCH version för robust concurrency handling
                if (token.IsCancellationRequested)
                {
                    return; // Cancelled, ignore
                }
                if (Volatile.Read(ref _detailsRequestVersion) != thisVersion)
                {
                    return; // Stale result, ignore
                }

                // Await istället för .Result (idiomatisk async pattern)
                var links = await linksTask.ConfigureAwait(true) ?? new List<TradeSystemLinkRow>();
                var eventsList = await eventsTask.ConfigureAwait(true) ?? new List<TradeWorkflowEventRow>();

                //Debug.WriteLine($"[Details] COMMIT for StpTradeId={stpTradeId}, Version={thisVersion}, Links={links.Count}, Events={eventsList.Count}");

                // Commit (clear + repopulate) - version check garanterar att endast latest results committas
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
                // no-op: ny selection vann, expected behavior
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
            _detailsCts = null;  // ← Atomic write FÖRST

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

            // Vänta lite innan dispose (ger async tasks tid att checka token)
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

                // Kolla om trade fortfarande är valid
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
                // Logga istället för att swallowa
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

        /// <summary>
        /// Fire-and-forget wrapper som loggar exceptions istället för att swallowa dem.
        /// </summary>
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


        /// <summary>
        /// D4.2c: Bokar vald trade till MX3.
        /// Skriver till DB, refreshar sedan trade från DB för att uppdatera UI.
        /// </summary>
        public async Task ExecuteBookTradeAsync(long stpTradeId, string systemCode)
        {
            // UI-SKYDD: Blocka dubbelklick
            if (_inFlightBookings.Contains(stpTradeId))
                return;

            _inFlightBookings.Add(stpTradeId);

            try
            {
                // 1. Anropa command service (skriver till DB)
                BookTradeResult result = null;

                if (systemCode == "MX3")
                {
                    result = await _commandService.BookOptionToMx3Async(stpTradeId).ConfigureAwait(true);
                }
                else
                {
                    MessageBox.Show($"System {systemCode} not yet supported.", "Book Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 2. Check result
                if (!result.Success)
                {
                    MessageBox.Show($"Book failed: {result.ErrorMessage}", "Book Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 3. Targeted refresh: Hämta updated trade från DB och uppdatera UI
                await RefreshSingleTradeAsync(stpTradeId).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Book failed: {ex.Message}", "Book Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // UI-SKYDD: Frigör
                _inFlightBookings.Remove(stpTradeId);
            }
        }

        /// <summary>
        /// D4.2c: Refreshar en enskild trade från DB och uppdaterar UI.
        /// Recreate TradeRowViewModel från DB-data och ersätt i collection.
        /// </summary>
        private async Task RefreshSingleTradeAsync(long stpTradeId)
        {
            try
            {
                // Hämta updated trade från DB
                var updatedTrade = await _readService.GetTradeByIdAsync(stpTradeId).ConfigureAwait(true);

                if (updatedTrade == null)
                    return;

                // Kolla om detta är selected trade (spara innan vi ersätter)
                bool wasSelected = SelectedTrade?.StpTradeId == stpTradeId;

                // Hitta trade i rätt collection
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

                // Recreate ViewModel från DB-data
                var newVm = MapToTradeRowViewModel(updatedTrade);

                // Ersätt i collection
                if (optionIndex >= 0)
                {
                    OptionTrades[optionIndex] = newVm;

                    // Återställ selection om den var selected
                    if (wasSelected)
                    {
                        SelectedOptionTrade = newVm;
                    }
                }
                else if (linearIndex >= 0)
                {
                    LinearTrades[linearIndex] = newVm;

                    // Återställ selection om den var selected
                    if (wasSelected)
                    {
                        SelectedLinearTrade = newVm;
                    }
                }

                // Om traden var selected, ladda details direkt (för Routing/WorkflowEvents update)
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


        /// <summary>
        /// D4.2c: Mappar BlotterTradeRow (från DB) till TradeRowViewModel.
        /// Återanvänd samma logik som i RefreshAsync.
        /// </summary>
        private TradeRowViewModel MapToTradeRowViewModel(FxTradeHub.Contracts.Dtos.BlotterTradeRow trade)
        {
            // Använd samma mapping som i RefreshAsync
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
                isNew: false,
                isUpdated: false
            );
        }



        // === CONTEXT MENU COMMAND HANDLERS ===

        private bool CanExecuteBookTrade()
        {
            if (SelectedTrade == null)
                return false;

            // Blocka om Status inte är New eller Error
            if (SelectedTrade.Status != "New" && SelectedTrade.Status != "Error")
                return false;

            // Blocka om booking pågår
            if (_inFlightBookings.Contains(SelectedTrade.StpTradeId))
                return false;

            return true;
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

        private bool CanExecuteCopyToManualInput()
        {
            return GetSelectedTrade() != null;
        }

        private void ExecuteCopyToManualInput()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            // TODO: Implementera copy to manual input
            Debug.WriteLine($"Copy to manual input: {trade.TradeId}");
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

            // TODO
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

            // TODO
            Debug.WriteLine($"Opening error log for: {trade.TradeId}");
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

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Stop polling
            try
            {
                if (_pollTimer != null && _pollTimer.IsEnabled)
                {
                    _pollTimer.Stop();
                }
            }
            catch { /* no-op */ }

            // Stop debounce timer
            try
            {
                if (_userInteractionDebounceTimer != null && _userInteractionDebounceTimer.IsEnabled)
                {
                    _userInteractionDebounceTimer.Stop();
                }
            }
            catch { /* no-op */ }

            // Clear collections (frigör ViewModels och deras memory)
            OptionTrades.Clear();
            LinearTrades.Clear();

            // Clear dictionaries
            _seenTradeIds.Clear();
            _lastSignatureByTradeId.Clear();
            _isNewFlags.Clear();
            _isUpdatedFlags.Clear();
        }


    }
}
