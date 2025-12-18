using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Windows.Input;
using OptionSuite.Blotter.Wpf.Infrastructure;

namespace OptionSuite.Blotter.Wpf.ViewModels
{
    /// <summary>
    /// Root-ViewModel för Blotter-modulen.
    /// Håller modulens grundmetadata (Title/Subtitle) och är ingångspunkten för UI:t i Fas 2.
    /// Denna VM är window-agnostic och antar inte att den kör i Shell.
    /// </summary>
    public sealed class BlotterRootViewModel : INotifyPropertyChanged
    {
        private string _title;
        private string _subtitle;
        private readonly Random _random = new Random();

        // Command för Refresh-knappen
        public ICommand RefreshCommand { get; }

        /// <summary>
        /// Rader i blottern (dummy i M3/M4; ersätts av read-path i M5).
        /// </summary>
        public ObservableCollection<TradeRowViewModel> Trades { get; }

        /// <summary>
        /// Skapar root-VM för Blotter.
        /// Innehåller initial dummydata för att kunna bygga grid-UI innan read-path kopplas på.
        /// </summary>
        public BlotterRootViewModel()
        {
            Title = "Trade Blotter";
            Subtitle = "Blotter (Fas 2) – UI skeleton och read-path kommer stegvis.";

            Trades = new ObservableCollection<TradeRowViewModel>
            {
                new TradeRowViewModel(
                    DateTime.Now.AddMinutes(-2),
                    "VOLBROKER",
                    "704955891.0.05",
                    "OPTION",
                    "USD",
                    "JPY",
                    "Buy",
                    25000000m,
                    155.2300m,
                    "Pending"),

                new TradeRowViewModel(
                    DateTime.Now.AddMinutes(-5),
                    "RTNS",
                    "RTNS-908132",
                    "SPOT",
                    "EUR",
                    "SEK",
                    "Sell",
                    10000000m,
                    11.4205m,
                    "Booked"),

                new TradeRowViewModel(
                    DateTime.Now.AddMinutes(-11),
                    "ICAP",
                    "ICAP-771020",
                    "NDF",
                    "USD",
                    "BRL",
                    "Buy",
                    5000000m,
                    5.1120m,
                    "Rejected")
            };

            // Skapa command för Refresh
            RefreshCommand = new RelayCommand(ExecuteRefresh);
        }

        private void ExecuteRefresh()
        {
            // Simulera ny trade
            var systems = new[] { "VOLBROKER", "RTNS", "ICAP", "BLOOMBERG", "REFINITIV" };
            var products = new[] { "OPTION", "SPOT", "NDF", "FORWARD" };
            var ccyPairs = new[] { ("USD", "JPY"), ("EUR", "SEK"), ("GBP", "USD"), ("USD", "BRL"), ("EUR", "USD") };
            var sides = new[] { "Buy", "Sell" };

            var system = systems[_random.Next(systems.Length)];
            var product = products[_random.Next(products.Length)];
            var ccyPair = ccyPairs[_random.Next(ccyPairs.Length)];
            var side = sides[_random.Next(sides.Length)];

            var newTrade = new TradeRowViewModel(
                DateTime.Now,
                system,
                $"{system}-{_random.Next(100000, 999999)}",
                product,
                ccyPair.Item1,
                ccyPair.Item2,
                side,
                _random.Next(1000000, 50000000),
                100m + (decimal)(_random.NextDouble() * 100),
                "New",        // <-- Status = "New" för nya trades
                isNew: true); // Behåll för badge-visning

            // Lägg till först i listan
            Trades.Insert(0, newTrade);

            // Ändra status från "New" till "Pending" efter 20 sekunder
            System.Threading.Tasks.Task.Delay(20000).ContinueWith(_ =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    newTrade.IsNew = false;  // Dölj badge
                                             // Om du vill ändra status också:
                                             // Men TradeRowViewModel är immutable, så du kan inte ändra Status direkt
                });
            });
        }



        /// <summary>
        /// Titel som visas i headern för modulen.
        /// </summary>
        public string Title
        {
            get { return _title; }
            set
            {
                if (Equals(_title, value))
                {
                    return;
                }

                _title = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Undertitel (en rad) som kompletterar modulens header.
        /// </summary>
        public string Subtitle
        {
            get { return _subtitle; }
            set
            {
                if (Equals(_subtitle, value))
                {
                    return;
                }

                _subtitle = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Triggas när en property ändras.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Notifierar UI att en property har ändrats.
        /// </summary>
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler == null)
            {
                return;
            }

            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
