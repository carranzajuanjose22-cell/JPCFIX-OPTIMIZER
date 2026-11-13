namespace JPCFIX.Optimizer.Application;

// Windows path rules apply even to a manifest produced on another machine.
public static class BackupPaths
{
    public static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public static bool Within(string path, string root) => string.Equals(Full(path), Full(root), StringComparison.OrdinalIgnoreCase)
        || Full(path).StartsWith(Full(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static string SafeRelative(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 32000 || value.Contains('\\') || Path.IsPathRooted(value))
            throw new InvalidDataException("Ruta relativa inválida.");
        foreach (var part in value.Split('/'))
        {
            var stem = part.Split('.')[0];
            if (part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.Any(c => c < 32 || "<>:\"|?*".Contains(c)) ||
                new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
                throw new InvalidDataException("Componente de ruta no permitido.");
        }
        return value;
    }
    public static string Join(string root, string relative)
    {
        var result = Path.GetFullPath(Path.Combine(root, SafeRelative(relative).Replace('/', Path.DirectorySeparatorChar)));
        if (!Within(result, root)) throw new InvalidDataException("Ruta fuera del destino autorizado.");
        RejectLinks(result);
        return result;
    }
    public static void RejectLinks(string path)
    {
        var current = Full(path);
        while (true)
        {
            // GetAttributes also detects existing file links. Missing suffixes are allowed for new destinations.
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("No se permiten vínculos ni junctions en esta ruta."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            var parent = Path.GetDirectoryName(current);
            if (parent == null || parent == current) break;
            current = parent;
        }
    }
}
