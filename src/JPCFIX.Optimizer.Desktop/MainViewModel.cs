using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using JPCFIX.Optimizer.Core;
using JPCFIX.Optimizer.Infrastructure;

namespace JPCFIX.Optimizer.Desktop;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IDiagnosticService diagnostic;
    private readonly IOptimization optimization;
    private readonly IChangeStore store;
    private readonly IPrivilegedExecutor executor;
    private readonly List<AsyncCommand> commands = [];
    private ExecutionPlan? preview;
    private DiagnosticReport? report;
    private bool busy;
    private int page;
    private string status = "Listo. El diagnóstico y la simulación no modifican Windows.";
    private string summary = "Todavía no se analizó el equipo.", warnings = "", powerStatus = "", previewText = "Simulá para ver el estado original y la propuesta.", recommendations = "Sin evaluación todavía.", restoreStatus = "Sin consultar.", reportDirectory = "";
    private PowerPlan? selectedPlan;
    private ChangeRecord? selectedChange;
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JPCFIX.Optimizer", "settings.json");
    public BackupViewModel Backup { get; }
    public IReadOnlyList<NavigationEntry> Navigation { get; } = [new(0, "Inicio", "Home"), new(1, "Diagnóstico", "Diagnostic"), new(2, "Optimizaciones", "Rocket"), new(3, "Historial", "History"), new(5, "Smart Backup", "Database"), new(4, "Configuración", "Settings")];
    private DashboardData dashboard = DashboardData.From(null);
    public DashboardData Dashboard { get => dashboard; private set { Set(ref dashboard, value); PresentAnalysis(); } }
    public string AnalysisState => Busy ? "Procesando" : Dashboard.Analyzed ? "Diagnóstico disponible" : "Sin análisis";
    public string AnalysisHeading => Dashboard.Analyzed ? "Diagnóstico disponible para revisión" : "El equipo aún no ha sido analizado.";
    public string VersionText => "JPCFIX Optimizer v" + typeof(MainViewModel).Assembly.GetName().Version?.ToString(3);
    public IEnumerable<ChangeRecord> RecentActivity => History.OrderByDescending(r => r.CreatedAt).Take(3);
    public string OptimizationName => optimization.Name;
    public string OptimizationDescription => optimization.Description;
    public string OptimizationRisk => optimization.RiskLevel switch { RiskLevel.Low => "Bajo", RiskLevel.Moderate => "Moderado", _ => "Alto" };
    public string OptimizationAvailability => preview?.Compatibility.Reason ?? Recommendations;
    public MainViewModel(IDiagnosticService diagnostic, IOptimization optimization, IChangeStore store, IPrivilegedExecutor executor, BackupViewModel backup)
    {
        Backup = backup;
        this.diagnostic = diagnostic; this.optimization = optimization; this.store = store; this.executor = executor;
        ReportDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JPCFIX.Optimizer", "Reports");
        ScanCommand = Command(ScanAsync); RefreshCommand = Command(RefreshAsync);
        PreviewCommand = Command(PreviewAsync, () => SelectedPlan != null);
        ApplyCommand = Command(ApplyAsync, () => preview is { Compatibility.IsApplicable: true, CanRollback: true } p && p.Target != p.Original);
        HistoryCommand = Command(ReadHistoryAsync);
        RollbackCommand = Command(RollbackAsync, () => SelectedChange != null && SelectedChange.Status != ChangeStatus.Restored);
        OptimizePageCommand = Command(() => { Page = 2; return Task.CompletedTask; });
        HistoryPageCommand = Command(async () => { Page = 3; await ReadHistoryAsync(); });
        SaveSettingsCommand = Command(SaveSettingsAsync); ExportCommand = Command(ExportAsync, () => report != null);
        DetectRestoreCommand = Command(async () => { var points = await Task.Run(() => new RestorePoints().Read()); RestoreStatus = $"Proveedor disponible · {points.Count} punto(s) detectado(s). Esto no garantiza que Windows permita crear uno nuevo."; });
        RestoreCommand = Command(async () =>
        {
            if (!Confirm("¿Solicitar un punto de restauración adicional a Windows? Se pedirá elevación UAC.")) return;
            await executor.CreateRestorePointAsync(); RestoreStatus = "El ejecutor comprobó un nuevo punto de restauración.";
        });
    }
    public ObservableCollection<DiagnosticItem> Diagnostics { get; } = [];
    public ObservableCollection<PowerPlan> Plans { get; } = [];
    public ObservableCollection<ChangeRecord> History { get; } = [];
    public ICommand ScanCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand ApplyCommand { get; }
    public ICommand HistoryCommand { get; }
    public ICommand RollbackCommand { get; }
    public ICommand OptimizePageCommand { get; }
    public ICommand HistoryPageCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand DetectRestoreCommand { get; }
    public ICommand RestoreCommand { get; }
    public bool Busy { get => busy; private set { Set(ref busy, value); PresentAnalysis(); NotifyCommands(); } }
    public int Page { get => page; set => Set(ref page, value); }
    public string Status { get => status; private set => Set(ref status, value); }
    public string Summary { get => summary; private set => Set(ref summary, value); }
    public string Warnings { get => warnings; private set => Set(ref warnings, value); }
    public string PowerStatus { get => powerStatus; private set => Set(ref powerStatus, value); }
    public string PreviewText { get => previewText; private set => Set(ref previewText, value); }
    public string Recommendations { get => recommendations; private set => Set(ref recommendations, value); }
    public string RestoreStatus { get => restoreStatus; private set => Set(ref restoreStatus, value); }
    public string ReportDirectory { get => reportDirectory; set => Set(ref reportDirectory, value); }
    public string StorageText => $"Respaldos obligatorios: {JsonChangeStore.DefaultDirectory}\nConfiguración del usuario: {SettingsPath}";
    public PowerPlan? SelectedPlan { get => selectedPlan; set { Set(ref selectedPlan, value); preview = null; PreviewText = "La selección cambió. Volvé a simular."; PropertyChanged?.Invoke(this, new(nameof(OptimizationAvailability))); NotifyCommands(); } }
    public ChangeRecord? SelectedChange { get => selectedChange; set { Set(ref selectedChange, value); NotifyCommands(); } }
    public Task InitializeAsync() => RunAsync(async () =>
    {
        if (File.Exists(SettingsPath))
        {
            try { var settings = JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(SettingsPath)); if (settings != null && Path.IsPathFullyQualified(settings.ReportDirectory)) ReportDirectory = settings.ReportDirectory; }
            catch { Status = "No se pudo leer la configuración; se usa el directorio predeterminado."; }
        }
        await RefreshAsync(); await ReadHistoryAsync();
    });
    private async Task ScanAsync()
    {
        Status = "Analizando hardware y Windows…";
        report = await diagnostic.ScanAsync(CancellationToken.None);
        Dashboard = DashboardData.From(report);
        Diagnostics.Clear(); foreach (var item in report.Items) Diagnostics.Add(item);
        Warnings = string.Join("\n", report.Warnings);
        Summary = string.Join("\n", report.Items.Where(i => i.Category is "CPU" or "RAM" or "Almacenamiento" or "Windows").Select(i => $"{i.Name}: {i.Value}"));
        await RefreshAsync(); Status = $"Diagnóstico finalizado: {report.CapturedAt.ToLocalTime():g}. {report.Warnings.Count} advertencia(s).";
    }
    private async Task RefreshAsync()
    {
        preview = null;
        var state = await Task.Run(optimization.ReadCurrentState);
        Plans.Clear(); foreach (var plan in state.Plans) Plans.Add(plan);
        SelectedPlan = Plans.FirstOrDefault(p => p.Id == state.Active);
        PowerStatus = $"Activo: {SelectedPlan?.Name ?? "No disponible"} ({state.Active})\nAlimentación: {state.PowerSource}";
        var alternatives = state.Plans.Count(p => p.Id != state.Active);
        Recommendations = $"{alternatives} alternativa(s) de energía disponible(s). Recomendaciones automáticas: 0; la elección requiere evaluación técnica.";
        PropertyChanged?.Invoke(this, new(nameof(OptimizationAvailability)));
    }
    private async Task PreviewAsync()
    {
        var target = SelectedPlan?.Id ?? throw new InvalidOperationException("Seleccioná un plan.");
        preview = await Task.Run(() => optimization.Preview(target));
        PropertyChanged?.Invoke(this, new(nameof(OptimizationAvailability)));
        PreviewText = $"DRY RUN · No modifica Windows\nOriginal: {preview.Original}\nPropuesto / esperado: {preview.Target}\n{preview.Description}\nCompatibilidad: {preview.Compatibility.Reason}\nRiesgo: Moderado\nAdministrador: Sí · Reinicio: No\nReversión: {(preview.CanRollback ? "Disponible" : "No disponible")}";
    }
    private async Task ApplyAsync()
    {
        var plan = preview ?? throw new InvalidOperationException("Primero simulá la operación.");
        if (!Confirm(PreviewText + "\n\n¿Autorizar la operación y solicitar elevación UAC?")) return;
        try { await executor.ApplyAsync(plan); Status = "Plan aplicado y verificado por el ejecutor."; }
        finally { preview = null; await ReadHistoryAsync(); await RefreshAsync(); }
    }
    private async Task ReadHistoryAsync()
    {
        var records = await store.ReadAllAsync();
        History.Clear(); foreach (var record in records) History.Add(record);
        PropertyChanged?.Invoke(this, new(nameof(RecentActivity)));
        var incomplete = records.Count(r => r.Status is ChangeStatus.Prepared or ChangeStatus.RecoveryRequired);
        if (incomplete > 0) { Status = $"Hay {incomplete} operación(es) incompleta(s). Revisá Historial y solicitá recuperación."; Page = 3; }
    }
    private async Task RollbackAsync()
    {
        var record = SelectedChange ?? throw new InvalidOperationException("Seleccioná una operación.");
        if (!Confirm($"¿Restaurar el plan {record.Original} de la operación {record.Id}? Se pedirá elevación UAC.")) return;
        try { await executor.RollbackAsync(record.Id); Status = "Plan original restaurado y verificado."; }
        finally { await ReadHistoryAsync(); await RefreshAsync(); }
    }
    private async Task SaveSettingsAsync()
    {
        ValidateReportPath(); Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temp = SettingsPath + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new Settings(ReportDirectory)));
        File.Move(temp, SettingsPath, true); Status = "Configuración guardada.";
    }
    private void ValidateReportPath()
    {
        if (!Path.IsPathFullyQualified(ReportDirectory) || ReportDirectory.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(ReportDirectory)!).DriveType != DriveType.Fixed)
            throw new InvalidOperationException("Elegí una ruta absoluta en una unidad local fija para conservar los reportes.");
    }
    private async Task ExportAsync()
    {
        ValidateReportPath(); Directory.CreateDirectory(ReportDirectory);
        var file = Path.Combine(ReportDirectory, $"diagnostico-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Status = $"Reporte exportado: {file}";
    }
    private static bool Confirm(string text) => AppDialog.Confirm("Confirmar operación", text);
    private void PresentAnalysis() { PropertyChanged?.Invoke(this, new(nameof(AnalysisState))); PropertyChanged?.Invoke(this, new(nameof(AnalysisHeading))); }
    private AsyncCommand Command(Func<Task> action, Func<bool>? available = null)
    { var command = new AsyncCommand(() => RunAsync(action), () => !Busy && (available?.Invoke() ?? true)); commands.Add(command); return command; }
    private async Task RunAsync(Func<Task> action)
    {
        if (Busy) return;
        Busy = true;
        try { await action(); }
        catch (OperationCanceledException) { Status = "Operación cancelada."; }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { Status = "Elevación UAC cancelada."; }
        catch (Exception ex) { Status = ex is InvalidOperationException ? ex.Message : "No se pudo completar la operación: revisá permisos, proveedores de Windows y acceso al almacenamiento. No se supone un resultado exitoso."; }
        finally { Busy = false; }
    }
    private void NotifyCommands() { foreach (var command in commands) command.Notify(); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { field = value; PropertyChanged?.Invoke(this, new(name)); }
    private sealed record Settings(string ReportDirectory);
}
internal sealed class AsyncCommand(Func<Task> action, Func<bool> available) : ICommand
{
    public bool CanExecute(object? parameter) => available();
    public async void Execute(object? parameter) { if (CanExecute(parameter)) await action(); }
    public event EventHandler? CanExecuteChanged;
    public void Notify() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
