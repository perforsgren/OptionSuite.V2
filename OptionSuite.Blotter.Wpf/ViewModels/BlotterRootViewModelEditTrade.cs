using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using OptionSuite.Blotter.Wpf.Infrastructure;
using OptionSuite.Blotter.Wpf.Views;

namespace OptionSuite.Blotter.Wpf.ViewModels
{
    public sealed partial class BlotterRootViewModel
    {
        public ICommand EditTradeCommand { get; private set; }

        private void InitializeEditCommands()
        {
            EditTradeCommand = new RelayCommand(
                execute: () => OpenEditTradeWindow(TradeEditMode.Edit),
                canExecute: CanExecuteEditTrade
            );

            DuplicateTradeCommand = new RelayCommand(
                execute: () => OpenEditTradeWindow(TradeEditMode.Duplicate),
                canExecute: () => GetSelectedTrade() != null
            );
        }

        private bool CanExecuteEditTrade()
        {
            var trade = GetSelectedTrade();
            if (trade == null)
                return false;

            var status = trade.Status?.ToUpperInvariant() ?? "";
            return status == "NEW" || status == "ERROR";
        }

        private void OpenEditTradeWindow(TradeEditMode mode)
        {
            var selectedTrade = GetSelectedTrade();
            if (selectedTrade == null)
                return;

            if (mode == TradeEditMode.Edit && !CanExecuteEditTrade())
            {
                MessageBox.Show(
                    "This trade cannot be edited. Only trades with status 'New' or 'Error' can be edited.",
                    "Cannot Edit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var editVm = new TradeEditViewModel(
                source: selectedTrade,
                mode: mode,
                portfolioMx3Values: PortfolioMx3Values,
                calypsoBookValues: BookCalypsoValues,
                saveAction: SaveTradeFromEditWindowAsync,
                closeAction: (saved) => { }
            );

            var editWindow = new TradeEditWindow
            {
                DataContext = editVm,
                Owner = Application.Current.MainWindow
            };

            var result = editWindow.ShowDialog();

            if (result == true)
            {
                _ = RefreshAsync();
            }
        }

        private async Task<bool> SaveTradeFromEditWindowAsync(TradeEditViewModel editVm)
        {
            if (editVm == null)
                return false;

            var isDuplicate = editVm.WindowTitle.Contains("Duplicate");

            if (isDuplicate)
            {
                Debug.WriteLine($"[BlotterVM] Duplicating trade {editVm.StpTradeId}");

                // TODO: Implementera DuplicateTradeAsync i BlotterCommandServiceAsync
                // var result = await _commandService.DuplicateTradeAsync(...).ConfigureAwait(true);
                
                MessageBox.Show("Duplicate not yet implemented", "TODO", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            else
            {
                // Använd befintlig metod tills vidare (uppdaterar bara routing)
                await _commandService.UpdateTradeRoutingFieldsAsync(
                    stpTradeId: editVm.StpTradeId,
                    portfolioMx3: editVm.PortfolioMx3,
                    calypsoBook: editVm.CalypsoBook,
                    userId: Environment.UserName
                ).ConfigureAwait(true);

                Debug.WriteLine($"[BlotterVM] Trade {editVm.StpTradeId} routing updated");
                return true;
            }
        }
    }
}