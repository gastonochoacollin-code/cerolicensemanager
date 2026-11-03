param([string]$PackagePath = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackagePath).Path.TrimEnd('\')
$manifestPath = Join-Path $package 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'Falta manifest.json.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$listed = @()
foreach ($entry in $manifest.files) {
    if ($entry.path -notmatch '^[A-Za-z0-9._/-]+$' -or $entry.path.Contains('..')) { throw 'Ruta de manifiesto inválida.' }
    $path = [IO.Path]::GetFullPath((Join-Path $package $entry.path))
    if (-not $path.StartsWith($package+'\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Archivo fuera del paquete.' }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Falta archivo: $($entry.path)" }
    $file = Get-Item -LiteralPath $path
    if ($file.Length -ne $entry.bytes -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
        throw "Archivo alterado: $($entry.path)"
    }
    $listed += $entry.path
}
$actual = @(Get-ChildItem -LiteralPath $package -Recurse -File |
    Where-Object { $_.FullName -ne $manifestPath } |
    ForEach-Object { $_.FullName.Substring($package.Length+1).Replace('\','/') })
if (@(Compare-Object ($listed | Sort-Object) ($actual | Sort-Object)).Count -ne 0) { throw 'El contenido no coincide con el manifiesto.' }
if (-not (Test-Path (Join-Path $package 'app\CeroLicenseManager.exe'))) { throw 'Falta la aplicación.' }
Write-Host "Manifiesto verificado: $($listed.Count) archivos."
