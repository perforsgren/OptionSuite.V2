using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OptionSuite.Blotter.Wpf.ViewModels
{
    /// <summary>
    /// Minimal radmodell för blottern (M3/M4).
    /// Håller endast fält som behövs för gridens första iteration.
    /// </summary>
    public sealed class TradeRowViewModel : INotifyPropertyChanged
    {
        private bool _isNew;

        /// <summary>Trade time (UTC/local enligt källa).</summary>
        public DateTime Time { get; }

        /// <summary>Källsystem, t.ex. VOLBROKER, RTNS.</summary>
        public string System { get; }

        /// <summary>Extern trade-id (571/818/etc).</summary>
        public string ExternalTradeId { get; }

        /// <summary>Produktkod (OPTION/SPOT/NDF etc).</summary>
        public string Product { get; }

        /// <summary>Första valuta.</summary>
        public string Ccy1 { get; }

        /// <summary>Andra valuta.</summary>
        public string Ccy2 { get; }

        /// <summary>Buy/Sell.</summary>
        public string Side { get; }

        /// <summary>Notional formatteras i UI.</summary>
        public decimal Notional { get; }

        /// <summary>Pris/spot/strike beroende på produkt (v1).</summary>
        public decimal Price { get; }

        /// <summary>Status: Pending/Booked/Rejected.</summary>
        public string Status { get; }

        /// <summary>
        /// Indikerar om traden är ny och ska flashas.
        /// </summary>
        public bool IsNew
        {
            get { return _isNew; }
            set
            {
                if (_isNew != value)
                {
                    _isNew = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Skapar en minimal rad.
        /// </summary>
        public TradeRowViewModel(
            DateTime time,
            string system,
            string externalTradeId,
            string product,
            string ccy1,
            string ccy2,
            string side,
            decimal notional,
            decimal price,
            string status,
            bool isNew = false)
        {
            Time = time;
            System = system ?? string.Empty;
            ExternalTradeId = externalTradeId ?? string.Empty;
            Product = product ?? string.Empty;
            Ccy1 = ccy1 ?? string.Empty;
            Ccy2 = ccy2 ?? string.Empty;
            Side = side ?? string.Empty;
            Notional = notional;
            Price = price;
            Status = status ?? string.Empty;
            IsNew = isNew;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
