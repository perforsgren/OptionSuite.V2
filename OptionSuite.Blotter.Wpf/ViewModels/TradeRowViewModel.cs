using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OptionSuite.Blotter.Wpf.ViewModels
{
    public sealed class TradeRowViewModel : INotifyPropertyChanged
    {
        private bool _isNew;

        public event PropertyChangedEventHandler PropertyChanged;

        // Options-specifika kolumner
        public string TradeId { get; }
        public string Counterparty { get; }
        public string CcyPair { get; }
        public string BuySell { get; }
        public string CallPut { get; }
        public decimal? Strike { get; }
        public DateTime? ExpiryDate { get; }
        public decimal Notional { get; }
        public string NotionalCcy { get; }
        public decimal? Premium { get; }
        public string PremiumCcy { get; }
        public string PortfolioMx3 { get; }
        public string Trader { get; }
        public string Status { get; }

        // Gemensamma fält
        public DateTime Time { get; }
        public string System { get; }
        public string Product { get; }

        // Linear-specifika
        public decimal? SpotRate { get; }
        public decimal? SwapPoints { get; }
        public DateTime? SettlementDate { get; }
        public decimal? HedgeRate { get; }           // NY!
        public string HedgeType { get; }              // NY!
        public string CalypsoPortfolio { get; }       // NY!

        public bool IsNew
        {
            get => _isNew;
            set
            {
                if (_isNew != value)
                {
                    _isNew = value;
                    OnPropertyChanged();
                }
            }
        }

        public TradeRowViewModel(
            string tradeId,
            string counterparty,
            string ccyPair,
            string buySell,
            string callPut,
            decimal? strike,
            DateTime? expiryDate,
            decimal notional,
            string notionalCcy,
            decimal? premium,
            string premiumCcy,
            string portfolioMx3,
            string trader,
            string status,
            DateTime time,
            string system,
            string product,
            decimal? spotRate = null,
            decimal? swapPoints = null,
            DateTime? settlementDate = null,
            decimal? hedgeRate = null,
            string hedgeType = null,
            string calypsoPortfolio = null,
            bool isNew = false)
        {
            TradeId = tradeId ?? string.Empty;
            Counterparty = counterparty ?? string.Empty;
            CcyPair = ccyPair ?? string.Empty;
            BuySell = buySell ?? string.Empty;
            CallPut = callPut ?? string.Empty;
            Strike = strike;
            ExpiryDate = expiryDate;
            Notional = notional;
            NotionalCcy = notionalCcy ?? string.Empty;
            Premium = premium;
            PremiumCcy = premiumCcy ?? string.Empty;
            PortfolioMx3 = portfolioMx3 ?? string.Empty;
            Trader = trader ?? string.Empty;
            Status = status ?? string.Empty;
            Time = time;
            System = system ?? string.Empty;
            Product = product ?? string.Empty;
            SpotRate = spotRate;
            SwapPoints = swapPoints;
            SettlementDate = settlementDate;
            HedgeRate = hedgeRate;
            HedgeType = hedgeType ?? string.Empty;
            CalypsoPortfolio = calypsoPortfolio ?? string.Empty;
            _isNew = isNew;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
