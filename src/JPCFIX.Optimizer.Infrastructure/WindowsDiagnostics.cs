using System.Diagnostics;
using System.Management;
using Microsoft.Win32;
using JPCFIX.Optimizer.Core;

namespace JPCFIX.Optimizer.Infrastructure;

public sealed class WindowsDiagnostics : IDiagnosticService
{
    public Task<DiagnosticReport> ScanAsync(CancellationToken ct) => Task.Run(() =>
    {
        var items = new List<DiagnosticItem>();
        var warnings = new List<string>();
        void Add(string category, string name, object? value) => items.Add(new(category, name, value?.ToString() ?? "No disponible"));
        void Query(string category, string query, Action<ManagementBaseObject> read, string scope = @"root\cimv2")
        {
            try
            {
                using var search = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query), new EnumerationOptions { Timeout = TimeSpan.FromSeconds(10), ReturnImmediately = false });
                using var results = search.Get();
                if (results.Count == 0) Add(category, "Datos", null);
                foreach (ManagementBaseObject row in results) { using (row) { ct.ThrowIfCancellationRequested(); read(row); } }
            }
            catch (OperationCanceledException) { throw; }
            catch { warnings.Add($"{category}: información parcial o no disponible (WMI/permisos/proveedor)."); }
        }
        Add("Equipo", "Nombre", Environment.MachineName);
        Add("CPU", "Arquitectura del sistema", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
        Query("CPU", "SELECT Name,NumberOfCores,NumberOfLogicalProcessors,LoadPercentage FROM Win32_Processor", r =>
        { Add("CPU", "Procesador", r["Name"]); Add("CPU", "Núcleos / hilos", $"{r["NumberOfCores"]} / {r["NumberOfLogicalProcessors"]}"); Add("CPU", "Carga (muestra del proveedor WMI; no promedio histórico)", r["LoadPercentage"] is null ? null : $"{r["LoadPercentage"]}%"); });
        Query("Windows", "SELECT Caption,Version,BuildNumber,TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem", r =>
        { Add("Windows", "Edición", r["Caption"]); Add("Windows", "Versión / compilación", $"{r["Version"]} / {r["BuildNumber"]}"); Add("RAM", "Visible para Windows", KiB(r["TotalVisibleMemorySize"])); Add("RAM", "Disponible (instantánea)", KiB(r["FreePhysicalMemory"])); });
        Query("RAM", "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", r => Add("RAM", "Instalada", Bytes(r["TotalPhysicalMemory"])));
        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            try { if (drive.IsReady) Add("Almacenamiento", drive.Name, $"{Bytes(drive.AvailableFreeSpace)} libres / {Bytes(drive.TotalSize)} · {drive.DriveType}"); }
            catch { warnings.Add($"Unidad {drive.Name}: acceso no disponible."); }
        }
        Query("Discos físicos", "SELECT FriendlyName,MediaType,Size FROM MSFT_PhysicalDisk", r =>
        {
            var type = Convert.ToInt32(r["MediaType"] ?? 0) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "No disponible" };
            Add("Discos físicos", r["FriendlyName"]?.ToString() ?? "Disco", $"{type} · {Bytes(r["Size"])} (sin inferir correspondencia con volúmenes)");
        }, @"root\Microsoft\Windows\Storage");
        Query("Inicio", "SELECT Name,Location,User FROM Win32_StartupCommand", r => Add("Inicio", r["Name"]?.ToString() ?? "Entrada", r["Location"]));
        Add("Inicio", "Alcance", "Inventario WMI parcial; no incluye necesariamente todas las fuentes de inicio.");
        foreach (var process in Process.GetProcesses().OrderBy(p => p.Id))
        {
            using (process)
            {
                ct.ThrowIfCancellationRequested();
                try { Add("Procesos", $"{process.ProcessName} (PID {process.Id})", $"RAM de trabajo: {Bytes(process.WorkingSet64)} (instantánea)"); }
                catch { Add("Procesos", "Proceso finalizado o protegido", null); }
            }
        }
        Add("Energía", "Alimentación", WindowsPowerService.PowerSource());
        Add("Diagnóstico", "Cambios realizados", "Ninguno. Esta lectura no modifica Windows.");
        return new DiagnosticReport(DateTimeOffset.UtcNow, items, warnings);
    }, ct);
    private static string KiB(object? value) => value is null ? "No disponible" : $"{Convert.ToDouble(value) / 1048576:F2} GiB";
    private static string Bytes(object? value) => value is null ? "No disponible" : $"{Convert.ToDouble(value) / 1073741824:F2} GiB";
}
