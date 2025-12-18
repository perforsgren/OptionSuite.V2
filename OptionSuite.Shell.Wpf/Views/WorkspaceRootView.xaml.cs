using System.Windows.Controls;

namespace OptionSuite.Shell.Wpf.Views
{
    /// <summary>
    /// Root-view för ett workspace (modul-instans).
    /// Innehåller alltid Header + Content så att allt följer med vid Undock.
    /// </summary>
    public partial class WorkspaceRootView : UserControl
    {
        public WorkspaceRootView()
        {
            InitializeComponent();
        }
    }
}
