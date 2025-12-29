using FxTradeHub.Data.MySql.Repositories;
using FxTradeHub.Domain.Interfaces;
using FxTradeHub.Services;
using OptionSuite.Blotter.Wpf.ViewModels;
using FxSharedConfig;

namespace OptionSuite.Blotter.Host.Wpf
{
    internal static class BlotterCompositionRoot
    {
         
        public static IBlotterReadServiceAsync CreateReadService()
        {
            var cs = AppDbConfig.GetConnectionString("trade_stp"); 

            // VIKTIGT: detta måste vara en repo som implementerar IStpRepositoryAsync
            IStpRepositoryAsync repo = new MySqlStpRepositoryAsync(cs);

            return new BlotterReadServiceAsync(repo);
        }

        public static BlotterRootViewModel CreateRootViewModel()
        {
            var readService = CreateReadService();
            return new BlotterRootViewModel(readService);
        }

    }
}
