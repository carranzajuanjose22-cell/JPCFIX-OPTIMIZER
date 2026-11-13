using System.Globalization;
using System.Windows;
using System.Windows.Data;
using JPCFIX.Optimizer.Core;
namespace JPCFIX.Optimizer.Desktop;

public sealed record NavigationEntry(int Index, string Label, string Icon);
public sealed record DashboardData(bool Analyzed, string Cpu, string CpuDescription, string Ram, string Storage, string StorageDescription, string General, string GeneralDescription, string Windows, string Processor, string StorageDetail, string Notice, string Captured)
{
    public static DashboardData From(DiagnosticReport? report)
    {
        if (report == null) return new(false, "—", "Sin datos", "—", "—", "Sin datos", "Sin análisis", "Requiere evaluación", "Sin datos", "Sin datos", "Sin datos", "Ejecutá un análisis para conocer el estado actual del equipo y la energía disponible.", "El equipo no ha sido analizado.");
        string Read(string category, string name) => report.Items.FirstOrDefault(i => i.Category == category && i.Name == name)?.Value ?? "No disponible";
        var volume = report.Items.FirstOrDefault(i => i.Category == "Almacenamiento" && i.Name.Equals(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase)) ?? report.Items.FirstOrDefault(i => i.Category == "Almacenamiento");
        var processor = Read("CPU", "Procesador");
        return new(true, report.Items.FirstOrDefault(i => i.Category == "CPU" && i.Name.StartsWith("Carga"))?.Value ?? "No disponible", processor, Read("RAM", "Instalada"), volume?.Value.Split(" libres")[0] ?? "No disponible", volume == null ? "Sin información de volúmenes" : $"Libres en {volume.Name}", report.Warnings.Count > 0 ? "Revisar avisos" : "Análisis leído", $"{report.Warnings.Count} aviso(s) del diagnóstico", Read("Windows", "Edición") + " · " + Read("Windows", "Versión / compilación"), processor, volume == null ? "No disponible" : $"{volume.Name} · {volume.Value}", report.Warnings.Count == 0 ? "Lectura completada. La información es una instantánea; evaluá cada cambio antes de aplicarlo." : "Hay información parcial o advertencias. Revisá los detalles antes de realizar cambios.", $"Último análisis: {report.CapturedAt.ToLocalTime():g}");
    }
}
public sealed class CompactVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double width && width < 1000 ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class TableHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Math.Max(240, value is double height ? height - 260 : 340);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class SidebarWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double width && width < 1000 ? 196d : 240d;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
public sealed class ChangeStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is ChangeStatus status ? status switch { ChangeStatus.Applied => "Aplicado y verificado", ChangeStatus.Restored => "Restaurado y verificado", ChangeStatus.Prepared => "Preparado / pendiente", ChangeStatus.RecoveryRequired => "Requiere recuperación", _ => "Fallido" } : "No disponible";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
