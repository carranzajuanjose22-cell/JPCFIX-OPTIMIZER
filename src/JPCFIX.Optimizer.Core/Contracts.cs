namespace JPCFIX.Optimizer.Core;

public enum RiskLevel { Low, Moderate, High }
public enum ChangeStatus { Prepared, Applied, Restored, Failed, RecoveryRequired }
public record PowerPlan(Guid Id, string Name);
public record PowerSnapshot(Guid Active, IReadOnlyList<PowerPlan> Plans, string PowerSource);
public record Compatibility(bool IsApplicable, string Reason);
public record ExecutionPlan(string Id, Guid Original, Guid Target, string Description,
    Compatibility Compatibility, RiskLevel Risk, bool RequiresElevation, bool RequiresRestart, bool CanRollback);
public sealed record ChangeRecord(Guid Id, DateTimeOffset CreatedAt, Guid Original, Guid Target,
    ChangeStatus Status, string Detail);
public record DiagnosticItem(string Category, string Name, string Value);
public record DiagnosticReport(DateTimeOffset CapturedAt, IReadOnlyList<DiagnosticItem> Items, IReadOnlyList<string> Warnings);
public interface IPowerService
{
    PowerSnapshot Read();
    void SetActive(Guid id);
}
public interface IChangeStore
{
    Task SaveAsync(ChangeRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<ChangeRecord>> ReadAllAsync(CancellationToken ct = default);
}
public interface IDiagnosticService { Task<DiagnosticReport> ScanAsync(CancellationToken ct); }
public interface IPrivilegedExecutor
{
    Task ApplyAsync(ExecutionPlan plan);
    Task RollbackAsync(Guid operation);
    Task CreateRestorePointAsync();
}
public interface IOptimization
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    string Category { get; }
    RiskLevel RiskLevel { get; }
    bool RequiresElevation { get; }
    bool RequiresRestart { get; }
    PowerSnapshot ReadCurrentState();
    Compatibility IsApplicable(PowerSnapshot state, Guid target);
    ExecutionPlan Preview(Guid target);
    Task<ChangeRecord> Backup(ExecutionPlan plan, CancellationToken ct = default);
    Task Apply(ExecutionPlan plan, CancellationToken ct = default);
    bool Verify(Guid expected);
    Task Rollback(Guid operation, CancellationToken ct = default);
}
