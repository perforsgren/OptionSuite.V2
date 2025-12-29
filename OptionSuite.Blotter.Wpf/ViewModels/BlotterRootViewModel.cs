using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using FxTradeHub.Contracts.Dtos;
using FxTradeHub.Services;
using OptionSuite.Blotter.Wpf.Infrastructure;

namespace OptionSuite.Blotter.Wpf.ViewModels
{
    public sealed class BlotterRootViewModel : INotifyPropertyChanged
    {
        private readonly IBlotterReadServiceAsync _readService;

        private TradeRowViewModel _selectedOptionTrade;
        private TradeRowViewModel _selectedLinearTrade;

        private bool _isBusy;
        private string _lastError;

        // 2D: state för diff
        private readonly HashSet<string> _seenTradeIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _lastSignatureByTradeId = new Dictionary<string, string>(StringComparer.Ordinal);

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

            BookTradeCommand = new RelayCommand(() => ExecuteBookTrade(), () => CanExecuteBookTrade());
            DuplicateTradeCommand = new RelayCommand(() => ExecuteDuplicateTrade(), () => CanExecuteDuplicateTrade());
            CopyToManualInputCommand = new RelayCommand(() => ExecuteCopyToManualInput(), () => CanExecuteCopyToManualInput());
            BookAsLiveTradeCommand = new RelayCommand(() => ExecuteBookAsLiveTrade(), () => CanExecuteBookAsLiveTrade());
            CancelCalypsoTradeCommand = new RelayCommand(() => ExecuteCancelCalypsoTrade(), () => CanExecuteCancelCalypsoTrade());
            DeleteRowCommand = new RelayCommand(() => ExecuteDeleteRow(), () => CanExecuteDeleteRow());
            CheckIfBookedCommand = new RelayCommand(() => ExecuteCheckIfBooked(), () => CanExecuteCheckIfBooked());
            OpenErrorLogCommand = new RelayCommand(() => ExecuteOpenErrorLog(), () => CanExecuteOpenErrorLog());
        }

        public Task InitialLoadAsync()
        {
            return RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (IsBusy)
            {
                return;
            }

            // 2D: kom ihåg selection (id + vilken grid)
            var selectedOptionId = SelectedOptionTrade?.TradeId;
            var selectedLinearId = SelectedLinearTrade?.TradeId;

            try
            {
                IsBusy = true;
                LastError = null;

                var filter = new BlotterFilter
                {
                    FromTradeDate = DateTime.UtcNow.Date.AddDays(-30),
                    ToTradeDate = DateTime.UtcNow.Date.AddDays(1),
                    MaxRows = 500
                };

                var rows = await _readService.GetBlotterTradesAsync(filter).ConfigureAwait(true);

                OptionTrades.Clear();
                LinearTrades.Clear();

                // 2D: bygg nästa “snapshot” av signatures
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

                    // 2D: signature = “vad vi tycker betyder en ändring i raden”
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
                        OptionTrades.Add(trade);
                    }
                    else
                    {
                        LinearTrades.Add(trade);
                    }

                    // 2D: “släck” highlight efter en stund
                    if (trade.IsNew)
                    {
                        _ = ClearFlagLaterAsync(trade, clearNew: true, delayMs: 20000);
                    }

                    if (trade.IsUpdated)
                    {
                        _ = ClearFlagLaterAsync(trade, clearNew: false, delayMs: 2000);
                    }
                }

                // 2D: commit “snapshot”
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

                // 2D: återställ selection (utan att “cleara” den i onödan)
                RestoreSelection(selectedOptionId, selectedLinearId);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RestoreSelection(string selectedOptionId, string selectedLinearId)
        {
            // Om du hade en option selected: försök återselecta den först
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
            // Håll detta “billigt”: string med ett urval fält som betyder något visuellt i grid/routing/status.
            // Justera när du kopplar på fler kolumner.
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

        private bool CanExecuteBookAsLiveTrade()
        {
            var trade = GetSelectedTrade();
            return trade != null && trade.Status == "New";
        }

        private void ExecuteBookAsLiveTrade()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            System.Diagnostics.Debug.WriteLine($"Booking as live: {trade.TradeId}");
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

            System.Diagnostics.Debug.WriteLine($"Copy to manual input: {trade.TradeId}");
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

            System.Diagnostics.Debug.WriteLine($"Opening error log for: {trade.TradeId}");
        }

        private TradeRowViewModel GetSelectedTrade()
        {
            return SelectedOptionTrade ?? SelectedLinearTrade;
        }
    }
}
