[CmdletBinding()]
param([int]$ApplicationProcessId = 0)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package = Join-Path $repo 'dist/JPCFIX-Optimizer-Portable'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
public static class JpcfixPortableInspection {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", EntryPoint="FindResourceW")] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
    delegate bool EnumName(IntPtr module, IntPtr type, IntPtr name, IntPtr param);
    [DllImport("kernel32.dll", EntryPoint="EnumResourceNamesW")] static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumName callback, IntPtr param);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool OpenProcessToken(IntPtr process, uint access, out SafeFileHandle token);
    [DllImport("advapi32.dll", SetLastError=true)] static extern bool GetTokenInformation(SafeFileHandle token, int kind, out int data, int length, out int returned);
    public static string Manifest(string path) {
        var module = LoadLibraryEx(path, IntPtr.Zero, 0x22); // data/image resources only; no code execution
        if (module == IntPtr.Zero) throw new InvalidOperationException("No se pudo leer el PE.");
        try {
            var resource = FindResource(module, new IntPtr(1), new IntPtr(24));
            if (resource == IntPtr.Zero) throw new InvalidOperationException("Falta manifiesto PE.");
            var size = checked((int)SizeofResource(module, resource));
            var bytes = new byte[size]; Marshal.Copy(LockResource(LoadResource(module, resource)), bytes, 0, size);
            return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
        } finally { FreeLibrary(module); }
    }
    public static bool HasIcon(string path) {
        var module = LoadLibraryEx(path, IntPtr.Zero, 0x22);
        if (module == IntPtr.Zero) return false;
        try {
            bool found = false;
            EnumName callback = (m,t,n,p) => { found = true; return false; };
            EnumResourceNames(module, new IntPtr(14), callback, IntPtr.Zero);
            GC.KeepAlive(callback); return found;
        } finally { FreeLibrary(module); }
    }
    public static bool IsElevated(IntPtr process) {
        SafeFileHandle token;
        if (!OpenProcessToken(process, 8, out token)) throw new InvalidOperationException("No se pudo leer el token del proceso.");
        using(token) { int value, size; if (!GetTokenInformation(token, 20, out value, 4, out size)) throw new InvalidOperationException("Token no verificable."); return value != 0; }
    }
}
'@
$results = @()
foreach ($entry in @(@{ Name = 'JPCFIX-Optimizer.exe'; Level = 'asInvoker' }, @{ Name = 'Runner/JPCFIX.Optimizer.Runner.exe'; Level = 'requireAdministrator' })) {
    $path = Join-Path $package $entry.Name
    $stream = [IO.File]::OpenRead($path)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'Falta cabecera MZ.' }
        $stream.Position = 0x3C; $pe = $reader.ReadUInt32(); $stream.Position = $pe
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Se requiere un PE Windows x64.' }
        $stream.Position = $pe + 24 + 68
        if ($reader.ReadUInt16() -ne 2) { throw 'El ejecutable debe usar el subsistema gráfico Windows, sin consola.' }
    } finally { $reader.Dispose(); $stream.Dispose() }
    [xml]$manifest = [JpcfixPortableInspection]::Manifest($path)
    $level = $manifest.SelectSingleNode("//*[local-name()='requestedExecutionLevel']").GetAttribute('level')
    if ($level -ne $entry.Level) { throw "Nivel UAC incorrecto: $($entry.Name)." }
    if (![JpcfixPortableInspection]::HasIcon($path)) { throw 'Falta ícono PE.' }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if (!(Select-String -LiteralPath (Join-Path $package 'SHA256SUMS.txt') -SimpleMatch "$hash  $($entry.Name)" -Quiet)) { throw 'Checksum de distribución incorrecto.' }
    $results += [pscustomobject]@{ File = $entry.Name; Bytes = (Get-Item -LiteralPath $path).Length; Architecture = 'x64'; Subsystem = 'Windows GUI'; Elevation = $level; Icon = $true; SHA256 = $hash }
}
$processResult = $null
if ($ApplicationProcessId -gt 0) {
    $process = Get-Process -Id $ApplicationProcessId
    try {
        $path = $process.MainModule.FileName
        $elevated = [JpcfixPortableInspection]::IsElevated($process.Handle)
        if ($path -ne (Join-Path $package 'JPCFIX-Optimizer.exe') -or $elevated) { throw 'El proceso debe corresponder al paquete y estar sin elevación.' }
        $modules = @($process.Modules | Where-Object { $_.ModuleName -match '^(coreclr|wpfgfx_cor3|PresentationNative_cor3|wminet_utils)\.dll$' } | ForEach-Object { $_.FileName })
        # The single-file host may statically link CoreCLR. WPF's native modules must actually be loaded.
        if (@($modules | Where-Object { [IO.Path]::GetFileName($_) -match '^(wpfgfx_cor3|PresentationNative_cor3)\.dll$' }).Count -lt 2) { throw 'No se detectaron las bibliotecas nativas de WPF cargadas.' }
        $processResult = [pscustomobject]@{ Id = $ApplicationProcessId; Elevated = $elevated; RuntimeModules = $modules }
    } finally { $process.Dispose() }
}
$report = [pscustomobject]@{ At = [DateTimeOffset]::UtcNow; Files = $results; RunningProcess = $processResult }
$directory = Join-Path $repo 'artifacts'
[void][IO.Directory]::CreateDirectory($directory)
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'portable-verification.json') -Encoding UTF8
$report | ConvertTo-Json -Depth 5
