$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class JpcfixShaderCompiler {
    [ComImport, Guid("8BA5FB08-5195-40E2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IBlob { [PreserveSig] IntPtr GetBufferPointer(); [PreserveSig] UIntPtr GetBufferSize(); }
    [DllImport("d3dcompiler_47.dll", CharSet=CharSet.Ansi)]
    static extern int D3DCompile(byte[] source, UIntPtr length, string name, IntPtr defines, IntPtr include, string entry, string target, uint flags, uint extra, out IBlob code, out IBlob errors);
    public static byte[] Compile(byte[] source) {
        IBlob code = null, errors = null;
        try {
            int result = D3DCompile(source, new UIntPtr((uint)source.Length), "BrandLogo.hlsl", IntPtr.Zero, IntPtr.Zero, "main", "ps_2_0", 0, 0, out code, out errors);
            if(result < 0) throw new InvalidOperationException(errors == null ? "Shader compilation failed." : Marshal.PtrToStringAnsi(errors.GetBufferPointer()));
            byte[] bytes = new byte[checked((int)code.GetBufferSize().ToUInt64())]; Marshal.Copy(code.GetBufferPointer(), bytes, 0, bytes.Length); return bytes;
        } finally { if(code != null) Marshal.ReleaseComObject(code); if(errors != null) Marshal.ReleaseComObject(errors); }
    }
}
'@
$assets = Join-Path $PSScriptRoot '../src/JPCFIX.Optimizer.Desktop/Assets'
$source = [IO.File]::ReadAllBytes((Join-Path $assets 'BrandLogo.hlsl'))
[IO.File]::WriteAllBytes((Join-Path $assets 'BrandLogo.ps'), [JpcfixShaderCompiler]::Compile($source))
Write-Output 'BrandLogo.ps compilado: WPF Shader Model 2.0.'
