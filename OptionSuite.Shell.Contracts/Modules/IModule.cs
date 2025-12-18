using System;

namespace OptionSuite.Shell.Contracts.Modules
{
    /// <summary>
    /// Beskriver en modul som ska exponeras som en toppnivå-tab i Shell.
    /// Modulen levererar en root-viewmodel som Shell presenterar via WPF DataTemplates.
    /// </summary>
    public interface IModule
    {
        /// <summary>
        /// Stabilt id för modulen.
        /// </summary>
        string ModuleId { get; }

        /// <summary>
        /// Titel som visas i modulens tab.
        /// </summary>
        string Title { get; }

        /// <summary>
        /// Skapar modulens root-viewmodel.
        /// </summary>
        object CreateRootViewModel(IServiceProvider serviceProvider);
    }
}
