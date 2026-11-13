using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using JPCFIX.Optimizer.Core;

namespace JPCFIX.Optimizer.Application;

public sealed class SmartBackupService(IBackupEnvironment environment)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public BackupProfile Profile => environment.CurrentProfile;
    public IReadOnlyList<PersonalFolder> Detect() => environment.GetPersonalFolders();
    public Task<BackupManifest> AnalyzeAsync(IEnumerable<BackupCategory> selected, CancellationToken ct = default) => Task.Run(() =>
    {
        var manifest = new BackupManifest { Profile = Profile, Status = BackupStatus.Analyzing };
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mappedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in selected.OrderBy(c => BackupPaths.Full(c.SourcePath).Length))
        {
            ct.ThrowIfCancellationRequested();
            var source = BackupPaths.Full(input.SourcePath);
            if (manifest.Categories.Any(c => BackupPaths.Within(source, c.SourcePath))) continue;
            var identity = input.IsFile ? null : environment.GetLocationIdentity(source);
            if (identity != null && !identities.Add(identity)) continue;
            if (manifest.Categories.Count >= 10000) throw new InvalidOperationException("Demasiadas categorías. Dividí la selección en respaldos menores.");
            var category = new BackupCategory { Id = $"category-{manifest.Categories.Count + 1:D3}", Name = input.Name,
                SourcePath = source, KnownFolder = input.KnownFolder, IsFile = input.IsFile };
            manifest.Categories.Add(category);
            var pending = new Stack<string>(); pending.Push(source);
            void Add(string path)
            {
                ct.ThrowIfCancellationRequested();
                if (manifest.Files.Count >= 500000) throw new InvalidOperationException("Se alcanzó el límite de 500.000 entradas. Dividí la selección en respaldos menores.");
                var relative = category.IsFile ? Path.GetFileName(path) : Path.GetRelativePath(source, path).Replace('\\', '/');
                if (relative == ".") relative = "";
                var item = new BackupFile { CategoryId = category.Id, RelativePath = relative, IsDirectory = !category.IsFile && relative.Length == 0,
                    BackupPath = $"files/user-001/{category.Id}" + (relative.Length > 0 ? "/" + relative : "") };
                if (!mappedPaths.Add(item.BackupPath)) throw new InvalidOperationException("Existen nombres que solo difieren por mayúsculas. No se pueden mapear de forma portable; revisá esas ubicaciones antes de respaldar.");
                manifest.Files.Add(item);
                try
                {
                    var attributes = File.GetAttributes(path);
                    item.IsDirectory = (attributes & FileAttributes.Directory) != 0;
                    if (relative.Length > 0) BackupPaths.SafeRelative(relative);
                    if (environment.TechnicalExclusions.Where(p => !string.IsNullOrWhiteSpace(p)).Any(p => BackupPaths.Within(path, p)))
                    { item.Status = BackupItemStatus.Skipped; item.Error = "Ubicación técnica excluida; revisar si contiene información necesaria."; return; }
                    // OFFLINE, RECALL_ON_OPEN and RECALL_ON_DATA_ACCESS: never implicitly hydrate.
                    if (((int)attributes & (0x1000 | 0x40000 | 0x400000)) != 0)
                    { item.Status = BackupItemStatus.DownloadRequired; item.Error = "Requiere descarga autorizada fuera de esta entrega y nuevo análisis."; return; }
                    BackupPaths.RejectLinks(path);
                    item.Attributes = attributes;
                    item.LastWriteUtc = File.GetLastWriteTimeUtc(path);
                    if (!item.IsDirectory) { item.Length = new FileInfo(path).Length; return; }
                    // Incremental enumeration retains files already found if access fails midway.
                    foreach (var child in Directory.EnumerateFileSystemEntries(path))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (manifest.Files.Count + pending.Count >= 500000) throw new InvalidOperationException("Se alcanzó el límite de 500.000 entradas. Dividí la selección en respaldos menores.");
                        pending.Push(child);
                    }
                }
                catch (Exception ex) when (IsFileError(ex))
                { item.Status = BackupItemStatus.Failed; item.Error = Error(ex); }
            }
            while (pending.Count > 0) Add(pending.Pop());
        }
        if (manifest.Categories.Count == 0) throw new InvalidOperationException("Seleccioná al menos una ubicación.");
        manifest.Status = BackupStatus.Prepared;
        return manifest;
    }, ct);

    public DestinationAssessment ValidateDestination(BackupManifest manifest, string destination)
    {
        if (!Path.IsPathFullyQualified(destination) || !Directory.Exists(destination)) throw new IOException("Elegí un directorio existente.");
        BackupPaths.RejectLinks(destination);
        if (manifest.Categories.Any(c => BackupPaths.Within(destination, c.SourcePath) || BackupPaths.Within(c.SourcePath, destination)))
            throw new IOException("El destino no puede contener ni estar dentro de un origen seleccionado.");
        var assessment = environment.AssessDestination(destination, manifest.Categories.Select(c => c.SourcePath).ToArray());
        var required = manifest.Files.Where(f => !f.IsDirectory).Sum(f => f.Length);
        if (required > assessment.AvailableBytes - 16 * 1024 * 1024) throw new IOException("Espacio libre insuficiente (incluye margen para manifiesto y reportes).");
        if (assessment.FileSystem.Equals("FAT32", StringComparison.OrdinalIgnoreCase) && manifest.Files.Any(f => f.Length > uint.MaxValue))
            throw new IOException("FAT32 no admite un archivo seleccionado de 4 GiB o más.");
        var probe = Path.Combine(destination, ".jpcfix-write-" + Guid.NewGuid().ToString("N"));
        using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { stream.WriteByte(0); stream.Flush(true); }
        return assessment;
    }

    public async Task<string> CreateAsync(BackupManifest manifest, string destination, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        if (manifest.Status != BackupStatus.Prepared) throw new InvalidOperationException("Volvé a analizar antes de crear otro respaldo.");
        ValidateManifest(destination, manifest);
        var assessment = ValidateDestination(manifest, destination);
        manifest.Warnings.AddRange(assessment.Warnings);
        var root = BackupPaths.Join(destination, $"JPCFIX_Backup_{DateTime.Now:yyyy-MM-dd_HHmmss}_{manifest.Id:N}");
        Directory.CreateDirectory(root);
        using var operation = new FileStream(BackupPaths.Join(root, "operation.lock"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var clock = Stopwatch.StartNew();
        long bytes = 0; int processed = 0;
        var total = manifest.Files.Where(f => !f.IsDirectory).Sum(f => f.Length);
        try
        {
            manifest.Status = BackupStatus.Copying;
            await SaveAsync(root, manifest);
            foreach (var item in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                if (item.Status != BackupItemStatus.Pending) { processed++; continue; }
                var category = manifest.Categories.Single(c => c.Id == item.CategoryId);
                try
                {
                    var source = category.IsFile ? category.SourcePath : item.RelativePath.Length == 0 ? category.SourcePath : BackupPaths.Join(category.SourcePath, item.RelativePath);
                    BackupPaths.RejectLinks(source);
                    var target = BackupPaths.Join(root, item.BackupPath);
                    if (item.IsDirectory)
                    {
                        if (!Directory.Exists(source)) throw new IOException("El directorio de origen ya no está disponible. Volvé a analizar.");
                        Directory.CreateDirectory(target); item.Status = BackupItemStatus.Verified;
                    }
                    else
                    {
                        item.Sha256 = await CopyVerifiedAsync(source, target, item.Length, item.LastWriteUtc, null,
                            transferred => progress?.Report(new("Copiando / verificando", item.RelativePath, processed, manifest.Files.Count,
                                bytes + transferred, total, transferred, item.Length, (bytes + transferred) / Math.Max(clock.Elapsed.TotalSeconds, .001))), ct);
                        bytes += item.Length; item.Status = BackupItemStatus.Verified;
                        SetMetadata(target, item);
                    }
                }
                catch (Exception ex) when (IsFileError(ex)) { item.Status = BackupItemStatus.Failed; item.Error = Error(ex); }
                processed++;
                // Bounded checkpoints avoid rewriting a large manifest for every tiny file.
                // A crash may leave a published file Pending, which can never be mistaken for complete.
                if (processed % 25 == 0) await SaveAsync(root, manifest);
            }
            manifest.Status = BackupStatus.Verifying;
            await SaveAsync(root, manifest);
            await VerifyFilesAsync(root, manifest, progress, ct);
            manifest.Status = manifest.Files.All(f => f.Status == BackupItemStatus.Verified) ? BackupStatus.CompleteVerified : BackupStatus.Incomplete;
        }
        catch (OperationCanceledException) { manifest.Status = BackupStatus.Cancelled; }
        catch (Exception ex) when (IsFileError(ex)) { manifest.Status = BackupStatus.Failed; manifest.Warnings.Add(Error(ex)); }
        finally
        {
            manifest.Elapsed = clock.Elapsed;
            await SaveAsync(root, manifest);
            await WriteReportAsync(root, manifest);
        }
        return root;
    }

    public async Task<BackupManifest> OpenAndVerifyAsync(string root, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        BackupPaths.RejectLinks(root);
        var path = BackupPaths.Join(root, "manifest.json");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Manifiesto fuera del límite de 64 MiB.");
        BackupManifest manifest;
        try { manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, Json, ct) ?? throw new InvalidDataException("Manifiesto vacío."); }
        catch (JsonException) { throw new InvalidDataException("Manifiesto malformado."); }
        ValidateManifest(root, manifest);
        if (manifest.Status is BackupStatus.Copying or BackupStatus.Verifying or BackupStatus.Analyzing or BackupStatus.Prepared)
            manifest.Status = BackupStatus.Recoverable;
        var wasComplete = manifest.Status == BackupStatus.CompleteVerified;
        await VerifyFilesAsync(root, manifest, progress, ct);
        if (wasComplete && manifest.Files.Any(f => f.Status != BackupItemStatus.Verified)) manifest.Status = BackupStatus.Incomplete;
        return manifest;
    }

    public RestorePlan PreviewRestore(string root, BackupManifest manifest, IEnumerable<string> selectedCategories)
    {
        ValidateManifest(root, manifest);
        var folders = environment.GetPersonalFolders();
        var selected = selectedCategories.ToHashSet(StringComparer.Ordinal);
        var items = new List<RestoreItem>();
        foreach (var category in manifest.Categories.Where(c => selected.Contains(c.Id)))
        {
            // Never use the original absolute path as a destination. Extras have a dedicated safe container.
            var known = folders.FirstOrDefault(f => f.Key == category.KnownFolder && f.Key != "Extras");
            var targetRoot = known != null && Path.IsPathFullyQualified(known.Path) ? known.Path : Path.Combine(Profile.Path, "JPCFIX Recuperados", manifest.Id.ToString("N"), category.Id);
            BackupPaths.RejectLinks(targetRoot);
            if (BackupPaths.Within(root, targetRoot) || BackupPaths.Within(targetRoot, root)) throw new IOException("El respaldo y la restauración no pueden solaparse.");
            foreach (var file in manifest.Files.Where(f => f.CategoryId == category.Id && f.Status == BackupItemStatus.Verified))
            {
                var target = file.RelativePath.Length == 0 ? targetRoot : BackupPaths.Join(targetRoot, file.RelativePath);
                items.Add(new(file, target, File.Exists(target) || Directory.Exists(target)));
            }
        }
        return new(root, manifest, items, manifest.Files.Where(f => selected.Contains(f.CategoryId) && f.Status != BackupItemStatus.Verified)
            .Select(f => $"No recuperable: {f.BackupPath} · {f.Error ?? f.Status.ToString()}").ToArray());
    }

    public async Task<RestoreResult> RestoreAsync(RestorePlan preview, ConflictPolicy policy = ConflictPolicy.KeepBoth,
        bool replaceConfirmed = false, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(policy) || (policy == ConflictPolicy.Replace && !replaceConfirmed)) throw new InvalidOperationException("Reemplazar requiere confirmación expresa.");
        // Recompute authorized paths; never trust a forged/stale preview destination.
        var fresh = PreviewRestore(preview.BackupRoot, preview.Manifest, preview.Items.Select(i => i.File.CategoryId).Distinct());
        if (fresh.Items.Count != preview.Items.Count || fresh.Items.Zip(preview.Items).Any(p => p.First.Destination != p.Second.Destination || p.First.File.BackupPath != p.Second.File.BackupPath || p.First.Conflict != p.Second.Conflict))
            throw new InvalidOperationException("Cambió la previsualización; volvé a revisar las rutas.");
        int restored = 0, skipped = 0, failed = 0; bool cancelled = false;
        var errors = new List<string>(); long bytes = 0;
        var clock = Stopwatch.StartNew();
        try
        {
            foreach (var item in fresh.Items)
            {
                ct.ThrowIfCancellationRequested();
                var file = item.File; var target = item.Destination;
                try
                {
                    BackupPaths.RejectLinks(target);
                    if (file.IsDirectory)
                    {
                        if (!Directory.Exists(BackupPaths.Join(fresh.BackupRoot, file.BackupPath))) throw new IOException("El directorio respaldado ya no está disponible.");
                        Directory.CreateDirectory(target); continue;
                    }
                    if (Directory.Exists(target)) throw new IOException("Existe un directorio con el nombre del archivo.");
                    if (File.Exists(target))
                    {
                        if (policy == ConflictPolicy.Skip) { skipped++; continue; }
                        if (policy == ConflictPolicy.KeepBoth) target = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target) + " (JPCFIX " + Guid.NewGuid().ToString("N") + ")" + Path.GetExtension(target));
                    }
                    var source = BackupPaths.Join(fresh.BackupRoot, file.BackupPath);
                    await CopyVerifiedAsync(source, target, file.Length, null, file.Sha256, n => progress?.Report(new("Restaurando / verificando", file.RelativePath,
                        restored + skipped + failed, fresh.Items.Count, bytes + n, fresh.Items.Sum(i => i.File.Length), n, file.Length,
                        (bytes + n) / Math.Max(clock.Elapsed.TotalSeconds, .001))), ct, policy == ConflictPolicy.Replace && item.Conflict);
                    SetMetadata(target, file); restored++; bytes += file.Length;
                }
                catch (Exception ex) when (IsFileError(ex)) { failed++; errors.Add($"{file.BackupPath}: {Error(ex)}"); }
            }
        }
        catch (OperationCanceledException) { cancelled = true; }
        // Restore report belongs to the current user; a portable backup can be read-only.
        return new(restored, skipped, failed, cancelled, errors);
    }

    private static void ValidateManifest(string root, BackupManifest m)
    {
        if (m.FormatVersion != 1 || m.Id == Guid.Empty || !Enum.IsDefined(m.Status) || m.Profile == null ||
            m.Categories == null || m.Files == null || m.Warnings == null || m.Categories.Count is 0 or > 10000 || m.Files.Count > 500000)
            throw new InvalidDataException("Versión o esquema de manifiesto no compatible.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(["Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos", "Favorites", "SavedGames", "Extras"], StringComparer.Ordinal);
        foreach (var c in m.Categories)
            if (c == null || c.Id == null || !ids.Add(c.Id) || !known.Contains(c.KnownFolder) || BackupPaths.SafeRelative(c.Id).Contains('/'))
                throw new InvalidDataException("Categoría inválida o duplicada.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalLength = 0;
        foreach (var f in m.Files)
        {
            if (f == null || !ids.Contains(f.CategoryId) || f.RelativePath == null || f.BackupPath == null || f.Length < 0 || !Enum.IsDefined(f.Status))
                throw new InvalidDataException("Entrada de archivo inválida.");
            if (f.Length > long.MaxValue - totalLength) throw new InvalidDataException("Tamaño total fuera de límites.");
            totalLength += f.Length;
            if (f.RelativePath.Length > 0) BackupPaths.SafeRelative(f.RelativePath);
            else if (!f.IsDirectory) throw new InvalidDataException("Archivo sin nombre.");
            var expected = $"files/user-001/{f.CategoryId}" + (f.RelativePath.Length > 0 ? "/" + f.RelativePath : "");
            if (f.BackupPath != expected || !paths.Add(f.BackupPath)) throw new InvalidDataException("Mapeo inválido o duplicado.");
            BackupPaths.SafeRelative(f.BackupPath);
            if (f.Status == BackupItemStatus.Verified && !f.IsDirectory && (f.Sha256 == null || f.Sha256.Length != 64 || !f.Sha256.All(Uri.IsHexDigit)))
                throw new InvalidDataException("Hash SHA-256 inválido.");
        }
    }

    private static async Task VerifyFilesAsync(string root, BackupManifest manifest, IProgress<BackupProgress>? progress, CancellationToken ct)
    {
        int count = 0;
        foreach (var item in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (item.Status == BackupItemStatus.Verified)
            {
                try
                {
                    var path = BackupPaths.Join(root, item.BackupPath);
                    if (item.IsDirectory) { if (!Directory.Exists(path)) throw new IOException("Falta el directorio respaldado."); }
                    else
                    {
                        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
                        if (stream.Length != item.Length || !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)), item.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Contenido ausente o corrupto: hash/tamaño diferente.");
                    }
                }
                catch (Exception ex) when (IsFileError(ex)) { item.Status = BackupItemStatus.Failed; item.Error = Error(ex); }
            }
            progress?.Report(new("Verificando integridad", item.RelativePath, ++count, manifest.Files.Count, 0, 0, 0, 0, 0));
        }
    }

    private static async Task<string> CopyVerifiedAsync(string source, string target, long expectedLength, DateTime? expectedTime,
        string? expectedHash, Action<long> progress, CancellationToken ct, bool replace = false)
    {
        BackupPaths.RejectLinks(source); BackupPaths.RejectLinks(target);
        var attributes = File.GetAttributes(source);
        if (((int)attributes & (0x1000 | 0x40000 | 0x400000)) != 0) throw new IOException("El archivo requiere descarga; no se autoriza hidratación implícita.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = Path.Combine(Path.GetDirectoryName(target)!, ".jpcfix-" + Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
            var before = File.GetLastWriteTimeUtc(source);
            if (input.Length != expectedLength || (expectedTime.HasValue && before != expectedTime.Value)) throw new BackupFileIssue("El origen cambió desde el análisis. Reintentá con un nuevo análisis.");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long transferred = 0; var buffer = new byte[128 * 1024];
            var lastProgress = Stopwatch.StartNew(); bool first = true;
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, true))
            {
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) != 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct); hash.AppendData(buffer, 0, read); transferred += read;
                    if (first || lastProgress.ElapsedMilliseconds >= 100) { progress(transferred); first = false; lastProgress.Restart(); }
                }
                await output.FlushAsync(ct); output.Flush(true);
            }
            var digest = Convert.ToHexString(hash.GetHashAndReset());
            if (transferred != expectedLength || (expectedHash != null && !string.Equals(digest, expectedHash, StringComparison.OrdinalIgnoreCase))) throw new BackupFileIssue("Integridad no comprobada: el hash/tamaño del origen no coincide.");
            input.Position = 0;
            if (Convert.ToHexString(await SHA256.HashDataAsync(input, ct)) != digest || File.GetLastWriteTimeUtc(source) != before) throw new BackupFileIssue("El origen cambió durante la copia. Cerrá la aplicación de origen y repetí el análisis.");
            await using (var check = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, true))
                if (Convert.ToHexString(await SHA256.HashDataAsync(check, ct)) != digest) throw new BackupFileIssue("Integridad no comprobada: falló la verificación del archivo temporal escrito.");
            ct.ThrowIfCancellationRequested();
            BackupPaths.RejectLinks(target);
            File.Move(temp, target, replace);
            progress(transferred);
            return digest;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void SetMetadata(string path, BackupFile item)
    {
        File.SetLastWriteTimeUtc(path, item.LastWriteUtc);
        // No ACLs, EFS, compression flags, cloud or reparse attributes from the old installation.
        File.SetAttributes(path, (item.Attributes & (FileAttributes.Archive | FileAttributes.Hidden | FileAttributes.ReadOnly)) is var a && a != 0 ? a : FileAttributes.Normal);
    }
    private sealed class BackupFileIssue(string message) : IOException(message);
    private static bool IsFileError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException;
    private static string Error(Exception ex) => ex switch
    {
        BackupFileIssue => ex.Message, // Only fixed, application-owned messages; never arbitrary exception payloads.
        UnauthorizedAccessException => "Acceso denegado. Revisá permisos sin elevar automáticamente.",
        IOException when (ex.HResult & 0xFFFF) is 32 or 33 => "Archivo bloqueado. Cerrá la aplicación de origen y reintentá.",
        IOException when (ex.HResult & 0xFFFF) is 112 or 39 => "Espacio insuficiente durante la escritura. Conservá el origen y elegí otro destino.",
        FileNotFoundException or DirectoryNotFoundException => "Archivo o directorio no disponible. Volvé a analizar antes de reintentar.",
        InvalidDataException => "Ruta, nombre o contenido de manifiesto inválido. Se requiere revisión manual.",
        IOException => "Error de E/S: archivo bloqueado, modificado, inaccesible, vínculo, falta de espacio o integridad no comprobada. Revisar y reintentar.",
        _ => "Ruta o atributos no compatibles. Revisión manual necesaria."
    };
    private static async Task SaveAsync(string root, BackupManifest manifest)
    {
        var path = BackupPaths.Join(root, "manifest.json");
        var temp = BackupPaths.Join(root, "manifest-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, Json);
                if (stream.Length > 64 * 1024 * 1024) throw new IOException("El manifiesto supera 64 MiB. Dividí la selección en respaldos menores; no se considera completa.");
                await stream.FlushAsync(); stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static async Task WriteReportAsync(string root, BackupManifest m)
    {
        var directory = BackupPaths.Join(root, "reports"); Directory.CreateDirectory(directory);
        var text = $"JPCFIX Smart Backup · {m.Status}\nArchivos previstos: {m.Files.Count(f => !f.IsDirectory)}\nVerificados: {m.Files.Count(f => !f.IsDirectory && f.Status == BackupItemStatus.Verified)}\nFallidos: {m.Files.Count(f => f.Status == BackupItemStatus.Failed)}\nOmitidos: {m.Files.Count(f => f.Status == BackupItemStatus.Skipped)}\nPendientes de descarga: {m.Files.Count(f => f.Status == BackupItemStatus.DownloadRequired)}\nPendientes: {m.Files.Count(f => f.Status == BackupItemStatus.Pending)}\nBytes previstos: {m.Files.Sum(f => f.Length)}\nTiempo: {m.Elapsed}\n";
        text += string.Join("\n", m.Warnings.Concat(m.Files.Where(f => f.Error != null).Select(f => $"{f.BackupPath}: {f.Error}")));
        await File.WriteAllTextAsync(BackupPaths.Join(directory, "backup-report.txt"), text);
    }
}
