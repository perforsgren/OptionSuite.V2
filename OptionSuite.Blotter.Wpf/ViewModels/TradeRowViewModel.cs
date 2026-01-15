using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OptionSuite.Blotter.Wpf.ViewModels
{
    public sealed class TradeRowViewModel : INotifyPropertyChanged
    {
        private bool _isNew;
        private bool _isUpdated;

        // Flagga för om raden är i edit-mode
        private bool _isEditing;

        public bool HasMargin => Margin != null && Margin != 0;

        public event PropertyChangedEventHandler PropertyChanged;
        //public event EventHandler<string> OnPortfolioMx3Changed;
        //public event EventHandler<string> OnCalypsoPortfolioChanged;

        // Primärnyckel i STP (behövs för att hämta links/events per trade)
        public long StpTradeId { get; }

        private string _portfolioMx3;
        private string _calypsoPortfolio;

        // Originalvärden för rollback
        private string _originalPortfolioMx3;
        private string _originalCalypsoPortfolio;

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
        public DateTime? PremiumDate { get; }

        // EDITABLE FIELDS
        public string PortfolioMx3
        {
            get => _portfolioMx3;
            set
            {
                if (_portfolioMx3 != value)
                {
                    _portfolioMx3 = value;
                    OnPropertyChanged();
                }
            }
        }

        //public string PortfolioMx3 { get; }
        public string Trader { get; }
        public string Status { get; }
        public string Mx3Status { get; }
        public string CalypsoStatus { get; }

        // Gemensamma fält
        public DateTime Time { get; }
        public string System { get; }
        public string Product { get; }

        // Linear-specifika
        public decimal? SpotRate { get; }
        public decimal? SwapPoints { get; }
        public DateTime? SettlementDate { get; }
        public decimal? HedgeRate { get; }
        public string HedgeType { get; }

        // EDITABLE FIELDS
        public string CalypsoPortfolio
        {
            get => _calypsoPortfolio;
            set
            {
                if (_calypsoPortfolio != value)
                {
                    _calypsoPortfolio = value;
                    OnPropertyChanged();
                }
            }
        }

        //public string CalypsoPortfolio { get; }
        public string SettlementCurrency { get; }
        public bool? IsNonDeliverable { get; }
        public DateTime? FixingDate { get; }

        // Regulatory fields
        public string Mic { get; }
        public string Tvtic { get; }
        public string Isin { get; }
        public string InvDecisionId { get; }
        public string ReportingEntityId { get; }
        public decimal? Margin { get; }

        public bool StpFlag { get; }

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

        public bool IsUpdated
        {
            get => _isUpdated;
            set
            {
                if (_isUpdated != value)
                {
                    _isUpdated = value;
                    OnPropertyChanged();
                }
            }
        }

        // Metod för att spara ändringar
        public bool HasEditChanges()
        {
            return _originalPortfolioMx3 != _portfolioMx3 ||
                   _originalCalypsoPortfolio != _calypsoPortfolio;
        }

        public void BeginEdit()
        {
            _isEditing = true;
            _originalPortfolioMx3 = _portfolioMx3;
            _originalCalypsoPortfolio = _calypsoPortfolio;
        }

        public void CancelEdit()
        {
            _isEditing = false;
            _portfolioMx3 = _originalPortfolioMx3;
            _calypsoPortfolio = _originalCalypsoPortfolio;
            OnPropertyChanged(nameof(PortfolioMx3));
            OnPropertyChanged(nameof(CalypsoPortfolio));
        }

        public void EndEdit()
        {
            _isEditing = false;
        }

        public TradeRowViewModel(
            long stpTradeId,
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
            DateTime? premiumDate,
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
            string settlementCurrency = null,
            bool? isNonDeliverable = null,
            DateTime? fixingDate = null,
            string mx3Status = null,
            string calypsoStatus = null,
            string mic = null,
            string tvtic = null,
            string isin = null,
            string invDecisionId = null,
            string reportingEntityId = null,
            decimal? margin = null,
            bool stpFlag = false,
            bool isNew = false,
            bool isUpdated = false)
        {
            StpTradeId = stpTradeId;

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
            PremiumDate = premiumDate;

            // EDITABLE - initialize backing field directly (no event firing in ctor)
            _portfolioMx3 = portfolioMx3 ?? string.Empty;
            //PortfolioMx3 = portfolioMx3 ?? string.Empty;
            
            Trader = trader ?? string.Empty;

            // Beräkna aggregerad status från system-status
            Mx3Status = mx3Status ?? "New";
            CalypsoStatus = calypsoStatus ?? "New";
            Status = CalculateAggregatedStatus(Mx3Status, CalypsoStatus, status);

            Time = time;
            System = system ?? string.Empty;
            Product = product ?? string.Empty;
            SpotRate = spotRate;
            SwapPoints = swapPoints;
            SettlementDate = settlementDate;
            HedgeRate = hedgeRate;
            HedgeType = hedgeType ?? string.Empty;

            // EDITABLE - initialize backing field directly
            _calypsoPortfolio = calypsoPortfolio ?? string.Empty;
            //CalypsoPortfolio = calypsoPortfolio ?? string.Empty;
            
            SettlementCurrency = settlementCurrency ?? string.Empty;
            IsNonDeliverable = isNonDeliverable;
            FixingDate = fixingDate;

            Mic = mic ?? string.Empty;
            Tvtic = tvtic ?? string.Empty;
            Isin = isin ?? string.Empty;
            InvDecisionId = invDecisionId ?? string.Empty;
            ReportingEntityId = reportingEntityId ?? string.Empty;
            Margin = margin;

            StpFlag = stpFlag;

            _isNew = isNew;
            _isUpdated = isUpdated;

        }

        private string CalculateAggregatedStatus(string mx3Status, string calypsoStatus, string fallbackStatus)
        {
            // För Options: använd fallback (ingen dual-system booking)
            if (!string.IsNullOrEmpty(CallPut))
            {
                var status = fallbackStatus ?? "New";
                return ToProperCase(status);
            }

            // Normalisera statusar
            var mx3 = ToProperCase(mx3Status ?? "New");
            var caly = ToProperCase(calypsoStatus ?? "New");

            // För NDF: endast MX3 (CalypsoStatus är null eller "New")
            // För Spot/Fwd: båda MX3 och Calypso
            var hasCalypsoLink = !string.IsNullOrEmpty(calypsoStatus) &&
                                 caly != "New" &&
                                 caly != "Unknown";

            if (!hasCalypsoLink)
            {
                // Endast MX3 - returnera MX3 status direkt
                return mx3;
            }

            // För Linear med båda system: beräkna aggregerad status

            // Båda booked = fully booked
            if (mx3 == "Booked" && caly == "Booked")
                return "Booked";

            // Något system ERROR/FAILED/REJECTED = overall error
            if (mx3 == "Error" || caly == "Error" ||
                mx3 == "Failed" || caly == "Failed" ||
                mx3 == "Rejected" || caly == "Rejected")
                return "Error";

            // Ett system booked, ett inte = partial
            if ((mx3 == "Booked" && caly != "Booked") ||
                (caly == "Booked" && mx3 != "Booked"))
                return "Partial";

            // Något system pending = overall pending
            if (mx3 == "Pending" || caly == "Pending")
                return "Pending";

            // Annars new
            return "New";
        }

        private string ToProperCase(string status)
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


        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
