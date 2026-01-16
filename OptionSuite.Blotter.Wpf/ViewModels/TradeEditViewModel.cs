using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OptionSuite.Blotter.Wpf.Infrastructure;

namespace OptionSuite.Blotter.Wpf.ViewModels
{
    /// <summary>
    /// Edit mode: Edit existing trade, Duplicate (create new from template), or View (read-only).
    /// </summary>
    public enum TradeEditMode
    {
        Edit,
        Duplicate,
        View,
        Create
    }

    /// <summary>
    /// ViewModel for the Trade Edit Window.
    /// Supports both Edit (update existing) and Duplicate (create new) modes.
    /// Edit is only enabled for trades with Status = New or Error.
    /// </summary>
    public sealed class TradeEditViewModel : INotifyPropertyChanged
    {
        private readonly TradeEditMode _mode;
        private readonly Action<bool> _closeAction;
        private readonly Func<TradeEditViewModel, Task<bool>> _saveAction;

        private string _errorMessage;
        private bool _isSaving;

        // Original values for change detection
        private readonly string _originalCounterparty;
        private readonly string _originalBroker;
        private readonly string _originalCcyPair;
        private readonly string _originalBuySell;
        private readonly decimal _originalNotional;
        private readonly string _originalNotionalCcy;
        private readonly decimal? _originalHedgeRate;
        private readonly DateTime? _originalSettlementDate;
        private readonly string _originalCallPut;
        private readonly decimal? _originalStrike;
        private readonly DateTime? _originalExpiryDate;
        private readonly decimal? _originalPremium;
        private readonly string _originalPremiumCcy;
        private readonly DateTime? _originalPremiumDate;
        private readonly string _originalPortfolioMx3;
        private readonly string _originalCalypsoBook;

        public event PropertyChangedEventHandler PropertyChanged;

        // Trade identity (read-only)
        public long StpTradeId { get; }
        public string TradeId { get; }
        public string Status { get; }
        public string Product { get; }
        public string Trader { get; }

