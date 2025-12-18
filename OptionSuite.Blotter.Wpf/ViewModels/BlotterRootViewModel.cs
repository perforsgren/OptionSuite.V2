using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using FxTradeHub.Contracts.Dtos;
using FxTradeHub.Services;

namespace OptionSuite.Blotter.Wpf.ViewModels
{
    public sealed class BlotterRootViewModel : INotifyPropertyChanged
    {
        private readonly IBlotterReadServiceAsync _readService;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Title { get; set; } = "Trade Blotter";
        public string Subtitle { get; set; } = "v2";

        public ObservableCollection<TradeRowViewModel> Trades { get; } =
            new ObservableCollection<TradeRowViewModel>();

        public BlotterRootViewModel(IBlotterReadServiceAsync readService)
        {
            _readService = readService ?? throw new ArgumentNullException(nameof(readService));
        }

        public async Task InitialLoadAsync()
        {
            // Minimal filter nu – du kan koppla UI-filter senare
            var filter = new BlotterFilter
            {
                // Exempel: senaste 7 dagarna (du kan ändra)
                FromTradeDate = DateTime.UtcNow.Date.AddDays(-7),
                ToTradeDate = DateTime.UtcNow.Date.AddDays(1),
                MaxRows = 500
            };

            var rows = await _readService.GetBlotterTradesAsync(filter).ConfigureAwait(true);

            Trades.Clear();

            foreach (var r in rows)
            {
                var (ccy1, ccy2) = SplitCcyPair(r.CcyPair);

                // Price: välj en rimlig primär “pris-källa” för UI-kolumnen
                // (HedgeRate först, annars SpotRate, annars Strike, annars 0)
                var price =
                    (r.HedgeRate ?? r.SpotRate ?? r.Strike ?? 0m);

                var time =
                    (r.ExecutionTimeUtc ?? r.TradeDate ?? DateTime.UtcNow);

                var system =
                    !string.IsNullOrWhiteSpace(r.SourceVenueCode) ? r.SourceVenueCode : r.SourceType;

                Trades.Add(new TradeRowViewModel(
                    time: time,
                    system: system,
                    externalTradeId: r.ExternalTradeId,
                    product: r.ProductType,
                    ccy1: ccy1,
                    ccy2: ccy2,
                    side: r.BuySell,
                    notional: r.Notional,
                    price: price,
                    status: !string.IsNullOrWhiteSpace(r.Mx3Status) ? r.Mx3Status : r.SystemStatus
                ));
            }
        }

        private static (string Ccy1, string Ccy2) SplitCcyPair(string ccyPair)
        {
            if (string.IsNullOrWhiteSpace(ccyPair))
                return (string.Empty, string.Empty);

            // USDJPY
            if (ccyPair.Length == 6 && ccyPair.IndexOfAny(new[] { '/', '-', ' ' }) < 0)
                return (ccyPair.Substring(0, 3), ccyPair.Substring(3, 3));

            // USD/JPY, USD-JPY, "USD JPY"
            var cleaned = ccyPair.Replace("-", "/").Replace(" ", "/");
            var parts = cleaned.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
                return (parts[0], parts[1]);

            return (ccyPair, string.Empty);
        }
    }
}





// Skapa command för Refresh
//RefreshCommand = new RelayCommand(ExecuteRefresh);


//public BlotterRootViewModel()
//{
//    Title = "Trade Blotter";
//    Subtitle = "Blotter (Fas 2) – UI skeleton och read-path kommer stegvis.";

//    Trades = new ObservableCollection<TradeRowViewModel>
//            {
//                new TradeRowViewModel(
//                    DateTime.Now.AddMinutes(-2),
//                    "VOLBROKER",
//                    "704955891.0.05",
//                    "OPTION",
//                    "USD",
//                    "JPY",
//                    "Buy",
//                    25000000m,
//                    155.2300m,
//                    "Pending"),

//                new TradeRowViewModel(
//                    DateTime.Now.AddMinutes(-5),
//                    "RTNS",
//                    "RTNS-908132",
//                    "SPOT",
//                    "EUR",
//                    "SEK",
//                    "Sell",
//                    10000000m,
//                    11.4205m,
//                    "Booked"),

//                new TradeRowViewModel(
//                    DateTime.Now.AddMinutes(-11),
//                    "ICAP",
//                    "ICAP-771020",
//                    "NDF",
//                    "USD",
//                    "BRL",
//                    "Buy",
//                    5000000m,
//                    5.1120m,
//                    "Rejected")
//            };

//    // Skapa command för Refresh
//    RefreshCommand = new RelayCommand(ExecuteRefresh);
//}

//private void ExecuteRefresh()
//{
//    // Simulera ny trade
//    var systems = new[] { "VOLBROKER", "RTNS", "ICAP", "BLOOMBERG", "REFINITIV" };
//    var products = new[] { "OPTION", "SPOT", "NDF", "FORWARD" };
//    var ccyPairs = new[] { ("USD", "JPY"), ("EUR", "SEK"), ("GBP", "USD"), ("USD", "BRL"), ("EUR", "USD") };
//    var sides = new[] { "Buy", "Sell" };

//    var system = systems[_random.Next(systems.Length)];
//    var product = products[_random.Next(products.Length)];
//    var ccyPair = ccyPairs[_random.Next(ccyPairs.Length)];
//    var side = sides[_random.Next(sides.Length)];

//    var newTrade = new TradeRowViewModel(
//        DateTime.Now,
//        system,
//        $"{system}-{_random.Next(100000, 999999)}",
//        product,
//        ccyPair.Item1,
//        ccyPair.Item2,
//        side,
//        _random.Next(1000000, 50000000),
//        100m + (decimal)(_random.NextDouble() * 100),
//        "New",        // <-- Status = "New" för nya trades
//        isNew: true); // Behåll för badge-visning

//    // Lägg till först i listan
//    Trades.Insert(0, newTrade);

//    // Ändra status från "New" till "Pending" efter 20 sekunder
//    System.Threading.Tasks.Task.Delay(20000).ContinueWith(_ =>
//    {
//        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
//        {
//            newTrade.IsNew = false;  // Dölj badge
//                                     // Om du vill ändra status också:
//                                     // Men TradeRowViewModel är immutable, så du kan inte ändra Status direkt
//        });
//    });
//}