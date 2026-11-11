using JPCFIX.Optimizer.Core;

namespace JPCFIX.Optimizer.Application;

// This coordinator runs inside the privileged boundary. Preview is read-only.
public sealed class PowerOptimization(IPowerService power, IChangeStore store, Func<bool> supported) : IOptimization
{
    public string Id => "windows.power-plan";
    public string Name => "Plan de energía";
    public string Description => "Cambiar a un plan existente. Puede aumentar el consumo y la temperatura; no garantiza mayor rendimiento.";
    public string Category => "Energía";
    public RiskLevel RiskLevel => RiskLevel.Moderate;
    public bool RequiresElevation => true;
    public bool RequiresRestart => false;
    public PowerSnapshot ReadCurrentState() => power.Read();
    public Compatibility IsApplicable(PowerSnapshot state, Guid target)
    {
        if (!supported()) return new(false, "Requiere Windows 10/11 (compilación 10240 o posterior).");
        if (!state.Plans.Any(p => p.Id == state.Active)) return new(false, "No se puede garantizar la restauración del plan activo.");
        if (target == Guid.Empty || !state.Plans.Any(p => p.Id == target)) return new(false, "El plan seleccionado no existe en este equipo.");
        return new(true, state.Active == target ? "El plan ya está activo; no se realizarán cambios." : "Compatible: alternativa existente y reversión disponible.");
    }
    public ExecutionPlan Preview(Guid target)
    {
        var state = ReadCurrentState();
        return new(Id, state.Active, target, $"{Description}\nAlimentación: {state.PowerSource}", IsApplicable(state, target), RiskLevel, true, false,
            state.Plans.Any(p => p.Id == state.Active));
    }
    public async Task<ChangeRecord> Backup(ExecutionPlan plan, CancellationToken ct = default)
    {
        Validate(plan);
        var record = new ChangeRecord(Guid.NewGuid(), DateTimeOffset.UtcNow, plan.Original, plan.Target, ChangeStatus.Prepared, "Respaldo e intención guardados antes de modificar Windows.");
        await store.SaveAsync(record, ct);
        return record;
    }
    private void Validate(ExecutionPlan plan)
    {
        var current = power.Read();
        var compatible = IsApplicable(current, plan.Target);
        if (plan.Id != Id || !compatible.IsApplicable || current.Active != plan.Original || !plan.CanRollback)
            throw new InvalidOperationException("La previsualización perdió vigencia o la operación no está autorizada. Volvé a simular.");
    }
    public bool Verify(Guid expected) => power.Read().Active == expected;
    public async Task Apply(ExecutionPlan plan, CancellationToken ct = default)
    {
        Validate(plan);
        if (plan.Original == plan.Target) return;
        var record = await Backup(plan, ct);
        // No cancellation after durable intent: finish verification/compensation.
        try
        {
            Validate(plan);
            power.SetActive(plan.Target);
            if (!Verify(plan.Target)) throw new InvalidOperationException("Windows no confirmó el plan solicitado.");
            await store.SaveAsync(record with { Status = ChangeStatus.Applied, Detail = "Plan aplicado y verificado." });
        }
        catch
        {
            try
            {
                var failedState = power.Read();
                if (!failedState.Plans.Any(p => p.Id == record.Original) || (failedState.Active != record.Target && failedState.Active != record.Original)) throw new InvalidOperationException();
                if (failedState.Active != record.Original) power.SetActive(record.Original);
                if (!Verify(record.Original)) throw new InvalidOperationException();
                await store.SaveAsync(record with { Status = ChangeStatus.Restored, Detail = "Falló la aplicación; estado original restaurado y verificado." });
            }
            catch
            {
                await store.SaveAsync(record with { Status = ChangeStatus.RecoveryRequired, Detail = "No se pudo verificar la compensación. Se requiere recuperación manual." });
            }
            throw;
        }
    }
    public async Task Rollback(Guid operation, CancellationToken ct = default)
    {
        var record = (await store.ReadAllAsync(ct)).SingleOrDefault(r => r.Id == operation)
            ?? throw new InvalidOperationException("Operación inexistente.");
        if (record.Status == ChangeStatus.Restored) return;
        var state = power.Read();
        if (!state.Plans.Any(p => p.Id == record.Original)) throw new InvalidOperationException("El plan original ya no existe. No se modificó Windows.");
        if (state.Active != record.Target && state.Active != record.Original)
            throw new InvalidOperationException("Otro cambio alteró el plan activo. Reversión bloqueada para no sobrescribirlo.");
        await store.SaveAsync(record with { Status = ChangeStatus.RecoveryRequired, Detail = "Intención de restauración guardada." }, ct);
        if (state.Active != record.Original) power.SetActive(record.Original);
        if (!Verify(record.Original)) throw new InvalidOperationException("No se pudo verificar la restauración.");
        await store.SaveAsync(record with { Status = ChangeStatus.Restored, Detail = "Plan original restaurado y verificado." });
    }
}
