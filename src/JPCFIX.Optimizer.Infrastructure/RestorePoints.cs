using System.Management;

namespace JPCFIX.Optimizer.Infrastructure;

public sealed class RestorePoints
{
    public IReadOnlyList<long> Read()
    {
        using var search = new ManagementObjectSearcher(new ManagementScope(@"root\default"),
            new ObjectQuery("SELECT SequenceNumber FROM SystemRestore"), new EnumerationOptions { Timeout = TimeSpan.FromSeconds(15), ReturnImmediately = false });
        using var rows = search.Get();
        return rows.Cast<ManagementBaseObject>().Select(r => { using (r) return Convert.ToInt64(r["SequenceNumber"]); }).ToArray();
    }
    public void CreateAndVerify()
    {
        var before = Read();
        var scope = new ManagementScope(@"\\.\root\default", new ConnectionOptions { EnablePrivileges = true });
        using var systemRestore = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
        using var input = systemRestore.GetMethodParameters("CreateRestorePoint");
        input["Description"] = "JPCFIX Optimizer: respaldo adicional";
        input["RestorePointType"] = 12u;
        input["EventType"] = 100u;
        using var result = systemRestore.InvokeMethod("CreateRestorePoint", input, new InvokeMethodOptions { Timeout = TimeSpan.FromMinutes(3) });
        if (result is null || Convert.ToUInt32(result["ReturnValue"]) != 0)
            throw new InvalidOperationException("Windows no permitió crear el punto de restauración. No se habilita protección automáticamente.");
        if (!Read().Except(before).Any())
            throw new InvalidOperationException("No se pudo comprobar un nuevo punto de restauración. Windows puede limitar su frecuencia; no se considera exitoso.");
    }
}
