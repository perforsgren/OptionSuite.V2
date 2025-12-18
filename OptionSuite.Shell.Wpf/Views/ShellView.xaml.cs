using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OptionSuite.Shell.Wpf.Infrastructure;

namespace OptionSuite.Shell.Wpf.Views
{
    public partial class ShellView : UserControl
    {
        public ShellView()
        {
            InitializeComponent();
        }

        private Window GetHostWindow()
        {
            return Window.GetWindow(this);
        }

        private void HandleTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            var wnd = GetHostWindow();
            if (wnd == null)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                HandleMaxRestoreClick(sender, e);
                return;
            }

            try
            {
                wnd.DragMove();
            }
            catch
            {
                // ignore drag exceptions (happens in some edge cases)
            }
        }

        private void HandleMinimizeClick(object sender, RoutedEventArgs e)
        {
            var wnd = GetHostWindow();
            if (wnd == null) return;

            wnd.WindowState = WindowState.Minimized;
        }

        private void HandleMaxRestoreClick(object sender, RoutedEventArgs e)
        {
            var wnd = GetHostWindow();
            if (wnd == null) return;

            wnd.WindowState = (wnd.WindowState == WindowState.Maximized)
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void HandleCloseClick(object sender, RoutedEventArgs e)
        {
            var wnd = GetHostWindow();
            if (wnd == null) return;

            wnd.Close();
        }

        private void HandleModuleClick(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null)
            {
                return;
            }

            var idText = btn.CommandParameter as string;
            if (string.IsNullOrEmpty(idText))
            {
                return;
            }

            ShellModuleId id;
            if (!Enum.TryParse(idText, out id))
            {
                return;
            }

            ShellSelection.SetSelectedModule(this, id);
        }
    }
}
