using System.Text.Json;

namespace JPCFIX.Optimizer.Infrastructure;

public static class AuditLog
{
    // Only fixed action/status labels and operation GUIDs; no paths, scripts, user data or exception payloads.
    public static void Write(string action, string status)
    {
        var directory = Path.Combine(Path.GetDirectoryName(JsonChangeStore.DefaultDirectory)!, "Audit");
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N");
        var temp = Path.Combine(directory, id + ".tmp");
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(stream, new { Id = id, At = DateTimeOffset.UtcNow, Action = action, Status = status }); stream.Flush(true); }
        File.Move(temp, Path.Combine(directory, id + ".json"));
    }
}
