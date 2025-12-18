using System;
using System.ComponentModel;

namespace OptionSuite.Blotter.Wpf.ViewModels
{
    public sealed class TradeRowViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public DateTime Time { get; }
        public string System { get; }
        public string ExternalTradeId { get; }
        public string Product { get; }
        public string Ccy1 { get; }
        public string Ccy2 { get; }
        public string Side { get; }
        public decimal Notional { get; }
        public decimal Price { get; }
        public string Status { get; }

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
            string status)
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
        }
    }
}
