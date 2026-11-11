using System.Diagnostics;
using JPCFIX.Optimizer.Core;

namespace JPCFIX.Optimizer.Desktop;

public sealed class RunnerClient : IPrivilegedExecutor
{
    public Task ApplyAsync(ExecutionPlan plan) => Run("apply", plan.Original.ToString("D"), plan.Target.ToString("D"));
    public Task RollbackAsync(Guid operation) => Run("rollback", operation.ToString("D"));
    public Task CreateRestorePointAsync() => Run("restore-point");
    private static async Task Run(params string[] args)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Runner", "JPCFIX.Optimizer.Runner.exe");
        if (!File.Exists(executable)) throw new InvalidOperationException("Falta Runner/JPCFIX.Optimizer.Runner.exe. Usá la carpeta portable publicada completa.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar el ejecutor.");
        await process.WaitForExitAsync();
        if (process.ExitCode == 1223) throw new OperationCanceledException();
        if (process.ExitCode != 0) throw new InvalidOperationException("El ejecutor no completó la operación. Revisá el mensaje y el historial.");
    }
}
