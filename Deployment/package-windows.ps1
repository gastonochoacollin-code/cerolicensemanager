param([string]$Version = (Get-Date -Format 'yyyyMMdd-HHmmss'))
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[A-Za-z0-9._-]+$') { throw 'Versión inválida.' }
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$artifacts = Join-Path $repo 'artifacts'
$stage = Join-Path $artifacts "publish-$Version"
$package = Join-Path $artifacts "CeroLicenseManager-win-x64-$Version"
$zip = "$package.zip"
if ((Test-Path $stage) -or (Test-Path $package) -or (Test-Path $zip)) { throw 'La versión ya existe en artifacts.' }
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
dotnet publish (Join-Path $repo 'CeroLicenseManager.csproj') -c Release -r win-x64 --self-contained true -p:UseAppHost=true -o $stage
if ($LASTEXITCODE -ne 0) { throw 'Falló dotnet publish.' }
New-Item -ItemType Directory -Path (Join-Path $package 'app') -Force | Out-Null
Get-ChildItem -LiteralPath $stage -Force | Copy-Item -Destination (Join-Path $package 'app') -Recurse
foreach ($name in @('install.ps1','manage.ps1','verify-package.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $package $name)
}
Copy-Item -LiteralPath (Join-Path $repo 'README.md') -Destination (Join-Path $package 'README.md')
$files = @(Get-ChildItem -LiteralPath $package -Recurse -File)
if (-not (Test-Path (Join-Path $package 'app\CeroLicenseManager.exe'))) { throw 'Falta el ejecutable autocontenido.' }
foreach ($file in $files) {
    $relative = $file.FullName.Substring($package.Length + 1).Replace('\','/')
    if ($file.Extension -in @('.db','.dat','.pem','.pfx','.key','.cer','.env') -or
        $relative -match '(^|/)(keys|exports|backups|data-protection|test-results|artifacts|publish|tests|\.git|\.codex|bin|obj)/') {
        throw "Archivo de datos o secreto en el paquete: $relative"
    }
}
$manifest = @($files | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($package.Length + 1).Replace('\','/')
        bytes = $_.Length
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
})
@{ version=$Version; builtAtUtc=[DateTime]::UtcNow.ToString('o'); files=$manifest } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $package 'manifest.json') -Encoding UTF8
& (Join-Path $package 'verify-package.ps1') -PackagePath $package
Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip
Write-Host "Paquete verificado: $package"
Write-Host "ZIP: $zip"
