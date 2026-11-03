param(
    [Parameter(Mandatory=$true)][ValidateSet('status','backup','update','uninstall')][string]$Action,
    [string]$PackagePath
)
$ErrorActionPreference = 'Stop'
$serviceName = 'CeroLicenseManager'
$installRoot = Join-Path $env:ProgramFiles 'CeroLicenseManager'
$appRoot = Join-Path $installRoot 'app'
$dataRoot = Join-Path $env:ProgramData 'CeroLicenseManager'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $service) { throw 'El servicio no está instalado.' }
if ($Action -eq 'status') {
    Get-Service -Name $serviceName | Format-List Name,Status,StartType
    Write-Host "Health local: $(try { (Invoke-WebRequest 'http://127.0.0.1:5118/health' -TimeoutSec 2 -UseBasicParsing).StatusCode } catch { 'sin respuesta' })"
    return
}
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Abra PowerShell como administrador.' }

if ($Action -eq 'backup') {
    $wasRunning = $service.Status -ne 'Stopped'
    if ($wasRunning) { Stop-Service -Name $serviceName; (Get-Service $serviceName).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30)) }
    try {
        $bundle = Join-Path $dataRoot ('backups\recovery-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $bundle | Out-Null
        $db = Join-Path $dataRoot 'license-manager.db'
        if (-not (Test-Path $db)) { throw 'No existe la base de datos.' }
        Copy-Item -LiteralPath $db -Destination (Join-Path $bundle 'license-manager.db')
        foreach ($suffix in @('-wal','-shm')) {
            if (Test-Path -LiteralPath "$db$suffix") {
                Copy-Item -LiteralPath "$db$suffix" -Destination (Join-Path $bundle "license-manager.db$suffix")
            }
        }
        foreach ($folder in @('keys','exports','data-protection')) {
            $source = Join-Path $dataRoot $folder
            if (Test-Path $source) { Copy-Item -LiteralPath $source -Destination $bundle -Recurse }
        }
        Write-Host "Respaldo: $bundle"
        Write-Host 'Contiene claves y datos personales. Copie esta carpeta a un medio cifrado y protegido.'
    }
    finally { if ($wasRunning) { Start-Service -Name $serviceName } }
    return
}

if ($Action -eq 'update') {
    if (-not $PackagePath) { throw 'Indique -PackagePath con el paquete nuevo descomprimido.' }
    $package = (Resolve-Path -LiteralPath $PackagePath).Path
    & (Join-Path $package 'verify-package.ps1') -PackagePath $package
    & $PSCommandPath -Action backup
    $service.Refresh()
    $wasRunning = $service.Status -ne 'Stopped'
    if ($wasRunning) { Stop-Service -Name $serviceName; (Get-Service $serviceName).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30)) }
    $suffix = Get-Date -Format 'yyyyMMdd-HHmmss'
    $next = Join-Path $installRoot "app.next-$suffix"
    $previous = Join-Path $installRoot "app.previous-$suffix"
    if ((Test-Path $next) -or (Test-Path $previous)) { throw 'Ya existe una carpeta de actualización con ese nombre.' }
    try {
        New-Item -ItemType Directory -Path $next | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $package 'app') -Force | Copy-Item -Destination $next -Recurse
        Move-Item -LiteralPath $appRoot -Destination $previous
        Move-Item -LiteralPath $next -Destination $appRoot
        if ($wasRunning) { Start-Service -Name $serviceName }
        $ready = $false
        for ($i=0; $i -lt 30; $i++) {
            Start-Sleep -Seconds 1
            try {
                if ((Invoke-WebRequest 'http://127.0.0.1:5118/health' -TimeoutSec 2 -UseBasicParsing).StatusCode -eq 200) {
                    $ready = $true; break
                }
            } catch { }
        }
        if (-not $ready) { throw 'La versión nueva no respondió.' }
        foreach ($name in @('manage.ps1','verify-package.ps1','README.md')) {
            Copy-Item -LiteralPath (Join-Path $package $name) -Destination (Join-Path $installRoot $name) -Force
        }
        Write-Host "Actualización lista. Versión anterior: $previous"
    }
    catch {
        if ((Get-Service $serviceName).Status -ne 'Stopped') { Stop-Service $serviceName }
        if (Test-Path $previous) {
            if (Test-Path $appRoot) { Move-Item -LiteralPath $appRoot -Destination (Join-Path $installRoot "app.failed-$suffix") }
            Move-Item -LiteralPath $previous -Destination $appRoot
        }
        if ($wasRunning) { Start-Service $serviceName }
        throw
    }
    return
}

if ($Action -eq 'uninstall') {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name $serviceName; (Get-Service $serviceName).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30)) }
    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo retirar el servicio.' }
    if (Test-Path -LiteralPath $installRoot) { Remove-Item -LiteralPath $installRoot -Recurse -Force }
    Write-Host "Servicio y aplicación retirados. Base, llaves, exportaciones y respaldos permanecen en $dataRoot"
}
