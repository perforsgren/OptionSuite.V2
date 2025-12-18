using FxTradeHub.Data.MySql.Repositories;
using FxTradeHub.Domain.Interfaces;
using FxTradeHub.Services;
using OptionSuite.Blotter.Wpf.ViewModels;

namespace OptionSuite.Blotter.Host.Wpf
{
    internal static class BlotterCompositionRoot
    {
         
        /// <summary>
        /// Bygger connection string för trade_stp-databasen.
        /// </summary>
        private static string BuildTradeStpConnectionString()
        {

            // TODO v2: flytta till config senare (App.config / Shell config)

            string username = "fxopt";
            string password = "fxopt987";

            return
                "Server=srv78506;Port=3306;Database=trade_stp;" +
                "User Id=" + username + ";" +
                "Password=" + password + ";" +
                "Connection Timeout=15;TreatTinyAsBoolean=false;";
        }

        public static IBlotterReadServiceAsync CreateReadService()
        {
            var cs = BuildTradeStpConnectionString();

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
