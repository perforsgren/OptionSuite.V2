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
using FxTradeHub.Services.Blotter;
using System.Windows;

namespace OptionSuite.Blotter.Wpf.ViewModels
{
    public sealed class BlotterRootViewModel : INotifyPropertyChanged
    {
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

        private readonly ObservableCollection<TradeSystemLinkRow> _selectedTradeSystemLinks =
            new ObservableCollection<TradeSystemLinkRow>();

        private readonly ObservableCollection<TradeWorkflowEventRow> _selectedTradeWorkflowEvents =
            new ObservableCollection<TradeWorkflowEventRow>();

        private CancellationTokenSource _detailsCts;

        private int _detailsRequestVersion = 0;  // request version för concurrency

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

                    // D2.2C: load details när selection ändras
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
                        FromTradeDate = DateTime.UtcNow.Date.AddDays(-30),
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

                        var isUpdated =
                            !isNew &&
                            _lastSignatureByTradeId.TryGetValue(tradeId, out var prevSig) &&
                            !string.Equals(prevSig, signature, StringComparison.Ordinal);

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

                        if (trade.IsNew)
                        {
                            _ = ClearFlagLaterAsync(trade, clearNew: true, delayMs: 20000);
                        }

                        if (trade.IsUpdated)
                        {
                            _ = ClearFlagLaterAsync(trade, clearNew: false, delayMs: 2000);
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
        /// Laddar TradeSystemLinks och TradeWorkflowEvents för vald trade asynkront.
        /// Använder cancellation token och request version pattern för att hantera snabba selection-ändringar.
        /// </summary>
        /// <param name="selected">Vald trade (kan vara null vid deselection)</param>
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




        private void CancelDetailsLoadInFlight()
        {
            try
            {
                _detailsCts?.Cancel();
            }
            catch
            {
                // no-op
            }
            finally
            {
                _detailsCts?.Dispose();
                _detailsCts = null;
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

        /// <summary>
        /// D4.2c: Bokar vald trade till MX3 med optimistic update.
        /// 
        /// Workflow:
        /// 1. Optimistic update: Sätt Status = PENDING i UI
        /// 2. Anropa command service (XML + DB update)
        /// 3. Targeted refresh: Hämta updated trade från DB
        /// 4. Om error: Rollback till original status + visa error
        /// </summary>
        public async Task ExecuteBookTradeAsync(long stpTradeId, string systemCode)
        {
            // 1. Hitta trade i UI (sök i både Options och Linear)
            var tradeVm = OptionTrades.FirstOrDefault(t => t.StpTradeId == stpTradeId)
                       ?? LinearTrades.FirstOrDefault(t => t.StpTradeId == stpTradeId);

            if (tradeVm == null)
            {
                MessageBox.Show($"Trade {stpTradeId} not found in UI.", "Book Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Spara original status för rollback
            var originalStatus = tradeVm.Status;

            try
            {
                // 2. Optimistic update: Sätt PENDING direkt i UI
                tradeVm.Status = "PENDING";

                // 3. Anropa command service
                BookTradeResult result = null;
                if (systemCode == "MX3")
                {
                    result = await _commandService.BookOptionToMx3Async(stpTradeId).ConfigureAwait(true);
                }
                else
                {
                    MessageBox.Show($"System {systemCode} not yet supported.", "Book Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    tradeVm.Status = originalStatus; // Rollback
                    return;
                }

                // 4. Check result
                if (!result.Success)
                {
                    // Rollback + visa error
                    tradeVm.Status = originalStatus;
                    MessageBox.Show($"Book failed: {result.ErrorMessage}", "Book Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 5. Targeted refresh: Hämta updated trade från DB
                await RefreshSingleTradeAsync(stpTradeId).ConfigureAwait(true);

                MessageBox.Show($"Trade {stpTradeId} booked successfully!\nXML: {result.XmlFileName}", "Book Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // Rollback på exception
                tradeVm.Status = originalStatus;
                MessageBox.Show($"Book failed: {ex.Message}", "Book Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// D4.2c: Refreshar en enskild trade från DB och uppdaterar UI.
        /// Används efter Book för att få korrekt status utan full refresh.
        /// </summary>
        private async Task RefreshSingleTradeAsync(long stpTradeId)
        {
            try
            {
                // Hämta updated trade från DB
                var updatedTrade = await _readService.GetTradeByIdAsync(stpTradeId).ConfigureAwait(true);

                if (updatedTrade == null)
                    return;

                // Hitta trade i UI (sök i både Options och Linear)
                var tradeVm = OptionTrades.FirstOrDefault(t => t.StpTradeId == stpTradeId)
                           ?? LinearTrades.FirstOrDefault(t => t.StpTradeId == stpTradeId);

                if (tradeVm != null)
                {
                    // Uppdatera properties (minst Status, men helst alla)
                    tradeVm.Status = updatedTrade.Status;
                    tradeVm.SystemTradeId = updatedTrade.SystemTradeId;
                    tradeVm.LastStatusUtc = updatedTrade.LastChangeUtc;

                    // Om detta är selected trade, uppdatera details också
                    if (SelectedTrade?.StpTradeId == stpTradeId)
                    {
                        _ = LoadDetailsForSelectedTradeAsync(tradeVm);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BlotterVM] RefreshSingleTradeAsync failed: {ex.Message}");
            }
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

            Debug.WriteLine($"Booking trade: {trade.TradeId}");
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
    }
}
