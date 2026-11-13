using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JPCFIX.Optimizer.Application;
using JPCFIX.Optimizer.Core;
using Microsoft.Win32;

namespace JPCFIX.Optimizer.Desktop;

public sealed class BackupViewModel : INotifyPropertyChanged
{
    private readonly SmartBackupService service;
    private readonly List<AsyncCommand> commands = [];
    private CancellationTokenSource? cancellation;
    private BackupManifest? analyzed, opened;
    private RestorePlan? preview;
    private string destination = "", backupRoot = "", status = "Elegí una operación. Solo se incluye el usuario que ejecuta la aplicación.", details = "", progressText = "", resultPath = "";
    private bool busy;
    private int workflow;
    private double percent, filePercent;
    private ConflictPolicy conflictPolicy;
    public BackupViewModel(SmartBackupService service)
    {
        this.service = service;
        DetectCommand = Command(DetectAsync);
        AnalyzeCommand = Command(AnalyzeAsync, () => Sources.Any(r => r.Included));
        RecommendedCommand = Command(() => { foreach (var row in Sources) row.Included = row.Recommended; return Task.CompletedTask; });
        AddFolderCommand = Command(() => { var dialog = new OpenFolderDialog { Title = "Agregar carpeta personal" }; if (dialog.ShowDialog() == true) AddSource(dialog.FolderName, false); return Task.CompletedTask; });
        AddFileCommand = Command(() => { var dialog = new OpenFileDialog { Title = "Agregar archivos personales", Multiselect = true }; if (dialog.ShowDialog() == true) foreach (var file in dialog.FileNames) AddSource(file, true); return Task.CompletedTask; });
        DestinationCommand = Command(() => { var dialog = new OpenFolderDialog { Title = "Destino del respaldo: elegí un medio que conservarás" }; if (dialog.ShowDialog() == true) { Destination = dialog.FolderName; Details = "Destino cambiado. Se validará antes de copiar."; } return Task.CompletedTask; });
        CreateCommand = Command(CreateAsync, () => analyzed != null && Destination.Length > 0);
        OpenCommand = Command(OpenAsync);
        PreviewCommand = Command(PreviewAsync, () => opened != null && Recoverable.Any(r => r.Included));
        RestoreCommand = Command(RestoreAsync, () => preview is { Items.Count: > 0 });
        CancelCommand = new AsyncCommand(() => { cancellation?.Cancel(); return Task.CompletedTask; }, () => Busy);
        NewCommand = Command(() => { Workflow = 0; return DetectAsync(); });
        ExistingCommand = Command(() => { Workflow = 1; return Task.CompletedTask; });
    }
    public ObservableCollection<BackupSelection> Sources { get; } = [];
    public ObservableCollection<BackupSelection> Recoverable { get; } = [];
    public ObservableCollection<RestoreItem> RestoreItems { get; } = [];
    public BackupProfile[] Profiles => [service.Profile];
    public string ProfileText => $"Usuario autorizado: {service.Profile.Name} · {service.Profile.Path}\nPara otro usuario, iniciá sesión con su cuenta y abrí la aplicación sin elevación.";
    public KeyValuePair<ConflictPolicy, string>[] Policies => [new(ConflictPolicy.KeepBoth, "Conservar ambos (recomendado)"),
        new(ConflictPolicy.Skip, "Omitir archivos existentes"), new(ConflictPolicy.Replace, "Reemplazar con confirmación")];
    public ConflictPolicy SelectedPolicy { get => conflictPolicy; set { conflictPolicy = value; Changed(); } }
    public ICommand DetectCommand { get; }
    public ICommand AnalyzeCommand { get; }
    public ICommand RecommendedCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand AddFileCommand { get; }
    public ICommand DestinationCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand ExistingCommand { get; }
    public int Workflow { get => workflow; set { workflow = value; Changed(); } }
    public bool Busy { get => busy; private set { busy = value; Changed(); Changed(nameof(Ready)); Notify(); ((AsyncCommand)CancelCommand).Notify(); } }
    public bool Ready => !Busy;
    public string Destination { get => destination; private set { destination = value; Changed(); Notify(); } }
    public string BackupRoot { get => backupRoot; private set { backupRoot = value; Changed(); } }
    public string ResultPath { get => resultPath; private set { resultPath = value; Changed(); } }
    public string Status { get => status; private set { status = value; Changed(); } }
    public string Details { get => details; private set { details = value; Changed(); } }
    public string ProgressText { get => progressText; private set { progressText = value; Changed(); } }
    public double Percent { get => percent; private set { percent = value; Changed(); } }
    public double FilePercent { get => filePercent; private set { filePercent = value; Changed(); } }

