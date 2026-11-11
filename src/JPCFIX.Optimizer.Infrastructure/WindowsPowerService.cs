using System.ComponentModel;
using System.Runtime.InteropServices;
using JPCFIX.Optimizer.Core;

namespace JPCFIX.Optimizer.Infrastructure;

public sealed class WindowsPowerService : IPowerService
{
    public PowerSnapshot Read()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var ptr));
        Guid active;
        try { active = Marshal.PtrToStructure<Guid>(ptr); } finally { LocalFree(ptr); }
        var plans = new List<PowerPlan>();
        for (uint index = 0; ; index++)
        {
            uint size = 16;
            var code = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, out var id, ref size);
            if (code == 259) break;
            Check(code);
            uint bytes = 0;
            code = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref bytes);
            if (code != 0 && code != 234) Check(code);
            if (bytes > 65536) throw new InvalidOperationException("Nombre de plan fuera de límites.");
            var buffer = new byte[bytes];
            Check(PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref bytes));
            plans.Add(new(id, System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0')));
        }
        return new(active, plans, PowerSource());
    }
    public void SetActive(Guid id)
    {
        if (!Read().Plans.Any(p => p.Id == id)) throw new InvalidOperationException("Plan inexistente.");
        Check(PowerSetActiveScheme(IntPtr.Zero, ref id));
    }
    public static string PowerSource()
    {
        if (!GetSystemPowerStatus(out var s)) return "No disponible";
        var source = s.ACLineStatus switch { 0 => "Batería", 1 => "Conectado a la red eléctrica", _ => "No disponible" };
        return source + (s.BatteryLifePercent <= 100 ? $" · batería {s.BatteryLifePercent}%" : "");
    }
    private static void Check(uint code) { if (code != 0) throw new Win32Exception((int)code); }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus
    { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr guid);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint access, uint index, out Guid guid, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr ptr);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
