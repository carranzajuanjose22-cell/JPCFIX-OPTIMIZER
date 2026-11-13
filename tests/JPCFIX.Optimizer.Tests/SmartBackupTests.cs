using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using JPCFIX.Optimizer.Application;
using JPCFIX.Optimizer.Core;
using Xunit;

namespace JPCFIX.Optimizer.Tests;

public sealed class SmartBackupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "JPCFIX-backup-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeEnvironment env;
    private readonly SmartBackupService service;
    private readonly List<string> junctions = [];
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private string Source => Path.Combine(root, "old-user", "redirected-documents");
    private string Destination => Path.Combine(root, "external");
    private string NewDocuments => Path.Combine(root, "different-user", "current-documents");
    public SmartBackupTests()
    {
        Directory.CreateDirectory(Source); Directory.CreateDirectory(Destination); Directory.CreateDirectory(NewDocuments);
        env = new FakeEnvironment { CurrentProfile = new("old-sid", "old-user", Path.Combine(root, "old-user")),
            Folders = [new("Documents", "Documentos", Source)] };
        service = new(env);
    }
    private async Task Write(string name = "documento.extraño", string content = "Información personal")
    { var path = Path.Combine(Source, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllTextAsync(path, content); }
    private Task<BackupManifest> Analyze() => service.AnalyzeAsync([new() { Name = "Documentos", KnownFolder = "Documents", SourcePath = Source }]);
    private async Task<(string Root, BackupManifest Manifest)> Backup()
    { var manifest = await Analyze(); return (await service.CreateAsync(manifest, Destination), manifest); }
    private void SwitchUser()
    { env.CurrentProfile = new("new-sid", "different-user", Path.Combine(root, "different-user")); env.Folders = [new("Documents", "Documentos", NewDocuments)]; }
    private static Task SaveManifest(string path, BackupManifest manifest) => File.WriteAllTextAsync(Path.Combine(path, "manifest.json"), JsonSerializer.Serialize(manifest, Json));

    [Fact] public void UsesInjectedRedirectedKnownFolders() => Assert.Equal(Source, Assert.Single(service.Detect()).Path);

    [Fact] public void NativeKnownFoldersAndPhysicalIdentityAreReadOnlyAndResolvable()
    {
        var native = new JPCFIX.Optimizer.Infrastructure.WindowsBackupEnvironment();
        var folders = native.GetPersonalFolders();
        Assert.Equal(8, folders.Count); Assert.Contains(folders, f => f.Key == "Downloads" && Path.IsPathFullyQualified(f.Path));
        Assert.False(string.IsNullOrEmpty(native.CurrentProfile.Id));
        Assert.NotNull(native.GetLocationIdentity(Source));
        Assert.Equal(native.GetLocationIdentity(Source), native.GetLocationIdentity(Source + "\\"));
        Assert.NotEqual(native.GetLocationIdentity(Source), native.GetLocationIdentity(Destination));
    }

    [Fact] public async Task DeduplicatesDirectoryAliasesByPhysicalIdentity()
    {
        var alias = Path.Combine(root, "alias"); Directory.CreateDirectory(alias);
        env.Identities[Source] = "same-volume:same-file-id"; env.Identities[alias] = "same-volume:same-file-id";
        var m = await service.AnalyzeAsync([new() { SourcePath = Source }, new() { SourcePath = alias }]);
        Assert.Single(m.Categories);
    }

    [Theory] [InlineData(".zip")] [InlineData(".exe")] [InlineData(".iso")] [InlineData(".pst")] [InlineData(".sqlite")] [InlineData(".tmp")] [InlineData(".sinextensionrara")]
    public async Task PreservesPersonalFilesRegardlessOfExtension(string extension)
    {
        await Write("archivo" + extension);
        var (backup, m) = await Backup();
        var f = Assert.Single(m.Files, f => !f.IsDirectory);
        Assert.Equal(BackupStatus.CompleteVerified, m.Status); Assert.Equal(BackupItemStatus.Verified, f.Status);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(Source, "archivo" + extension)))), f.Sha256);
        Assert.Equal(await File.ReadAllTextAsync(Path.Combine(Source, "archivo" + extension)), await File.ReadAllTextAsync(BackupPaths.Join(backup, f.BackupPath)));
    }

    [Fact] public async Task DeduplicatesIdenticalAndNestedSelectedSources()
    {
        await Write("nested/personal.txt");
        var m = await service.AnalyzeAsync([new() { SourcePath = Path.Combine(Source, "nested") }, new() { SourcePath = Source }, new() { SourcePath = Source + "\\" }]);
        Assert.Single(m.Categories); Assert.Single(m.Files, f => !f.IsDirectory);
    }

    [Fact] public async Task OnlyExcludesConfiguredTechnicalLocations()
    {
        await Write("cache/cached.bin"); await Write("personal.tmp"); await Write("other/cache/precious.zip");
        env.Exclusions = [Path.Combine(Source, "cache")];
        var (_, m) = await Backup();
        Assert.Equal(BackupStatus.Incomplete, m.Status);
        Assert.Equal(2, m.Files.Count(f => !f.IsDirectory && f.Status == BackupItemStatus.Verified));
        Assert.Contains(m.Files, f => f.RelativePath == "cache" && f.Status == BackupItemStatus.Skipped);
        Assert.True(File.Exists(Path.Combine(Source, "cache", "cached.bin")));
    }

    [Fact] public async Task KeepsEmptyDirectoriesAndNestedStructure()
    {
        Directory.CreateDirectory(Path.Combine(Source, "vacía")); await Write("uno/dos/archivo", "dato");
        var (backup, m) = await Backup(); SwitchUser();
        var result = await service.RestoreAsync(service.PreviewRestore(backup, m, m.Categories.Select(c => c.Id)));
        Assert.Equal(1, result.Restored); Assert.True(Directory.Exists(Path.Combine(NewDocuments, "vacía")));
        Assert.Equal("dato", await File.ReadAllTextAsync(Path.Combine(NewDocuments, "uno", "dos", "archivo")));
    }

    [Fact] public async Task ManualSingleFileGoesToDedicatedExtrasContainer()
    {
        await Write("uno.zip", "zip");
        var m = await service.AnalyzeAsync([new() { SourcePath = Path.Combine(Source, "uno.zip"), IsFile = true, Name = "Archivo", KnownFolder = "Extras" }]);
        var backup = await service.CreateAsync(m, Destination); SwitchUser();
        var plan = service.PreviewRestore(backup, m, m.Categories.Select(c => c.Id));
        Assert.All(plan.Items, i => Assert.True(BackupPaths.Within(i.Destination, Path.Combine(env.CurrentProfile.Path, "JPCFIX Recuperados")))) ;
        Assert.Equal(1, (await service.RestoreAsync(plan)).Restored);
    }

    [Fact] public async Task RejectsRecursiveAndOverlappingDestinations()
    {
        var m = await Analyze(); var child = Path.Combine(Source, "backup"); Directory.CreateDirectory(child);
        Assert.Throws<IOException>(() => service.ValidateDestination(m, child));
        Assert.Throws<IOException>(() => service.ValidateDestination(m, Path.GetDirectoryName(Source)!));
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact] public async Task InsufficientSpaceIsRejectedBeforeCreatingBackup()
    {
        await Write(); env.Available = 0;
        await Assert.ThrowsAsync<IOException>(async () => await service.CreateAsync(await Analyze(), Destination));
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact] public async Task DestinationPermissionFailureCreatesNothing()
    {
        env.AssessmentError = new UnauthorizedAccessException();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await service.CreateAsync(await Analyze(), Destination));
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact] public async Task RejectsFat32LargeFileWithoutAllocatingIt()
    {
        var m = await Analyze(); m.Files.Add(new() { Length = (long)uint.MaxValue + 1 }); env.FileSystem = "FAT32";
        Assert.Throws<IOException>(() => service.ValidateDestination(m, Destination));
    }

    [Fact] public async Task MissingSourceNeverLooksCompleteAndManifestCanBeOpened()
    {
        var missing = Path.Combine(root, "missing");
        var m = await service.AnalyzeAsync([new() { SourcePath = missing }]);
        var backup = await service.CreateAsync(m, Destination);
        Assert.Equal(BackupStatus.Incomplete, m.Status);
        Assert.Equal(BackupStatus.Incomplete, (await service.OpenAndVerifyAsync(backup)).Status);
    }

    [Fact] public async Task LockedFileIsReportedIndividuallyOtherFilesSurvive()
    {
        await Write("locked.txt"); await Write("ok.txt"); var m = await Analyze();
        using var held = new FileStream(Path.Combine(Source, "locked.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await service.CreateAsync(m, Destination);
        Assert.Equal(BackupStatus.Incomplete, m.Status);
        Assert.Equal(BackupItemStatus.Failed, m.Files.Single(f => f.RelativePath == "locked.txt").Status);
        Assert.Equal(BackupItemStatus.Verified, m.Files.Single(f => f.RelativePath == "ok.txt").Status);
    }

    [Fact] public async Task SourcePermissionDenialIsReportedWithoutElevation()
    {
        await Write(); var file = new FileInfo(Path.Combine(Source, "documento.extraño"));
        var original = file.GetAccessControl();
        var restricted = file.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        restricted.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadData, AccessControlType.Deny));
        try
        {
            file.SetAccessControl(restricted);
            var (_, m) = await Backup();
            Assert.Equal(BackupStatus.Incomplete, m.Status);
            Assert.Contains(m.Files, f => f.Status == BackupItemStatus.Failed && f.Error!.Contains("denegado"));
        }
        finally { file.SetAccessControl(original); }
    }

    [Fact] public async Task ChangedSinceAnalysisRequiresNewAnalysis()
    {
        await Write(); var m = await Analyze(); await Write(content: "contenido nuevo y distinto");
        await service.CreateAsync(m, Destination);
        Assert.Equal(BackupStatus.Incomplete, m.Status); Assert.Contains(m.Files, f => f.Status == BackupItemStatus.Failed && f.Error!.Contains("cambió"));
    }

    [Fact] public async Task MissingDirectoryAfterAnalysisCannotBeMarkedComplete()
    {
        var m = await Analyze(); Directory.Delete(Source);
        await service.CreateAsync(m, Destination);
        Assert.Equal(BackupStatus.Incomplete, m.Status); Assert.Contains(m.Files, f => f.Status == BackupItemStatus.Failed);
    }

    [Fact] public async Task OfflineMarkerIsNeverCopiedAsPersonalData()
    {
        await Write("offline.dat"); var path = Path.Combine(Source, "offline.dat");
        File.SetAttributes(path, FileAttributes.Offline);
        try
        {
            var (backup, m) = await Backup(); var f = Assert.Single(m.Files, f => !f.IsDirectory);
            Assert.Equal(BackupStatus.Incomplete, m.Status); Assert.Equal(BackupItemStatus.DownloadRequired, f.Status);
            Assert.Null(f.Sha256); Assert.False(File.Exists(BackupPaths.Join(backup, f.BackupPath)));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    [Fact] public async Task WriterCannotModifySourceDuringCopy()
    {
        await Write("big.bin", new string('x', 300000)); bool checkedLock = false;
        var m = await Analyze();
        await service.CreateAsync(m, Destination, new InlineProgress(p =>
        {
            if (p.Phase.StartsWith("Copiando") && p.FileBytes > 0 && !checkedLock)
            { checkedLock = true; Assert.Throws<IOException>(() => new FileStream(Path.Combine(Source, "big.bin"), FileMode.Open, FileAccess.Write)); }
        }));
        Assert.True(checkedLock); Assert.Equal(BackupStatus.CompleteVerified, m.Status);
    }

    [Fact] public async Task CancellationKeepsDurableNonCompleteStateAndNoPartialFile()
    {
        await Write("big.bin", new string('x', 1000000)); var m = await Analyze(); using var cts = new CancellationTokenSource();
        var backup = await service.CreateAsync(m, Destination, new InlineProgress(p => { if (p.Phase.StartsWith("Copiando")) cts.Cancel(); }), cts.Token);
        Assert.Equal(BackupStatus.Cancelled, m.Status);
        Assert.Equal(BackupStatus.Cancelled, (await service.OpenAndVerifyAsync(backup)).Status);
        Assert.Empty(Directory.EnumerateFiles(backup, "*.partial", SearchOption.AllDirectories));
        Assert.Contains(m.Files, f => !f.IsDirectory && f.Status == BackupItemStatus.Pending);
    }

    [Fact] public async Task InterruptedCheckpointIsNeverCompleteEvenIfPublishedFilesAreValid()
    {
        await Write(); var (backup, m) = await Backup(); m.Status = BackupStatus.Copying; await SaveManifest(backup, m);
        var reopened = await service.OpenAndVerifyAsync(backup);
        Assert.Equal(BackupStatus.Recoverable, reopened.Status);
        Assert.Equal(BackupItemStatus.Verified, Assert.Single(reopened.Files, f => !f.IsDirectory).Status);
    }

    [Fact] public async Task DetectsCorruptionAndOnlyRestoresVerifiedSubset()
    {
        await Write("bad.txt", "bad"); await Write("good.txt", "good");
        var (backup, m) = await Backup(); var bad = m.Files.Single(f => f.RelativePath == "bad.txt");
        await File.WriteAllTextAsync(BackupPaths.Join(backup, bad.BackupPath), "BAD");
        var reopened = await service.OpenAndVerifyAsync(backup);
        Assert.Equal(BackupStatus.Incomplete, reopened.Status); SwitchUser();
        var plan = service.PreviewRestore(backup, reopened, reopened.Categories.Select(c => c.Id));
        Assert.Single(plan.Warnings); var result = await service.RestoreAsync(plan);
        Assert.Equal(1, result.Restored); Assert.False(File.Exists(Path.Combine(NewDocuments, "bad.txt")));
    }

    [Fact] public async Task RechecksHashAtRestoreEvenAfterValidPreview()
    {
        await Write(); var (backup, m) = await Backup(); SwitchUser();
        var plan = service.PreviewRestore(backup, m, m.Categories.Select(c => c.Id));
        var file = m.Files.Single(f => !f.IsDirectory); await File.WriteAllTextAsync(BackupPaths.Join(backup, file.BackupPath), "tampered");
        var result = await service.RestoreAsync(plan);
        Assert.Equal(1, result.Failed); Assert.False(File.Exists(Path.Combine(NewDocuments, "documento.extraño")));
    }

    [Fact] public async Task BackupCanMoveToAnotherDirectoryAndDifferentUser()
    {
        await Write(); var (backup, _) = await Backup(); var moved = Path.Combine(root, "portable-moved"); Directory.Move(backup, moved);
        var m = await service.OpenAndVerifyAsync(moved); SwitchUser();
        var plan = service.PreviewRestore(moved, m, m.Categories.Select(c => c.Id));
        Assert.All(plan.Items, i => Assert.True(BackupPaths.Within(i.Destination, NewDocuments)));
        var result = await service.RestoreAsync(plan); Assert.Equal(1, result.Restored);
        Assert.Equal("Información personal", await File.ReadAllTextAsync(Path.Combine(NewDocuments, "documento.extraño")));
    }

    [Theory] [InlineData(ConflictPolicy.KeepBoth)] [InlineData(ConflictPolicy.Skip)] [InlineData(ConflictPolicy.Replace)]
    public async Task ExistingFileRespectsChosenConflictPolicy(ConflictPolicy policy)
    {
        await Write("conflict.txt", "original"); var (backup, m) = await Backup(); SwitchUser();
        var existing = Path.Combine(NewDocuments, "conflict.txt"); await File.WriteAllTextAsync(existing, "current");
        var plan = service.PreviewRestore(backup, m, m.Categories.Select(c => c.Id));
        Assert.Contains(plan.Items, i => !i.File.IsDirectory && i.Conflict);
        var result = await service.RestoreAsync(plan, policy, policy == ConflictPolicy.Replace);
        Assert.Equal(0, result.Failed);
        Assert.Equal(policy == ConflictPolicy.Replace ? "original" : "current", await File.ReadAllTextAsync(existing));
        Assert.Equal(policy == ConflictPolicy.KeepBoth ? 2 : 1, Directory.GetFiles(NewDocuments).Length);
    }

    [Fact] public async Task ReplacementRequiresExplicitConfirmation()
    {
        await Write(); var (backup, m) = await Backup(); SwitchUser();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(service.PreviewRestore(backup, m, m.Categories.Select(c => c.Id)), ConflictPolicy.Replace));
        Assert.Empty(Directory.GetFiles(NewDocuments));
    }

    [Fact] public async Task ForgedPreviewCannotWriteOutsideAuthorizedTarget()
    {
        await Write(); var (backup, m) = await Backup(); SwitchUser(); var plan = service.PreviewRestore(backup, m, m.Categories.Select(c => c.Id));
        var forged = plan with { Items = plan.Items.Select(i => i with { Destination = Path.Combine(root, "escape.txt") }).ToArray() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(forged));
        Assert.False(File.Exists(Path.Combine(root, "escape.txt")));
    }

    [Fact] public async Task NewConflictAfterPreviewRequiresAnotherReview()
    {
        await Write(); var (backup, m) = await Backup(); SwitchUser();
        var plan = service.PreviewRestore(backup, m, m.Categories.Select(c => c.Id));
        var file = Path.Combine(NewDocuments, "documento.extraño"); await File.WriteAllTextAsync(file, "nuevo");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(plan, ConflictPolicy.Replace, true));
        Assert.Equal("nuevo", await File.ReadAllTextAsync(file));
    }

    [Fact] public async Task ReplaceNeverOverwritesFileThatAppearsWhileCopyIsRunning()
    {
        await Write("big.txt", new string('x', 400000)); var (backup, m) = await Backup(); SwitchUser();
        var plan = service.PreviewRestore(backup, m, m.Categories.Select(c => c.Id)); bool appeared = false;
        var target = Path.Combine(NewDocuments, "big.txt");
        var result = await service.RestoreAsync(plan, ConflictPolicy.Replace, true, new InlineProgress(_ =>
        { if (!appeared) { appeared = true; File.WriteAllText(target, "conservar"); } }));
        Assert.Equal(1, result.Failed); Assert.Equal("conservar", await File.ReadAllTextAsync(target));
    }

    [Theory]
    [InlineData("../escape.txt")] [InlineData("/escape.txt")] [InlineData("C:/escape.txt")] [InlineData("folder/../../escape.txt")]
    [InlineData("folder\\escape.txt")] [InlineData("file.txt:stream")] [InlineData("CON.txt")] [InlineData("LPT1")]
    [InlineData("a./b")] [InlineData("a /b")] [InlineData("//server/share")] [InlineData("a//b")]
    public async Task MaliciousManifestPathsAreRejectedBeforeRestore(string path)
    {
        await Write(); var (backup, m) = await Backup(); var f = m.Files.Single(f => !f.IsDirectory);
        f.RelativePath = path; f.BackupPath = $"files/user-001/{f.CategoryId}/{path}"; await SaveManifest(backup, m);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.OpenAndVerifyAsync(backup));
        Assert.Empty(Directory.GetFiles(NewDocuments));
    }

    [Theory] [InlineData("{} ")] [InlineData("{")] [InlineData("null")] [InlineData("{\"FormatVersion\":999}")]
    [InlineData("{\"Categories\":null,\"Files\":null}")]
    public async Task MalformedOrUnsupportedManifestIsRejected(string json)
    {
        await File.WriteAllTextAsync(Path.Combine(Destination, "manifest.json"), json);
        await Assert.ThrowsAnyAsync<InvalidDataException>(() => service.OpenAndVerifyAsync(Destination));
    }

    [Fact] public async Task DuplicateManifestPathsAreRejected()
    {
        await Write(); var (backup, m) = await Backup(); m.Files.Add(m.Files.Last()); await SaveManifest(backup, m);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.OpenAndVerifyAsync(backup));
    }

    [Fact] public async Task MissingVerifiedFileInvalidatesCompleteness()
    {
        await Write(); var (backup, m) = await Backup(); File.Delete(BackupPaths.Join(backup, m.Files.Single(f => !f.IsDirectory).BackupPath));
        Assert.Equal(BackupStatus.Incomplete, (await service.OpenAndVerifyAsync(backup)).Status);
    }

    [Fact] public async Task JunctionCycleAndEscapeAreNotTraversed()
    {
        await Write(); var link = Path.Combine(Source, "cycle"); await Junction(link, Source);
        var (_, m) = await Backup();
        Assert.Equal(BackupStatus.Incomplete, m.Status); Assert.Single(m.Files, f => !f.IsDirectory);
        Assert.Equal(BackupItemStatus.Failed, m.Files.Single(f => f.RelativePath == "cycle").Status);
    }

    [Fact] public async Task DestinationJunctionIsRejected()
    {
        var link = Path.Combine(root, "redirect-destination"); await Junction(link, Destination);
        Assert.Throws<IOException>(() => service.ValidateDestination(Analyze().GetAwaiter().GetResult(), link));
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    [Fact] public async Task RestoreCannotEscapeThroughExistingJunction()
    {
        await Write("nested/file.txt"); var (backup, m) = await Backup(); SwitchUser();
        var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside); await Junction(Path.Combine(NewDocuments, "nested"), outside);
        Assert.Throws<IOException>(() => service.PreviewRestore(backup, m, m.Categories.Select(c => c.Id)));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    [Fact] public async Task RestoreCancellationDoesNotTouchExistingFiles()
    {
        await Write("big.txt", new string('x', 500000)); var (backup, m) = await Backup(); SwitchUser();
        await File.WriteAllTextAsync(Path.Combine(NewDocuments, "existing.txt"), "keep");
        using var cts = new CancellationTokenSource();
        var plan = service.PreviewRestore(backup, m, m.Categories.Select(c => c.Id));
        var result = await service.RestoreAsync(plan, progress: new InlineProgress(_ => cts.Cancel()), ct: cts.Token);
        Assert.True(result.Cancelled); Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(NewDocuments, "existing.txt")));
        Assert.Empty(Directory.GetFiles(NewDocuments, "*.partial", SearchOption.AllDirectories));
    }

    [Fact] public async Task PreservesLastWriteDateWithoutOldSecurityDescriptor()
    {
        await Write(); var original = Path.Combine(Source, "documento.extraño"); var date = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc); File.SetLastWriteTimeUtc(original, date);
        var (backup, m) = await Backup(); SwitchUser(); await service.RestoreAsync(service.PreviewRestore(backup, m, m.Categories.Select(c => c.Id)));
        Assert.Equal(date, File.GetLastWriteTimeUtc(Path.Combine(NewDocuments, "documento.extraño")));
    }

    private async Task Junction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
        using var p = Process.Start(start)!; await p.WaitForExitAsync(); Assert.Equal(0, p.ExitCode); junctions.Add(link);
    }
    public void Dispose()
    {
        // Only the unique fixture directory is ever removed; junction targets also stay inside that fixture.
        if (!root.StartsWith(Path.Combine(Path.GetTempPath(), "JPCFIX-backup-tests-"), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        // RemoveDirectoryW removes the junction itself; never recursively traverse it during cleanup.
        foreach (var link in junctions)
        {
            if (!BackupPaths.Within(link, root) || (File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0) throw new InvalidOperationException();
            if (!RemoveDirectory(link)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
    [DllImport("kernel32.dll", EntryPoint = "RemoveDirectoryW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveDirectory(string path);
    private sealed class InlineProgress(Action<BackupProgress> report) : IProgress<BackupProgress> { public void Report(BackupProgress value) => report(value); }
    private sealed class FakeEnvironment : IBackupEnvironment
    {
        public BackupProfile CurrentProfile { get; set; } = new("", "", "");
        public IReadOnlyList<PersonalFolder> Folders { get; set; } = [];
        public IReadOnlyList<string> Exclusions { get; set; } = [];
        public long Available = long.MaxValue;
        public string FileSystem = "NTFS";
        public Exception? AssessmentError;
        public Dictionary<string, string> Identities { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? GetLocationIdentity(string path) => Identities.GetValueOrDefault(path);
        public IReadOnlyList<PersonalFolder> GetPersonalFolders() => Folders;
        public IReadOnlyList<string> TechnicalExclusions => Exclusions;
        public DestinationAssessment AssessDestination(string destination, IReadOnlyList<string> sources)
        { if (AssessmentError != null) throw AssessmentError; return new(Available, FileSystem, ["Medio simulado: revisar antes de formatear."]); }
    }
}