    private async Task DetectAsync()
    {
        analyzed = null; Sources.Clear();
        foreach (var folder in await Task.Run(service.Detect))
        {
            var row = new BackupSelection { Id = folder.Key, Name = folder.Name, Path = folder.Path, KnownFolder = folder.Key,
                Recommended = folder.Recommended, Included = folder.Recommended && folder.Path.Length > 0,
                State = folder.Path.Length == 0 ? "No se pudo resolver la ubicación" : "Pendiente de análisis" };
            Watch(row, false); Sources.Add(row);
        }
        Status = "Paso 1 · Seleccioná categorías, agregá ubicaciones autorizadas y analizá. No se exploran otras unidades automáticamente.";
        Details = "No se garantiza detectar todos los archivos personales. AppData, navegadores, aplicaciones y preferencias pertenecen a entregas posteriores.";
    }
    private void AddSource(string path, bool isFile)
    {
        var row = new BackupSelection { Id = Guid.NewGuid().ToString("N"), Name = Path.GetFileName(path), Path = path, Included = true, IsFile = isFile, State = "Pendiente de análisis" };
        Watch(row, false); Sources.Add(row); analyzed = null; Notify();
    }
    private void Watch(BackupSelection row, bool restore) => row.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName != nameof(BackupSelection.Included)) return;
        if (restore) { preview = null; RestoreItems.Clear(); }
        else analyzed = null;
        Notify();
    };
    private async Task AnalyzeAsync()
    {
        analyzed = null;
        Status = "Paso 2 · Analizando archivos y accesibilidad…";
        analyzed = await service.AnalyzeAsync(Sources.Where(r => r.Included).Select(r => new BackupCategory { Name = r.Name, SourcePath = r.Path, KnownFolder = r.KnownFolder, IsFile = r.IsFile }), cancellation!.Token);
        foreach (var row in Sources)
        {
            var c = analyzed.Categories.FirstOrDefault(c => string.Equals(c.SourcePath, BackupPaths.Full(row.Path.Length == 0 ? "." : row.Path), StringComparison.OrdinalIgnoreCase));
            if (c == null) { row.State = row.Included ? "Ubicación ya incluida por otra categoría" : "No seleccionada"; continue; }
            var files = analyzed.Files.Where(f => f.CategoryId == c.Id).ToArray();
            row.Bytes = files.Sum(f => f.Length); row.Files = files.Count(f => !f.IsDirectory);
            row.Warning = string.Join("\n", files.Where(f => f.Error != null).Select(f => $"{f.RelativePath}: {f.Error}"));
            row.State = files.Any(f => f.Status != BackupItemStatus.Pending) ? "Requiere revisión: respaldo incompleto si continúa" : "Accesible al analizar";
        }
        Details = $"{analyzed.Files.Count(f => !f.IsDirectory)} archivo(s) · {analyzed.Files.Sum(f => f.Length):N0} bytes previstos. Revisá advertencias antes de continuar.";
        Status = "Paso 3 · Elegí el destino y revisá la confirmación de seguridad.";
    }
    private async Task CreateAsync()
    {
        var manifest = analyzed!;
        Status = "Validando destino, espacio y separación de discos…";
        var assessment = await Task.Run(() => service.ValidateDestination(manifest, Destination));
        var warning = string.Join("\n\n", assessment.Warnings);
        Details = $"Sistema de archivos: {assessment.FileSystem} · Libres: {assessment.AvailableBytes:N0} bytes\n{warning}";
        if (!BackupDialog.Confirm("Confirmar respaldo", $"Usuario: {service.Profile.Name}\nDestino: {Destination}\nArchivos: {manifest.Files.Count(f => !f.IsDirectory)}\nBytes: {manifest.Files.Sum(f => f.Length):N0}\n\n{Details}\n\n¿Autorizás copiar estos datos y confirmás que conservarás el medio de destino?")) return;
        Status = "Paso 4 · Copiando y verificando. Conservá el equipo y el destino conectados.";
        var reporting = Progress();
        ResultPath = await Task.Run(() => service.CreateAsync(manifest, Destination, reporting, cancellation!.Token));
        analyzed = null;
        Status = manifest.Status == BackupStatus.CompleteVerified ? "Completo y verificado. Comprobá el respaldo desde otro equipo antes de formatear." : $"RESPALDO NO COMPLETO · {StatusText(manifest.Status)}. Revisá el informe y conservá los originales.";
        Details = $"Informe: {Path.Combine(ResultPath, "reports", "backup-report.txt")}\nVerificados: {manifest.Files.Count(f => !f.IsDirectory && f.Status == BackupItemStatus.Verified)} · Errores/pendientes: {manifest.Files.Count(f => f.Status != BackupItemStatus.Verified)}";
    }
    private async Task OpenAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Seleccioná la carpeta que contiene manifest.json" };
        if (dialog.ShowDialog() != true) return;
        opened = null; preview = null; Recoverable.Clear(); RestoreItems.Clear(); BackupRoot = dialog.FolderName;
        Status = "Paso 1 · Validando manifiesto, rutas y hashes…";
        var reporting = Progress();
        opened = await Task.Run(() => service.OpenAndVerifyAsync(BackupRoot, reporting, cancellation!.Token));
        foreach (var c in opened.Categories)
        {
            var files = opened.Files.Where(f => f.CategoryId == c.Id).ToArray();
            var row = new BackupSelection { Id = c.Id, Name = c.Name, Path = c.SourcePath, Included = files.Any(f => f.Status == BackupItemStatus.Verified),
                Bytes = files.Where(f => f.Status == BackupItemStatus.Verified).Sum(f => f.Length), Files = files.Count(f => !f.IsDirectory && f.Status == BackupItemStatus.Verified),
                State = files.All(f => f.Status == BackupItemStatus.Verified) ? "Verificada" : "Recuperación parcial",
                Warning = string.Join("\n", files.Where(f => f.Status != BackupItemStatus.Verified).Select(f => $"{f.RelativePath}: {f.Error ?? f.Status.ToString()}")) };
            Watch(row, true); Recoverable.Add(row);
        }
        Status = $"Paso 2 · Estado del respaldo: {StatusText(opened.Status)}. Seleccioná categorías y previsualizá.";
        Details = "Se restaurará al usuario actual. Extras se guardan en «JPCFIX Recuperados». Los archivos pendientes o corruptos no se restauran. Un hash comprueba integridad, no autenticidad del propietario.";
    }
    private Task PreviewAsync()
    {
        preview = service.PreviewRestore(BackupRoot, opened!, Recoverable.Where(r => r.Included).Select(r => r.Id));
        RestoreItems.Clear(); foreach (var item in preview.Items) RestoreItems.Add(item);
        Details = string.Join("\n", preview.Warnings);
        Status = $"Paso 3 · Revisá {preview.Items.Count} ruta(s) y {preview.Items.Count(i => i.Conflict)} conflicto(s). La opción predeterminada conserva ambos archivos.";
        return Task.CompletedTask;
    }
    private async Task RestoreAsync()
    {
        var plan = preview!;
        if (!BackupDialog.Confirm("Confirmar restauración", $"Usuario destino: {service.Profile.Name}\nPerfil: {service.Profile.Path}\nRutas: {plan.Items.Count}\nConflictos: {plan.Items.Count(i => i.Conflict)}\nPolítica: {Policies.First(p => p.Key == SelectedPolicy).Value}\n\n{(SelectedPolicy == ConflictPolicy.Replace ? "ATENCIÓN: se reemplazarán archivos existentes. Esta acción puede perder la versión actual." : "Se conservarán los archivos existentes según la política seleccionada.")}\n\nRevisaste la previsualización y ¿autorizás restaurar?")) return;
        Status = "Paso 4 · Restaurando y comprobando cada archivo…";
        var reporting = Progress();
        var policy = SelectedPolicy;
        var result = await Task.Run(() => service.RestoreAsync(plan, policy, policy == ConflictPolicy.Replace, reporting, cancellation!.Token));
        preview = null;
        Status = result.Cancelled || result.Failed > 0 || result.Skipped > 0 || plan.Warnings.Count > 0 ? "Restauración parcial o cancelada. Revisá el informe." : "Restauración de la selección completada y verificada.";
        Details = $"Restaurados: {result.Restored} · Omitidos: {result.Skipped} · Fallidos: {result.Failed}\n" + string.Join("\n", result.Errors);
        var reports = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JPCFIX.Optimizer", "BackupReports");
        BackupPaths.RejectLinks(reports); Directory.CreateDirectory(reports);
        ResultPath = BackupPaths.Join(reports, "restore-" + Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(ResultPath, JsonSerializer.Serialize(new { BackupId = plan.Manifest.Id, At = DateTimeOffset.UtcNow, Result = result, Warnings = plan.Warnings }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private IProgress<BackupProgress> Progress() => new Progress<BackupProgress>(p =>
    {
        Percent = p.Total == 0 ? 0 : 100d * p.Processed / p.Total;
        FilePercent = p.FileLength == 0 ? 0 : 100d * p.FileBytes / p.FileLength;
        ProgressText = $"{p.Phase} · {p.File}\n{p.Processed}/{p.Total} elementos · {p.Bytes:N0}/{p.TotalBytes:N0} bytes · {p.BytesPerSecond / 1048576:F1} MiB/s";
    });
    private AsyncCommand Command(Func<Task> action, Func<bool>? enabled = null)
    {
        var command = new AsyncCommand(async () =>
        {
            Busy = true; cancellation = new(); Percent = FilePercent = 0; ProgressText = "";
            try { await action(); }
            catch (OperationCanceledException) { Status = "Cancelado. No se anuncia una operación completa."; }
            catch (Exception ex)
            {
                if (analyzed?.Status != BackupStatus.Prepared) analyzed = null;
                Status = "No se pudo completar la operación. No borres ni formatees los originales.";
                Details = ex is InvalidOperationException or IOException or InvalidDataException ? ex.Message : "Revisá acceso, permisos, espacio libre y compatibilidad de rutas. No se elevan privilegios automáticamente.";
            }
            finally { cancellation.Dispose(); cancellation = null; Busy = false; }
        }, () => !Busy && (enabled?.Invoke() ?? true));
        commands.Add(command); return command;
    }
    private static string StatusText(BackupStatus state) => state switch
    {
        BackupStatus.CompleteVerified => "Completo y verificado", BackupStatus.Cancelled => "Cancelado", BackupStatus.Recoverable => "Interrumpido / recuperable parcialmente",
        BackupStatus.Incomplete => "Incompleto", BackupStatus.Failed => "Fallido", _ => "Pendiente / sin finalizar"
    };
    private void Notify() { foreach (var command in commands) command.Notify(); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class BackupSelection : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string KnownFolder { get; init; } = "Extras";
    public bool IsFile { get; init; }
    public bool Recommended { get; init; }
    private bool included;
    private long bytes;
    private int files;
    private string state = "", warning = "";
    public bool Included { get => included; set { included = value; Changed(); } }
    public long Bytes { get => bytes; set { bytes = value; Changed(); } }
    public int Files { get => files; set { files = value; Changed(); } }
    public string State { get => state; set { state = value; Changed(); } }
    public string Warning { get => warning; set { warning = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

internal static class BackupDialog
{
    public static bool Confirm(string title, string text) => AppDialog.Confirm(title, text);
}