        // Editable fields
        private string _counterparty;
        public string Counterparty
        {
            get => _counterparty;
            set { _counterparty = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private string _broker;
        public string Broker
        {
            get => _broker;
            set { _broker = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private string _ccyPair;
        public string CcyPair
        {
            get => _ccyPair;
            set { _ccyPair = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private string _buySell;
        public string BuySell
        {
            get => _buySell;
            set { _buySell = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private decimal _notional;
        public decimal Notional
        {
            get => _notional;
            set { _notional = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private string _notionalCcy;
        public string NotionalCcy
        {
            get => _notionalCcy;
            set { _notionalCcy = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private decimal? _hedgeRate;
        public decimal? HedgeRate
        {
            get => _hedgeRate;
            set { _hedgeRate = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private DateTime? _settlementDate;
        public DateTime? SettlementDate
        {
            get => _settlementDate;
            set { _settlementDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        // Option fields
        private string _callPut;
        public string CallPut
        {
            get => _callPut;
            set { _callPut = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private decimal? _strike;
        public decimal? Strike
        {
            get => _strike;
            set { _strike = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private DateTime? _expiryDate;
        public DateTime? ExpiryDate
        {
            get => _expiryDate;
            set { _expiryDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private decimal? _premium;
        public decimal? Premium
        {
            get => _premium;
            set { _premium = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private string _premiumCcy;
        public string PremiumCcy
        {
            get => _premiumCcy;
            set { _premiumCcy = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private DateTime? _premiumDate;
        public DateTime? PremiumDate
        {
            get => _premiumDate;
            set { _premiumDate = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        // Routing fields
        private string _portfolioMx3;
        public string PortfolioMx3
        {
            get => _portfolioMx3;
            set { _portfolioMx3 = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        private string _calypsoBook;
        public string CalypsoBook
        {
            get => _calypsoBook;
            set { _calypsoBook = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasChanges)); OnPropertyChanged(nameof(CanSave)); }
        }

        // Regulatory fields (read-only)
        public DateTime? ExecutionTime { get; }
        public string Mic { get; }
        public string Tvtic { get; }
        public string Isin { get; }
        public string InvDecisionId { get; }
        public string InvDecisionName { get; }
        public string ReportingEntity { get; }

        // Dropdown values
        public ObservableCollection<string> PortfolioMx3Values { get; }
        public ObservableCollection<string> CalypsoBookValues { get; }
        public ObservableCollection<string> BuySellValues { get; }
        public ObservableCollection<string> CallPutValues { get; }

        // Computed properties
        public bool IsOption => !string.IsNullOrEmpty(_originalCallPut);
        public bool IsLinear => string.IsNullOrEmpty(_originalCallPut);

        /// <summary>
        /// True if editing is allowed (Status = New or Error, and mode is Edit).
        /// Duplicate mode always allows editing since we're creating a new trade.
        /// View mode is always read-only.
        /// </summary>
        public bool CanEdit => _mode == TradeEditMode.Duplicate ||
                               (_mode == TradeEditMode.Edit &&
                                (Status?.Equals("New", StringComparison.OrdinalIgnoreCase) == true ||
                                 Status?.Equals("Error", StringComparison.OrdinalIgnoreCase) == true));

        public bool IsReadOnly => !CanEdit;

        /// <summary>
        /// True if we're in View-only mode (read-only, no editing allowed).
        /// </summary>
        public bool IsViewMode => _mode == TradeEditMode.View;

        // Window title/subtitle - uppdatera med switch expression
        public string WindowTitle => _mode switch
        {
            TradeEditMode.Duplicate => "Duplicate Trade",
            TradeEditMode.View => "View Trade",
            _ => "Edit Trade"
        };

        public string WindowSubtitle => _mode switch
        {
            TradeEditMode.Duplicate => $"Creating new trade based on {TradeId}",
            TradeEditMode.View => $"Viewing {TradeId}",
            _ => $"Editing {TradeId}"
        };

        public string SaveButtonText => _mode switch
        {
            TradeEditMode.Duplicate => "Create Trade",
            _ => "Save Changes"
        };

        // Mode indicator styling - uppdatera för View mode
        public Brush ModeBackground => _mode switch
        {
            TradeEditMode.Duplicate => new SolidColorBrush(Color.FromArgb(0x1A, 0x3B, 0x82, 0xF6)),  // Blue tint
            TradeEditMode.View => new SolidColorBrush(Color.FromArgb(0x26, 0x94, 0xA3, 0xB8)),       // Slate 400 - mer opacity
            _ => new SolidColorBrush(Color.FromArgb(0x1A, 0x2D, 0xD4, 0xBF))                         // Cyan tint
        };

        public Brush ModeBorderBrush => _mode switch
        {
            TradeEditMode.Duplicate => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),  // Blue
            TradeEditMode.View => new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),       // Slate 400 - ljusare
            _ => new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF))                         // Cyan
        };

        public Brush ModeForeground => _mode switch
        {
            TradeEditMode.Duplicate => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),  // Blue
            TradeEditMode.View => new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),       // Slate 300 - mycket ljusare
            _ => new SolidColorBrush(Color.FromRgb(0x2D, 0xD4, 0xBF))                         // Cyan
        };

        public string ModeText => _mode switch
        {
            TradeEditMode.Duplicate => "DUPLICATE MODE",
            TradeEditMode.View => "READ-ONLY",
            _ => "EDIT MODE"
        };

        public bool CanSave => _mode == TradeEditMode.View || (CanEdit && HasChanges && !_isSaving);

        public bool HasChanges
        {
            get
            {
                if (_mode == TradeEditMode.Duplicate)
                    return true; // Duplicate always has "changes" (new trade)

                return _counterparty != _originalCounterparty ||
                       _broker != _originalBroker ||
                       _ccyPair != _originalCcyPair ||
                       _buySell != _originalBuySell ||
                       _notional != _originalNotional ||
                       _notionalCcy != _originalNotionalCcy ||
                       _hedgeRate != _originalHedgeRate ||
                       _settlementDate != _originalSettlementDate ||
                       _callPut != _originalCallPut ||
                       _strike != _originalStrike ||
                       _expiryDate != _originalExpiryDate ||
                       _premium != _originalPremium ||
                       _premiumCcy != _originalPremiumCcy ||
                       _premiumDate != _originalPremiumDate ||
                       _portfolioMx3 != _originalPortfolioMx3 ||
                       _calypsoBook != _originalCalypsoBook;
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        // Commands
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public TradeEditViewModel(
            TradeRowViewModel source,
            TradeEditMode mode,
            ObservableCollection<string> portfolioMx3Values,
            ObservableCollection<string> calypsoBookValues,
            Func<TradeEditViewModel, Task<bool>> saveAction,
            Action<bool> closeAction)
        {
            _mode = mode;
            _saveAction = saveAction ?? throw new ArgumentNullException(nameof(saveAction));
            _closeAction = closeAction ?? throw new ArgumentNullException(nameof(closeAction));

            PortfolioMx3Values = portfolioMx3Values ?? new ObservableCollection<string>();
            CalypsoBookValues = calypsoBookValues ?? new ObservableCollection<string>();

            // Populate Buy/Sell and Call/Put dropdown values
            BuySellValues = new ObservableCollection<string> { "Buy", "Sell" };
            CallPutValues = new ObservableCollection<string> { "Call", "Put" };

            // Copy values from source
            StpTradeId = source.StpTradeId;
            TradeId = mode == TradeEditMode.Duplicate ? "(New)" : source.TradeId;
            Status = mode == TradeEditMode.Duplicate ? "New" : source.Status;
            Product = source.Product;
            Trader = source.Trader;

            // Initialize editable fields and store originals
            _counterparty = _originalCounterparty = source.Counterparty;
            _broker = _originalBroker = string.Empty;
            _ccyPair = _originalCcyPair = source.CcyPair;
            _buySell = _originalBuySell = source.BuySell;
            _notional = _originalNotional = source.Notional;
            _notionalCcy = _originalNotionalCcy = source.NotionalCcy;
            _hedgeRate = _originalHedgeRate = source.HedgeRate;
            _settlementDate = _originalSettlementDate = source.SettlementDate;
            _callPut = _originalCallPut = source.CallPut;
            _strike = _originalStrike = source.Strike;
            _expiryDate = _originalExpiryDate = source.ExpiryDate;
            _premium = _originalPremium = source.Premium;
            _premiumCcy = _originalPremiumCcy = source.PremiumCcy;
            _premiumDate = _originalPremiumDate = source.PremiumDate;
            _portfolioMx3 = _originalPortfolioMx3 = source.PortfolioMx3;
            _calypsoBook = _originalCalypsoBook = source.CalypsoPortfolio;

            // Regulatory fields (read-only)
            ExecutionTime = source.Time;
            Mic = source.Mic;
            Tvtic = source.Tvtic;
            Isin = source.Isin;
            InvDecisionId = source.InvDecisionId;
            InvDecisionName = "N/A"; // Not available in TradeRowViewModel - can be added if needed
            ReportingEntity = source.ReportingEntityId;

            SaveCommand = new RelayCommand(ExecuteSave, () => CanSave);
            CancelCommand = new RelayCommand(ExecuteCancel);
        }

        // Konstruktor för Create mode (tom trade)
        public TradeEditViewModel(
            TradeEditMode mode,  // Måste vara Create
            ObservableCollection<string> portfolioMx3Values,
            ObservableCollection<string> calypsoBookValues,
            Func<TradeEditViewModel, Task<bool>> saveAction,
            Action<bool> closeAction)
        {
            _mode = mode;
            _saveAction = saveAction;
            _closeAction = closeAction;
            
            // Default values för ny trade
            StpTradeId = 0;  // Kommer genereras vid save
            TradeId = "(New)";
            Status = "New";
            Trader = Environment.UserName;
            
            // Alla fält tomma/default
            _counterparty = _originalCounterparty = string.Empty;
            _ccyPair = _originalCcyPair = string.Empty;
            _buySell = _originalBuySell = "Buy";
            _notional = _originalNotional = 0;
            _settlementDate = _originalSettlementDate = DateTime.Today.AddDays(2);
            _callPut = _originalCallPut = "Call";
            _strike = _originalStrike = 0;
            _expiryDate = _originalExpiryDate = DateTime.Today.AddDays(30);
            _premium = _originalPremium = 0;
            _premiumCcy = _originalPremiumCcy = string.Empty;
            _premiumDate = _originalPremiumDate = DateTime.Today;
            _portfolioMx3 = _originalPortfolioMx3 = string.Empty;
            _calypsoBook = _originalCalypsoBook = string.Empty;

            PortfolioMx3Values = portfolioMx3Values ?? new ObservableCollection<string>();
            CalypsoBookValues = calypsoBookValues ?? new ObservableCollection<string>();

            // Regulatory fields (read-only)
            ExecutionTime = null;
            Mic = string.Empty;
            Tvtic = string.Empty;
            Isin = string.Empty;
            InvDecisionId = string.Empty;
            InvDecisionName = "N/A"; // Not available in TradeRowViewModel - can be added if needed
            ReportingEntity = string.Empty;

            SaveCommand = new RelayCommand(ExecuteSave, () => CanSave);
            CancelCommand = new RelayCommand(ExecuteCancel);
        }

        private async void ExecuteSave()
        {
            if (!CanSave)
                return;

            try
            {
                _isSaving = true;
                ErrorMessage = null;
                OnPropertyChanged(nameof(CanSave));

                var success = await _saveAction(this).ConfigureAwait(true);

                if (success)
                {
                    _closeAction(true);
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Save failed: {ex.Message}";
            }
            finally
            {
                _isSaving = false;
                OnPropertyChanged(nameof(CanSave));
            }
        }

        private void ExecuteCancel()
        {
            _closeAction(false);
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}