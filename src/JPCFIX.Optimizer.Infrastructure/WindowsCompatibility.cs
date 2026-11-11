using Microsoft.Win32;

namespace JPCFIX.Optimizer.Infrastructure;
public static class WindowsCompatibility
{
    public static bool IsSupported()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240) || Environment.OSVersion.Version.Major != 10) return false;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions");
            return string.Equals(key?.GetValue("ProductType") as string, "WinNT", StringComparison.Ordinal);
        }
        catch { return false; }
    }
}
