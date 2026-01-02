using FxTradeHub.Data.MySql.Repositories;
using FxTradeHub.Domain.Interfaces;
using FxTradeHub.Services;
using OptionSuite.Blotter.Wpf.ViewModels;
using FxSharedConfig;
using FxTradeHub.Services.Blotter;

namespace OptionSuite.Blotter.Host.Wpf
{
    internal static class BlotterCompositionRoot
    {

        public static BlotterRootViewModel CreateRootViewModel()
        {
            var cs = AppDbConfig.GetConnectionString("trade_stp");
            var repo = new MySqlStpRepositoryAsync(cs);

            var readService = new BlotterReadServiceAsync(repo);
            var commandService = new BlotterCommandServiceAsync(repo);

            return new BlotterRootViewModel(readService, commandService);
        }


    }
}
