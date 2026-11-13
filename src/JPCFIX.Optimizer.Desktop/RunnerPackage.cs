using System.Security.Cryptography;
using JPCFIX.Optimizer.Application;

namespace JPCFIX.Optimizer.Desktop;

public static class RunnerPackage
{
    public static FileStream OpenVerified(string applicationDirectory, string? expectedSha256)
    {
        if (expectedSha256 == null || expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Falta la identidad del ejecutor. Usá una publicación portable completa generada por scripts/publish.ps1.");
        var path = BackupPaths.Join(applicationDirectory, "Runner/JPCFIX.Optimizer.Runner.exe");
        if (!File.Exists(path)) throw new InvalidOperationException("Falta Runner/JPCFIX.Optimizer.Runner.exe. Copiá toda la carpeta portable, incluida Runner.");
        // Retain the read-only lock through UAC and process completion; no replacement/write sharing.
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("El ejecutor no corresponde a esta publicación o fue modificado. No se solicitó UAC. Volvé a copiar el paquete original completo.");
            BackupPaths.RejectLinks(path);
            stream.Position = 0;
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}
