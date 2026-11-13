namespace JPCFIX.Optimizer.Core;

public enum BackupStatus { Pending, Analyzing, Prepared, Copying, Verifying, CompleteVerified, Incomplete, Failed, Cancelled, Recoverable }
public enum BackupItemStatus { Pending, Verified, Failed, Skipped, DownloadRequired }
public enum ConflictPolicy { KeepBoth, Skip, Replace }
public sealed record PersonalFolder(string Key, string Name, string Path, bool Recommended = true);
public sealed record BackupProfile(string Id, string Name, string Path);
public sealed class BackupCategory
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string KnownFolder { get; set; } = "Extras";
    public bool IsFile { get; set; }
}
public sealed class BackupFile
{
    public string CategoryId { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Length { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public FileAttributes Attributes { get; set; }
    public string? Sha256 { get; set; }
    public BackupItemStatus Status { get; set; }
    public string? Error { get; set; }
}
public sealed class BackupManifest
{
    public int FormatVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Machine { get; set; } = Environment.MachineName;
    public string OperatingSystem { get; set; } = Environment.OSVersion.VersionString;
    public BackupProfile Profile { get; set; } = new("", "", "");
    public BackupStatus Status { get; set; } = BackupStatus.Pending;
    public List<BackupCategory> Categories { get; set; } = [];
    public List<BackupFile> Files { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public TimeSpan Elapsed { get; set; }
}
public sealed record BackupProgress(string Phase, string File, int Processed, int Total, long Bytes, long TotalBytes,
    long FileBytes, long FileLength, double BytesPerSecond);
public sealed record DestinationAssessment(long AvailableBytes, string FileSystem, IReadOnlyList<string> Warnings);
public sealed record RestoreItem(BackupFile File, string Destination, bool Conflict);
public sealed record RestorePlan(string BackupRoot, BackupManifest Manifest, IReadOnlyList<RestoreItem> Items,
    IReadOnlyList<string> Warnings);
public sealed record RestoreResult(int Restored, int Skipped, int Failed, bool Cancelled, IReadOnlyList<string> Errors);

public interface IBackupEnvironment
{
    BackupProfile CurrentProfile { get; }
    IReadOnlyList<PersonalFolder> GetPersonalFolders();
    IReadOnlyList<string> TechnicalExclusions { get; }
    // Optional physical identity deduplicates aliases of an existing directory without reading its contents.
    string? GetLocationIdentity(string path) => null;
    DestinationAssessment AssessDestination(string destination, IReadOnlyList<string> sources);
}
