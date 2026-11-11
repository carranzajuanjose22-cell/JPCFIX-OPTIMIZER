using System.Security.Principal;
using System.Windows;
using JPCFIX.Optimizer.Application;
using JPCFIX.Optimizer.Core;
using JPCFIX.Optimizer.Infrastructure;

namespace JPCFIX.Optimizer.Runner;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 5;
            _ = RunnerRequest.Parse(args);
            using var mutex = new Mutex(false, @"Global\JPCFIX.Optimizer.Change.v1");
            bool held;
            try { held = mutex.WaitOne(0); } catch (AbandonedMutexException) { held = true; }
            if (!held) throw new InvalidOperationException("Ya hay otra operación en curso.");
            try
            {
                JsonChangeStore.SecureDefaultDirectory();
                AuditLog.Write(args[0], "Started");
                if (!WindowsCompatibility.IsSupported()) throw new InvalidOperationException("Solo se admiten Windows 10/11 de escritorio.");
                var store = new JsonChangeStore(JsonChangeStore.DefaultDirectory, true);
                var optimization = new PowerOptimization(new WindowsPowerService(), store, WindowsCompatibility.IsSupported);
                if (args[0] == "apply")
                {
                    var plan = optimization.Preview(Guid.ParseExact(args[2], "D"));
                    if (plan.Original != Guid.ParseExact(args[1], "D") || !plan.Compatibility.IsApplicable)
                        throw new InvalidOperationException("El estado cambió o el plan no es compatible. Volvé a simular.");
                    if (store.ReadAllAsync().GetAwaiter().GetResult().Any(r => r.Status is ChangeStatus.Prepared or ChangeStatus.RecoveryRequired))
                        throw new InvalidOperationException("Recuperá las operaciones incompletas antes de aplicar otro cambio.");
                    Confirm($"Cambiar el plan activo\nDe: {plan.Original}\nA: {plan.Target}\n\n{plan.Description}\n\nSe guardará el respaldo local. ¿Confirmás este cambio?");
                    optimization.Apply(plan).GetAwaiter().GetResult();
                }
                else if (args[0] == "rollback")
                {
                    var id = Guid.ParseExact(args[1], "D");
                    var record = store.ReadAllAsync().GetAwaiter().GetResult().Single(r => r.Id == id);
                    Confirm($"Restaurar operación {id}\nPlan original: {record.Original}\nPlan aplicado: {record.Target}\n\n¿Confirmás la restauración exacta del plan original?");
                    optimization.Rollback(id).GetAwaiter().GetResult();
                }
                else
                {
                    Confirm("Solicitar a Windows un punto de restauración adicional. No reemplaza al respaldo del plan de energía. ¿Continuar?");
                    new RestorePoints().CreateAndVerify();
                }
                AuditLog.Write(args[0], "Completed");
                return 0;
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch (OperationCanceledException) { TryAudit("Cancelled"); return 1223; }
        catch (Exception ex)
        {
            TryAudit("FailedOrRejected");
            var message = ex is InvalidOperationException ? ex.Message : "La operación falló por permisos, proveedor de Windows o persistencia. Revisá el historial antes de reintentar.";
            MessageBox.Show(message, "JPCFIX · Operación no completada", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
    private static void TryAudit(string status)
    {
        // Never trust an existing directory after validation failed. Logging is best effort on the error path.
        try { JsonChangeStore.SecureDefaultDirectory(); AuditLog.Write("Runner", status); } catch { }
    }
    private static void Confirm(string message)
    {
        if (MessageBox.Show(message, "JPCFIX · Confirmación privilegiada", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            throw new OperationCanceledException();
    }
}
