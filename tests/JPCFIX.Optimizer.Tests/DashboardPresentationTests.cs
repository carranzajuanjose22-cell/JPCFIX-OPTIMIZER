using JPCFIX.Optimizer.Core;
using JPCFIX.Optimizer.Desktop;
using Xunit;
namespace JPCFIX.Optimizer.Tests;

public sealed class DashboardPresentationTests
{
    [Fact] public void BeforeAnalysisNoSystemFactsAreInvented()
    { var data = DashboardData.From(null); Assert.False(data.Analyzed); Assert.Equal("—", data.Cpu); Assert.Equal("—", data.Ram); Assert.Equal("Sin análisis", data.General); Assert.Equal("Sin datos", data.Windows); }
    [Fact] public void ProjectsOnlyValuesReadByDiagnosticEngine()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        var report = new DiagnosticReport(DateTimeOffset.UtcNow, [new("CPU", "Procesador", "CPU de prueba"), new("CPU", "Carga (muestra)", "17%"), new("RAM", "Instalada", "12 GiB"), new("Windows", "Edición", "Windows de prueba"), new("Almacenamiento", root, "40 GiB libres / 100 GiB · Fixed")], []);
        var data = DashboardData.From(report);
        Assert.True(data.Analyzed); Assert.Equal("17%", data.Cpu); Assert.Equal("CPU de prueba", data.Processor); Assert.Equal("12 GiB", data.Ram); Assert.Equal("40 GiB", data.Storage); Assert.Contains(root, data.StorageDescription);
        Assert.DoesNotContain("SSD", data.StorageDetail); Assert.Equal("Análisis leído", data.General);
    }
    [Fact] public void MissingProvidersRemainUnavailableInsteadOfBeingHealthy()
    { var data = DashboardData.From(new(DateTimeOffset.UtcNow, [], ["Proveedor no disponible"])); Assert.Equal("No disponible", data.Cpu); Assert.Equal("No disponible", data.Ram); Assert.Equal("Revisar avisos", data.General); Assert.Contains("parcial", data.Notice); }
    [Fact] public void SidebarAdaptsWithoutChangingNavigationIndices()
    { var converter = new SidebarWidthConverter(); Assert.Equal(196d, converter.Convert(700d, typeof(double), null!, System.Globalization.CultureInfo.InvariantCulture)); Assert.Equal(240d, converter.Convert(1366d, typeof(double), null!, System.Globalization.CultureInfo.InvariantCulture)); }
    [Fact] public void TableKeepsAnAccessibleViewportWhenWindowIsShort()
    { var converter = new TableHeightConverter(); Assert.Equal(240d, converter.Convert(200d, typeof(double), null!, System.Globalization.CultureInfo.InvariantCulture)); }
}
