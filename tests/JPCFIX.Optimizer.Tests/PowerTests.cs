using JPCFIX.Optimizer.Application;
using JPCFIX.Optimizer.Core;
using JPCFIX.Optimizer.Infrastructure;
using Xunit;

namespace JPCFIX.Optimizer.Tests;

public sealed class PowerTests
{
    [Fact] public void UnsupportedWindowsIsRejected()
    { var p = new FakePower(); Assert.False(new PowerOptimization(p, new MemoryStore(), () => false).Preview(p.Target).Compatibility.IsApplicable); }
    [Fact] public void UnknownSelectionIsRejected()
    { var p = new FakePower(); Assert.False(Create(p).Preview(Guid.NewGuid()).Compatibility.IsApplicable); }
    [Fact] public void MissingOriginalBlocksReversalAndApplication()
    { var p = new FakePower { IncludeOriginal = false }; Assert.False(Create(p).Preview(p.Target).CanRollback); Assert.False(Create(p).Preview(p.Target).Compatibility.IsApplicable); }
    [Fact] public void DryRunDoesNotWrite()
    { var p = new FakePower(); var s = new MemoryStore(); var plan = Create(p, s).Preview(p.Target); Assert.Equal(p.Original, plan.Original); Assert.True(plan.RequiresElevation); Assert.False(plan.RequiresRestart); Assert.True(plan.CanRollback); Assert.Empty(s.Records); Assert.Equal(0, p.Writes); }
    [Fact] public async Task ApplyIsIdempotentAndBackupPrecedesMutation()
    {
        var p = new FakePower(); var s = new MemoryStore(); p.OnWrite = () => Assert.Contains(s.Records.Values, r => r.Status == ChangeStatus.Prepared);
        var engine = Create(p, s); await engine.Apply(engine.Preview(p.Target));
        p.OnWrite = null; await engine.Apply(engine.Preview(p.Target));
        Assert.Equal(1, p.Writes); Assert.Single(s.Records); Assert.Equal(ChangeStatus.Applied, s.Records.Values.Single().Status);
    }
    [Fact] public async Task BackupFailurePreventsMutation()
    { var p = new FakePower(); var e = Create(p, new MemoryStore { Fail = true }); await Assert.ThrowsAsync<IOException>(() => e.Apply(e.Preview(p.Target))); Assert.Equal(0, p.Writes); }
    [Fact] public async Task StalePreviewIsRejected()
    { var p = new FakePower(); var e = Create(p); var plan = e.Preview(p.Target); p.Active = p.Target; await Assert.ThrowsAsync<InvalidOperationException>(() => e.Apply(plan)); Assert.Equal(0, p.Writes); }
    [Fact] public async Task ForgedOptimizationIsRejected()
    { var p = new FakePower(); var e = Create(p); await Assert.ThrowsAsync<InvalidOperationException>(() => e.Apply(e.Preview(p.Target) with { Id = "script" })); Assert.Equal(0, p.Writes); }
    [Fact] public async Task ApplyFailureCompensates()
    { var p = new FakePower { FailTarget = true }; var s = new MemoryStore(); var e = Create(p, s); await Assert.ThrowsAsync<IOException>(() => e.Apply(e.Preview(p.Target))); Assert.Equal(p.Original, p.Active); Assert.Equal(ChangeStatus.Restored, s.Records.Values.Single().Status); }
    [Fact] public async Task VerificationFailureCompensates()
    { var p = new FakePower { IgnoreTarget = true }; var s = new MemoryStore(); var e = Create(p, s); await Assert.ThrowsAsync<InvalidOperationException>(() => e.Apply(e.Preview(p.Target))); Assert.Equal(ChangeStatus.Restored, s.Records.Values.Single().Status); }
    [Fact] public async Task FailedCompensationRemainsRecoverable()
    { var p = new FakePower { FailTarget = true, FailReadAfterWrite = true }; var s = new MemoryStore(); var e = Create(p, s); await Assert.ThrowsAsync<IOException>(() => e.Apply(e.Preview(p.Target))); Assert.Equal(ChangeStatus.RecoveryRequired, s.Records.Values.Single().Status); }
    [Fact] public async Task IndividualRollbackRestoresExactOriginal()
    { var p = new FakePower(); var s = new MemoryStore(); var e = Create(p, s); await e.Apply(e.Preview(p.Target)); var id = s.Records.Keys.Single(); await e.Rollback(id); await e.Rollback(id); Assert.Equal(p.Original, p.Active); Assert.Equal(2, p.Writes); Assert.Equal(ChangeStatus.Restored, s.Records[id].Status); }
    [Theory] [InlineData(ChangeStatus.Prepared)] [InlineData(ChangeStatus.RecoveryRequired)]
    public async Task InterruptedOperationCanBeRecovered(ChangeStatus state)
    { var p = new FakePower(); var s = new MemoryStore(); var id = Guid.NewGuid(); await s.SaveAsync(new(id, DateTimeOffset.UtcNow, p.Original, p.Target, state, "interrupted")); p.Active = p.Target; await Create(p, s).Rollback(id); Assert.Equal(p.Original, p.Active); }
    [Fact] public async Task LaterExternalChangeIsNotOverwritten()
    { var p = new FakePower(); var s = new MemoryStore(); var e = Create(p, s); await e.Apply(e.Preview(p.Target)); p.Active = Guid.NewGuid(); await Assert.ThrowsAsync<InvalidOperationException>(() => e.Rollback(s.Records.Keys.Single())); Assert.Equal(1, p.Writes); }
    [Theory] [InlineData("powershell", "x")] [InlineData("apply", "not-a-guid")] [InlineData("rollback", "00000000-0000-0000-0000-000000000000")]
    public void ArbitraryRunnerRequestsAreRejected(string action, string value) => Assert.Throws<InvalidOperationException>(() => RunnerRequest.Parse([action, value]));
    [Fact] public void TypedRunnerRequestIsAccepted()
    { var from = Guid.NewGuid(); var to = Guid.NewGuid(); var r = RunnerRequest.Parse(["apply", from.ToString("D"), to.ToString("D")]); Assert.Equal(RunnerAction.Apply, r.Action); Assert.Equal(to, r.Target); }
    [Fact] public async Task JsonBackupSurvivesReopeningAndAtomicUpdate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "JPCFIX-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var r = new ChangeRecord(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), ChangeStatus.Prepared, "backup");
            await new JsonChangeStore(directory, true).SaveAsync(r);
            Assert.Equal(r, Assert.Single(await new JsonChangeStore(directory).ReadAllAsync()));
            await new JsonChangeStore(directory, true).SaveAsync(r with { Status = ChangeStatus.Applied });
            Assert.Equal(ChangeStatus.Applied, Assert.Single(await new JsonChangeStore(directory).ReadAllAsync()).Status);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static PowerOptimization Create(FakePower p, MemoryStore? s = null) => new(p, s ?? new(), () => true);
    private sealed class MemoryStore : IChangeStore
    {
        public Dictionary<Guid, ChangeRecord> Records { get; } = [];
        public bool Fail;
        public Task SaveAsync(ChangeRecord record, CancellationToken ct = default) { if (Fail) throw new IOException(); Records[record.Id] = record; return Task.CompletedTask; }
        public Task<IReadOnlyList<ChangeRecord>> ReadAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ChangeRecord>>(Records.Values.ToArray());
    }
    private sealed class FakePower : IPowerService
    {
        public Guid Original { get; } = Guid.NewGuid(); public Guid Target { get; } = Guid.NewGuid();
        public Guid Active; public int Writes; public bool IncludeOriginal = true, FailTarget, IgnoreTarget, FailReadAfterWrite; public Action? OnWrite;
        public FakePower() => Active = Original;
        public PowerSnapshot Read()
        { if (FailReadAfterWrite && Writes > 0) throw new IOException(); return new(Active, IncludeOriginal ? [new(Original, "Original"), new(Target, "Alternative")] : [new(Target, "Alternative")], "Batería"); }
        public void SetActive(Guid id)
        { OnWrite?.Invoke(); Writes++; if (id == Target && FailTarget) throw new IOException(); if (!(id == Target && IgnoreTarget)) Active = id; }
    }
}
