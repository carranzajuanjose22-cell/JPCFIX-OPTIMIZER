using System.Windows;
using System.Windows.Controls;
namespace JPCFIX.Optimizer.Desktop;

// A real content host exposes the selected page to keyboard and accessibility clients.
public sealed class PageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Home { get; set; }
    public DataTemplate? Diagnostics { get; set; }
    public DataTemplate? Optimization { get; set; }
    public DataTemplate? History { get; set; }
    public DataTemplate? Settings { get; set; }
    public DataTemplate? Backup { get; set; }
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is int index ? index switch
    { 0 => Home, 1 => Diagnostics, 2 => Optimization, 3 => History, 4 => Settings, 5 => Backup, _ => Home } : Home;
}
