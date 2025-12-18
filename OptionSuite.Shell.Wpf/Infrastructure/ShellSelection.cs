using System.Windows;

namespace OptionSuite.Shell.Wpf.Infrastructure
{
    public static class ShellSelection
    {
        public static readonly DependencyProperty SelectedModuleProperty =
            DependencyProperty.RegisterAttached(
                "SelectedModule",
                typeof(ShellModuleId),
                typeof(ShellSelection),
                new FrameworkPropertyMetadata(ShellModuleId.Blotter, FrameworkPropertyMetadataOptions.Inherits));

        public static void SetSelectedModule(DependencyObject element, ShellModuleId value)
        {
            element.SetValue(SelectedModuleProperty, value);
        }

        public static ShellModuleId GetSelectedModule(DependencyObject element)
        {
            return (ShellModuleId)element.GetValue(SelectedModuleProperty);
        }
    }
}
