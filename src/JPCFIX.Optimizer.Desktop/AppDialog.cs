using System.Windows;
using System.Windows.Controls;
namespace JPCFIX.Optimizer.Desktop;

internal static class AppDialog
{
    public static bool Confirm(string title, string text)
    {
        var window = new Window { Title = "JPCFIX · " + title, Owner = System.Windows.Application.Current.MainWindow,
            Width = Math.Min(640, SystemParameters.WorkArea.Width - 32), Height = Math.Min(520, SystemParameters.WorkArea.Height - 32),
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Style = (Style)System.Windows.Application.Current.FindResource("ModalWindow") };
        var panel = new DockPanel { Margin = new Thickness(24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, Style = (Style)window.FindResource("SecondaryButton") };
        var confirm = new Button { Content = "Confirmar" };
        cancel.Click += (_, _) => window.DialogResult = false; confirm.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var heading = new TextBlock { Text = title, Style = (Style)window.FindResource("SectionTitle"), Margin = new Thickness(0, 0, 0, 18) };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = text, LineHeight = 22 } });
        window.Content = panel; window.Loaded += (_, _) => cancel.Focus();
        return window.ShowDialog() == true;
    }
}
