using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using JPCFIX.Optimizer.Core;

namespace JPCFIX.Optimizer.Infrastructure;

public sealed class JsonChangeStore(string directory, bool writable = false) : IChangeStore
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "JPCFIX.Optimizer", "Changes");
    public static void SecureDefaultDirectory()
    {
        var root = Path.GetDirectoryName(DefaultDirectory)!;
        // Refuse redirected directories at every existing ancestor.
        RejectReparse(root);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinUsersSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null),
                sid == WellKnownSidType.BuiltinUsersSid ? FileSystemRights.ReadAndExecute : FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        if (!Directory.Exists(root)) FileSystemAclExtensions.CreateDirectory(security, root);
        RejectReparse(root);
        var info = new DirectoryInfo(root);
        var owner = info.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null || (!owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) && !owner.IsWellKnown(WellKnownSidType.LocalSystemSid)))
            throw new UnauthorizedAccessException("El directorio de respaldos no pertenece a administradores o SYSTEM.");
        info.SetAccessControl(security);
        RejectReparse(DefaultDirectory);
        Directory.CreateDirectory(DefaultDirectory);
        new DirectoryInfo(DefaultDirectory).SetAccessControl(security);
        var audit = Path.Combine(root, "Audit");
        RejectReparse(audit);
        Directory.CreateDirectory(audit);
        new DirectoryInfo(audit).SetAccessControl(security);
    }
    private static void RejectReparse(string path)
    {
        for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("No se permiten directorios de respaldo redirigidos.");
    }
    public async Task SaveAsync(ChangeRecord record, CancellationToken ct = default)
    {
        if (!writable) throw new UnauthorizedAccessException("Almacenamiento de solo lectura.");
        RejectReparse(directory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, record.Id.ToString("D") + ".json");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Archivo redirigido.");
        var temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, record, cancellationToken: ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public async Task<IReadOnlyList<ChangeRecord>> ReadAllAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(directory)) return [];
        RejectReparse(directory);
        var result = new List<ChangeRecord>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Registro redirigido.");
            await using var stream = File.OpenRead(file);
            if (stream.Length > 65536) throw new IOException("Registro fuera de límites.");
            var record = await JsonSerializer.DeserializeAsync<ChangeRecord>(stream, cancellationToken: ct) ?? throw new IOException("Registro inválido.");
            if (record.Id == Guid.Empty || record.Original == Guid.Empty || record.Target == Guid.Empty ||
                !Enum.IsDefined(record.Status) || Path.GetFileNameWithoutExtension(file) != record.Id.ToString("D"))
                throw new IOException("Registro inconsistente: se bloqueó la recuperación automática.");
            result.Add(record);
        }
        return result.OrderByDescending(r => r.CreatedAt).ToArray();
    }
}
