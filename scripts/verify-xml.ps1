$ErrorActionPreference = 'Stop'
Get-ChildItem -Path (Join-Path $PSScriptRoot '..') -Recurse -File -Include *.xaml,*.csproj,*.props,*.manifest,*.slnx | ForEach-Object { [xml](Get-Content -LiteralPath $_.FullName -Raw) | Out-Null }
Write-Output 'XML syntax OK'
