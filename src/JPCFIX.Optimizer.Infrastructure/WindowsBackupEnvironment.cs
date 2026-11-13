using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using JPCFIX.Optimizer.Core;

namespace JPCFIX.Optimizer.Infrastructure;

public sealed class WindowsBackupEnvironment : IBackupEnvironment
{
    public BackupProfile CurrentProfile
    {
        get { using var identity = WindowsIdentity.GetCurrent(); return new(identity.User?.Value ?? "current", Environment.UserName, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)); }
    }
    public IReadOnlyList<PersonalFolder> GetPersonalFolders()
    {
        (string Key, string Name, string Id)[] definitions =
        [
            ("Desktop", "Escritorio", "B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
            ("Documents", "Documentos", "FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
            ("Downloads", "Descargas", "374DE290-123F-4565-9164-39C4925E467B"),
            ("Pictures", "Imágenes", "33E28130-4E1E-4676-835A-98395C3BC3BB"),
            ("Music", "Música", "4BD8D571-6D19-48D3-BE97-422220080E43"),
            ("Videos", "Videos", "18989B1D-99B5-455B-841C-AB7C74E4DDFC"),
            ("Favorites", "Favoritos de Windows", "1777F761-68AD-4D8A-87BD-30B759FA33DD"),
            ("SavedGames", "Partidas guardadas", "4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4")
        ];
        var result = new List<PersonalFolder>();
        foreach (var definition in definitions)
        {
            var id = Guid.Parse(definition.Id);
            var code = SHGetKnownFolderPath(ref id, 0x4000 /* DONT_VERIFY; current redirected path */, IntPtr.Zero, out var ptr);
            try
            {
                // Missing path remains visible; it must not disappear silently from the selection.
                var path = code == 0 ? Marshal.PtrToStringUni(ptr) ?? "" : "";
                var recommended = definition.Key is not ("Favorites" or "SavedGames") || Directory.Exists(path);
                result.Add(new(definition.Key, definition.Name, path, recommended));
            }
            finally { if (ptr != IntPtr.Zero) Marshal.FreeCoTaskMem(ptr); }
        }
        return result;
    }
    private readonly IReadOnlyList<string> exclusions = new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Path.GetTempPath()
    }.Where(p => !string.IsNullOrWhiteSpace(p)).Concat(DriveInfo.GetDrives().SelectMany(d =>
        new[] { "pagefile.sys", "hiberfil.sys", "swapfile.sys", "$Recycle.Bin", "System Volume Information" }.Select(n => Path.Combine(d.Name, n)))).ToArray();
    public IReadOnlyList<string> TechnicalExclusions => exclusions;

    public string? GetLocationIdentity(string path)
    {
        using var handle = CreateFile(path, 0x80 /* READ_ATTRIBUTES */, 7 /* READ | WRITE | DELETE sharing */, IntPtr.Zero,
            3 /* OPEN_EXISTING */, 0x02200000 /* BACKUP_SEMANTICS | OPEN_REPARSE_POINT */, IntPtr.Zero);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info) || (info.IndexHigh == 0 && info.IndexLow == 0)) return null;
        var final = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, final, final.Capacity, 1 /* volume GUID */);
        if (length == 0) length = GetFinalPathNameByHandle(handle, final, final.Capacity, 0 /* UNC / DOS fallback */);
        if (length == 0 || length >= final.Capacity) return null;
        // Volume identity prevents collisions between cloned disks with the same serial/file index.
        var volume = Path.GetPathRoot(final.ToString());
        return volume == null ? null : $"{volume}:{info.VolumeSerial:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}";
    }

    public DestinationAssessment AssessDestination(string destination, IReadOnlyList<string> sources)
    {
        if (!GetDiskFreeSpaceEx(destination, out var available, out _, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var warnings = new List<string>();
        var volumePath = new StringBuilder(32768);
        string format = "No verificado";
        if (GetVolumePathName(destination, volumePath, volumePath.Capacity))
        {
            var fs = new StringBuilder(256);
            if (GetVolumeInformation(volumePath.ToString(), null, 0, out _, out _, out _, fs, fs.Capacity)) format = fs.ToString();
        }
        if (format == "No verificado") warnings.Add("No se pudo verificar el formato. Revisá límites del almacenamiento y servidor.");
        var destinationDisks = PhysicalDisks(destination);
        var sourceDisks = sources.Append(CurrentProfile.Path).SelectMany(PhysicalDisks).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (destinationDisks.Overlaps(sourceDisks)) warnings.Add("PELIGRO: el destino comparte disco físico con el perfil o un origen. Formatearlo o reemplazarlo puede destruir el respaldo.");
        else if (sources.Append(CurrentProfile.Path).Any(s => string.Equals(Path.GetPathRoot(s), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase)))
            warnings.Add("PELIGRO: el destino está en el mismo volumen que un origen o el perfil. No está protegido contra el formateo.");
        if (destinationDisks.Count == 0 || sourceDisks.Count == 0)
            warnings.Add("No se comprobó la separación de discos físicos (red, permisos o proveedor). Verificá manualmente el destino antes de borrar discos.");
        warnings.Add("Confirmá que conservarás este destino al reinstalar Windows. La copia no está cifrada; contiene datos personales.");
        return new(available > long.MaxValue ? long.MaxValue : (long)available, format, warnings);
    }
    private static HashSet<string> PhysicalDisks(string path)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = Path.GetPathRoot(path);
        if (root == null || root.Length < 2 || !char.IsAsciiLetter(root[0]) || root[1] != ':') return result;
        // Only a validated drive letter enters the WMI query; never interpolate user-controlled WQL.
        try
        {
            using var search = new ManagementObjectSearcher(new ManagementScope(@"root\cimv2"),
                new ObjectQuery($"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{char.ToUpperInvariant(root[0])}:'}} WHERE AssocClass=Win32_LogicalDiskToPartition"),
                new EnumerationOptions { Timeout = TimeSpan.FromSeconds(5), ReturnImmediately = false });
            using var partitions = search.Get();
            foreach (ManagementBaseObject partition in partitions)
                using (partition) if (partition["DiskIndex"] != null) result.Add(Convert.ToUInt32(partition["DiskIndex"]).ToString());
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
        catch (COMException) { }
        return result;
    }
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, int length, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string path, out ulong available, out ulong total, out ulong free);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(string file, StringBuilder volume, int length);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(string root, StringBuilder? name, int nameSize, out uint serial, out uint maxComponent,
        out uint flags, StringBuilder format, int formatSize);
}
