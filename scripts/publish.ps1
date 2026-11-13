[CmdletBinding()]
param([string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stage = Join-Path $repo ('artifacts/publish-' + [Guid]::NewGuid().ToString('N'))
$runnerOutput = Join-Path $stage 'runner'
$desktopOutput = Join-Path $stage 'desktop'
$destination = Join-Path $repo 'dist/JPCFIX-Optimizer-Portable'
function Invoke-Dotnet([string[]]$Arguments) {
    & $DotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet falló ($LASTEXITCODE): $($Arguments[0]). No distribuir este resultado." }
}
function Assert-WorkspaceDirectory([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (!$resolved.StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Directorio fuera del workspace.' }
    for ($current = [IO.DirectoryInfo]::new($resolved); $null -ne $current -and $current.FullName -ne $repo; $current = $current.Parent) {
        if ($current.Exists -and ($current.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'No se permiten vínculos en los directorios de publicación.' }
    }
}
Assert-WorkspaceDirectory $stage
Assert-WorkspaceDirectory $destination
Push-Location $repo
try {
    # Native resources are source-controlled; generate them once if starting from a source-only copy.
    if (!(Test-Path -LiteralPath (Join-Path $repo 'src/JPCFIX.Optimizer.Desktop/Assets/Jpcfix.ico'))) { & (Join-Path $PSScriptRoot 'create-icon.ps1') }
    if (!(Test-Path -LiteralPath (Join-Path $repo 'src/JPCFIX.Optimizer.Desktop/Assets/BrandLogo.ps'))) { & (Join-Path $PSScriptRoot 'compile-logo-shader.ps1') }
    Invoke-Dotnet -Arguments @('test', 'JPCFIX.Optimizer.slnx', '-c', 'Release')
    Invoke-Dotnet -Arguments @('publish', 'src/JPCFIX.Optimizer.Runner/JPCFIX.Optimizer.Runner.csproj', '-p:PublishProfile=Portable', '-p:DebugType=embedded', '-c', 'Release', '-r', 'win-x64', '-o', $runnerOutput)
    $runnerExe = Join-Path $runnerOutput 'JPCFIX.Optimizer.Runner.exe'
    $runnerHash = (Get-FileHash -LiteralPath $runnerExe -Algorithm SHA256).Hash
    Invoke-Dotnet -Arguments @('publish', 'src/JPCFIX.Optimizer.Desktop/JPCFIX.Optimizer.Desktop.csproj', '-p:PublishProfile=Portable', '-p:DebugType=embedded', '-c', 'Release', '-r', 'win-x64', "-p:RunnerSha256=$runnerHash", '-o', $desktopOutput)
    # Fail closed if WPF or another dependency unexpectedly escapes the single-file bundle.
    foreach ($folder in @($runnerOutput, $desktopOutput)) {
        $files = @(Get-ChildItem -LiteralPath $folder -Recurse -File)
        if ($files.Count -ne 1 -or $files[0].Extension -ne '.exe') { throw "La publicación contiene dependencias externas inesperadas en $folder. Revisarlas antes de distribuir." }
    }
    # Never recursively delete an existing package. Preserve it beside the new release.
    if (Test-Path -LiteralPath $destination) {
        $item = Get-Item -LiteralPath $destination -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'El destino dist no puede ser un vínculo.' }
        $resolved = [IO.Path]::GetFullPath($item.FullName)
        if ($resolved -ne [IO.Path]::GetFullPath((Join-Path $repo 'dist/JPCFIX-Optimizer-Portable'))) { throw 'Destino fuera del workspace.' }
        Move-Item -LiteralPath $resolved -Destination ($resolved + '-previous-' + [Guid]::NewGuid().ToString('N'))
    }
    $runnerDestination = Join-Path $destination 'Runner'
    [void][IO.Directory]::CreateDirectory($runnerDestination)
    Copy-Item -LiteralPath (Join-Path $desktopOutput 'JPCFIX.Optimizer.Desktop.exe') -Destination (Join-Path $destination 'JPCFIX-Optimizer.exe')
    Copy-Item -LiteralPath $runnerExe -Destination (Join-Path $runnerDestination 'JPCFIX.Optimizer.Runner.exe')
    $help = @'
JPCFIX OPTIMIZER — PORTABLE WINDOWS x64

Copiar TODA esta carpeta al pendrive, incluida la subcarpeta Runner.
Iniciar con doble clic en JPCFIX-Optimizer.exe. No se instala .NET.
No iniciar el archivo de Runner directamente ni mezclar versiones.
El inicio no solicita administrador. Los cambios privilegiados solicitan UAC.

Los respaldos personales se guardan en el destino que selecciones.
Los registros de cambios de energía se conservan en ProgramData/JPCFIX.Optimizer.
Configuración e informes del usuario: LocalAppData/JPCFIX.Optimizer.
Los temporales de extracción de .NET no son un lugar de respaldo.

La publicación no lleva firma Authenticode. No desactives Defender ni SmartScreen.
Antes de formatear, verificar el respaldo y que el destino sobrevivirá al borrado.
'@
    [IO.File]::WriteAllText((Join-Path $destination 'LEEME.txt'), $help, [Text.UTF8Encoding]::new($false))
    $checksums = @('JPCFIX-Optimizer.exe', 'Runner/JPCFIX.Optimizer.Runner.exe') | ForEach-Object {
        $hash = (Get-FileHash -LiteralPath (Join-Path $destination $_) -Algorithm SHA256).Hash
        "$hash  $_"
    }
    [IO.File]::WriteAllLines((Join-Path $destination 'SHA256SUMS.txt'), [string[]]$checksums, [Text.Encoding]::ASCII)
    Write-Output "Portable generado: $destination"
    Write-Output 'Copiar toda esa carpeta. Iniciar JPCFIX-Optimizer.exe.'
} finally { Pop-Location }
