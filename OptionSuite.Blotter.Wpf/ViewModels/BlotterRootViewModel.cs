using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private readonly Random _random = new Random();
        private TradeRowViewModel _selectedOptionTrade;
        private TradeRowViewModel _selectedLinearTrade;

        //test

        public event PropertyChangedEventHandler PropertyChanged;

        public string Title { get; set; } = "Trade Blotter";
        public string Subtitle { get; set; } = "v2";

        public ObservableCollection<TradeRowViewModel> OptionTrades { get; } =
            new ObservableCollection<TradeRowViewModel>();

        public ObservableCollection<TradeRowViewModel> LinearTrades { get; } =
            new ObservableCollection<TradeRowViewModel>();

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



        // Separata selections - koordineras automatiskt
        public TradeRowViewModel SelectedOptionTrade
        {
            get => _selectedOptionTrade;
            set
            {
                if (_selectedOptionTrade != value)
                {
                    _selectedOptionTrade = value;
                    OnPropertyChanged(nameof(SelectedOptionTrade));

                    // När options-trade selectas, cleara linear
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

                    // När linear-trade selectas, cleara options
                    if (value != null && _selectedOptionTrade != null)
                    {
                        SelectedOptionTrade = null;
                    }
                }
            }
        }


        public BlotterRootViewModel(IBlotterReadServiceAsync readService)
        {
            _readService = readService ?? throw new ArgumentNullException(nameof(readService));
            RefreshCommand = new RelayCommand(ExecuteRefresh);

            // Context Menu Commands - lägg märke till lambdas!
            BookTradeCommand = new RelayCommand(() => ExecuteBookTrade(), () => CanExecuteBookTrade());
            DuplicateTradeCommand = new RelayCommand(() => ExecuteDuplicateTrade(), () => CanExecuteDuplicateTrade());
            CopyToManualInputCommand = new RelayCommand(() => ExecuteCopyToManualInput(), () => CanExecuteCopyToManualInput());
            BookAsLiveTradeCommand = new RelayCommand(() => ExecuteBookAsLiveTrade(), () => CanExecuteBookAsLiveTrade());
            CancelCalypsoTradeCommand = new RelayCommand(() => ExecuteCancelCalypsoTrade(), () => CanExecuteCancelCalypsoTrade());
            DeleteRowCommand = new RelayCommand(() => ExecuteDeleteRow(), () => CanExecuteDeleteRow());
            CheckIfBookedCommand = new RelayCommand(() => ExecuteCheckIfBooked(), () => CanExecuteCheckIfBooked());
            OpenErrorLogCommand = new RelayCommand(() => ExecuteOpenErrorLog(), () => CanExecuteOpenErrorLog());
        }

        public async Task InitialLoadAsync()
        {
            var filter = new BlotterFilter
            {
                FromTradeDate = DateTime.UtcNow.Date.AddDays(-30),
                ToTradeDate = DateTime.UtcNow.Date.AddDays(1),
                MaxRows = 500
            };

            var rows = await _readService.GetBlotterTradesAsync(filter).ConfigureAwait(true);

            OptionTrades.Clear();
            LinearTrades.Clear();

            foreach (var r in rows)
            {
                var time = (r.ExecutionTimeUtc ?? r.TradeDate ?? DateTime.UtcNow);
                var system = !string.IsNullOrWhiteSpace(r.SourceVenueCode)
                    ? r.SourceVenueCode
                    : r.SourceType;
                var status = !string.IsNullOrWhiteSpace(r.Mx3Status)
                    ? r.Mx3Status
                    : r.SystemStatus;

                var trade = new TradeRowViewModel(
                    tradeId: r.TradeId,
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
                    status: status,
                    time: time,
                    system: system,
                    product: r.ProductType,
                    spotRate: r.SpotRate,
                    swapPoints: r.SwapPoints,
                    settlementDate: r.SettlementDate,
                    hedgeRate: r.HedgeRate,
                    hedgeType: r.HedgeType,
                    calypsoPortfolio: r.CalypsoPortfolio,
                    mx3Status: r.Mx3Status ?? "New",           // NY!
                    calypsoStatus: r.CalypsoStatus ?? "New",   // NY!
                    isNew: false
                );

                if (IsOptionProduct(r.ProductType))
                {
                    OptionTrades.Add(trade);
                }
                else
                {
                    LinearTrades.Add(trade);
                }
            }
        }

        private void ExecuteRefresh()
        {
            var systems = new[] { "VOLBROKER", "RTNS", "ICAP", "BLOOMBERG", "REFINITIV" };
            var products = new[] { "OPTION", "SPOT", "NDF", "FORWARD" };
            var ccyPairs = new[] { "EURUSD", "USDJPY", "GBPUSD", "USDSEK", "EURSEK" };
            var sides = new[] { "Buy", "Sell" };
            var callPuts = new[] { "Call", "Put" };
            var counterparties = new[] { "CPTY_A", "CPTY_B", "CPTY_C" };
            var portfolios = new[] { "FX_OPT_1", "FX_OPT_2", "FX_LIN_1" };
            var traders = new[] { "JDoe", "ASmith", "MJones" };

            var system = systems[_random.Next(systems.Length)];
            var product = products[_random.Next(products.Length)];
            var ccyPair = ccyPairs[_random.Next(ccyPairs.Length)];
            var side = sides[_random.Next(sides.Length)];
            var isOption = product == "OPTION";

            var newTrade = new TradeRowViewModel(
                tradeId: $"{system}-{_random.Next(100000, 999999)}",
                counterparty: counterparties[_random.Next(counterparties.Length)],
                ccyPair: ccyPair,
                buySell: side,
                callPut: isOption ? callPuts[_random.Next(callPuts.Length)] : string.Empty,
                strike: isOption ? 100m + (decimal)(_random.NextDouble() * 50) : null,
                expiryDate: isOption ? DateTime.Now.AddMonths(3) : null,
                notional: _random.Next(1000000, 50000000),
                notionalCcy: ccyPair.Substring(0, 3),
                premium: isOption ? _random.Next(10000, 500000) : null,
                premiumCcy: isOption ? ccyPair.Substring(3, 3) : string.Empty,
                portfolioMx3: portfolios[_random.Next(portfolios.Length)],
                trader: traders[_random.Next(traders.Length)],
                status: "New",
                time: DateTime.Now,
                system: system,
                product: product,
                spotRate: !isOption ? 100m + (decimal)(_random.NextDouble() * 10) : null,
                swapPoints: !isOption && product == "FORWARD" ? (decimal)(_random.NextDouble() * 0.5) : null,
                settlementDate: !isOption ? DateTime.Now.AddDays(2) : null,
                hedgeRate: !isOption ? 100m + (decimal)(_random.NextDouble() * 5) : null,
                hedgeType: !isOption ? "SPOT" : null,
                calypsoPortfolio: "CAL_BOOK_1",
                mx3Status: !isOption ? "Booked" : null,       // NY! Linear börjar som Pending
                calypsoStatus: !isOption ? "Pending" : null,   // NY! Linear börjar som Pending
                isNew: true
            );

            if (IsOptionProduct(product))
            {
                OptionTrades.Insert(0, newTrade);
            }
            else
            {
                LinearTrades.Insert(0, newTrade);
            }

            Task.Delay(20000).ContinueWith(_ =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    newTrade.IsNew = false;
                });
            });
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

            // TODO: Implementera book-logik
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

            // TODO: Implementera book as live
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

            // TODO: Implementera duplicate
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

        private bool CanExecuteCancelCalypsoTrade()
        {
            var trade = GetSelectedTrade();
            // Bara för Linear trades (ingen CallPut) och bara om Calypso = Pending
            return trade != null &&
                   string.IsNullOrEmpty(trade.CallPut) &&
                   trade.CalypsoStatus == "Pending";
        }

        private void ExecuteCancelCalypsoTrade()
        {
            var trade = GetSelectedTrade();
            if (trade == null) return;

            // TODO: Implementera cancel Calypso trade
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

            // Ta bort från rätt collection
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

            // TODO: Implementera check if booked
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

            // TODO: Öppna error log
            System.Diagnostics.Debug.WriteLine($"Opening error log for: {trade.TradeId}");
        }

        private TradeRowViewModel GetSelectedTrade()
        {
            return SelectedOptionTrade ?? SelectedLinearTrade;
        }


    }
}
