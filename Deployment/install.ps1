param([string]$LegacyDataPath)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') { throw 'Windows requerido.' }
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Abra PowerShell como administrador.' }
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $PSScriptRoot
$serviceName = 'CeroLicenseManager'
$installRoot = Join-Path $env:ProgramFiles 'CeroLicenseManager'
$appRoot = Join-Path $installRoot 'app'
$dataRoot = Join-Path $env:ProgramData 'CeroLicenseManager'
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { throw 'El servicio ya existe; use manage.ps1 update.' }
if (Test-Path -LiteralPath $appRoot) { throw 'Ya existe la aplicación; no se sobrescribirá.' }
New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
foreach ($folder in @($dataRoot, (Join-Path $dataRoot 'keys'), (Join-Path $dataRoot 'exports'),
    (Join-Path $dataRoot 'data-protection'), (Join-Path $dataRoot 'backups'))) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}
& icacls.exe $dataRoot /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' '*S-1-5-19:(OI)(CI)M' /T | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron proteger los datos.' }
if ($LegacyDataPath) {
    $legacy = (Resolve-Path -LiteralPath $LegacyDataPath).Path
    if ($legacy -eq $dataRoot) { throw 'La carpeta heredada debe ser diferente de ProgramData.' }
    if ((Test-Path (Join-Path $dataRoot 'license-manager.db')) -or
        (Test-Path (Join-Path $dataRoot 'keys\private_key.pem'))) { throw 'Ya hay datos persistentes; no se sobrescribirán.' }
    $legacyDb = Join-Path $legacy 'license-manager.db'
    $legacyPrivate = Join-Path $legacy 'keys\private_key.pem'
    $legacyPublic = Join-Path $legacy 'keys\public_key.pem'
    if (-not (Test-Path $legacyDb) -or -not (Test-Path $legacyPrivate) -or -not (Test-Path $legacyPublic)) {
        throw 'La migración necesita base y ambos archivos PEM.'
    }
    Copy-Item -LiteralPath $legacyDb -Destination (Join-Path $dataRoot 'license-manager.db')
    foreach ($suffix in @('-wal','-shm')) {
        if (Test-Path -LiteralPath "$legacyDb$suffix") {
            Copy-Item -LiteralPath "$legacyDb$suffix" -Destination (Join-Path $dataRoot "license-manager.db$suffix")
        }
    }
    Copy-Item -LiteralPath $legacyPrivate -Destination (Join-Path $dataRoot 'keys\private_key.pem')
    Copy-Item -LiteralPath $legacyPublic -Destination (Join-Path $dataRoot 'keys\public_key.pem')
    $legacyExports = Join-Path $legacy 'exports'
    if (Test-Path $legacyExports) {
        Get-ChildItem -LiteralPath $legacyExports -File -Filter '*.dat' |
            Copy-Item -Destination (Join-Path $dataRoot 'exports')
    }
    & icacls.exe $dataRoot /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' '*S-1-5-19:(OI)(CI)M' /T | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudieron proteger los datos migrados.' }
}
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'app') -Force |
    Copy-Item -Destination $appRoot -Recurse -Force
foreach ($name in @('manage.ps1','verify-package.ps1','README.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $installRoot $name)
}
$exe = Join-Path $appRoot 'CeroLicenseManager.exe'
& sc.exe create $serviceName "binPath= `"$exe`"" 'start= auto' 'obj= NT AUTHORITY\LocalService' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'No se pudo registrar el servicio.' }
Start-Service -Name $serviceName
$ready = $false
for ($i=0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    try {
        $response = Invoke-WebRequest 'http://127.0.0.1:5118/health' -TimeoutSec 2 -UseBasicParsing
        if ($response.StatusCode -eq 200) { $ready = $true; break }
    } catch { }
}
if (-not $ready) { throw 'El servicio no respondió en loopback. Revise el Visor de eventos; se conservaron los datos.' }
Write-Host 'Servicio instalado. Abra http://127.0.0.1:5118/login.html en este equipo.'
Write-Host 'Cree la contraseña de Gaston en la primera visita. No se configuró acceso de red.'
