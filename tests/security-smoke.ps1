param([Parameter(Mandatory=$true)][string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Esta prueba requiere PowerShell 7.' }
$exe = (Resolve-Path -LiteralPath $ExecutablePath).Path
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$root = Join-Path $repo ('test-results\security-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port; $listener.Stop()
$base = "http://127.0.0.1:$port"
$env:CERO_LICENSE_DATA_ROOT = (Join-Path $root 'data')
$env:Server__Port = [string]$port
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$process = $null
try {
    $process = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $root 'stdout.log') -RedirectStandardError (Join-Path $root 'stderr.log')
} finally {
    Remove-Item Env:CERO_LICENSE_DATA_ROOT,Env:Server__Port,Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
}

function Assert-Status($response, [int]$expected, [string]$label) {
    if ($response.StatusCode -ne $expected) { throw "$label : esperaba $expected, obtuvo $($response.StatusCode)." }
}
function Send($path, $method, $session, $body = $null, $token = $null) {
    $arguments = @{
        Uri = "$base$path"; Method = $method; WebSession = $session
        TimeoutSec = 5; SkipHttpErrorCheck = $true
    }
    if ($token) { $arguments.Headers = @{ 'X-CSRF-TOKEN' = $token } }
    if ($null -ne $body) {
        $arguments.Body = ($body | ConvertTo-Json -Depth 8)
        $arguments.ContentType = 'application/json'
    }
    return Invoke-WebRequest @arguments
}

try {
    $ready = $false
    for ($i=0; $i -lt 80; $i++) {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw 'La aplicación terminó antes de responder.' }
        try { if ((Invoke-WebRequest "$base/health" -TimeoutSec 2).StatusCode -eq 200) { $ready = $true; break } } catch { }
    }
    if (-not $ready) { throw 'La aplicación no respondió en loopback.' }
    $anonymous = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    Assert-Status (Send '/api/customers' 'GET' $anonymous) 401 'Clientes sin sesión'
    Assert-Status (Send '/api/keys/status' 'GET' $anonymous) 401 'Llaves sin sesión'
    Assert-Status (Send '/api/licenses' 'GET' $anonymous) 401 'Licencias sin sesión'
    Assert-Status (Send '/api/dashboard/summary' 'GET' $anonymous) 401 'Dashboard sin sesión'
    Assert-Status (Send '/api/licenses/1/download-dat' 'GET' $anonymous) 401 'Descarga sin sesión'
    foreach ($path in @('/api/customers','/api/licenses/generate','/api/licenses/verify',
        '/api/licenses/1/renew','/api/licenses/1/suspend','/api/licenses/1/revoke','/api/keys/generate')) {
        Assert-Status (Send $path 'POST' $anonymous @{}) 401 "$path sin sesión"
    }
    $state = (Send '/api/auth/csrf' 'GET' $session).Content | ConvertFrom-Json
    if (-not $state.setupRequired -or -not $state.csrfToken) { throw 'El alta inicial no está disponible.' }
    Assert-Status (Send '/api/auth/setup' 'POST' $session @{ password='weak' } $state.csrfToken) 400 'Contraseña débil'
    $password = 'Aa!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(16))
    $created = Send '/api/auth/setup' 'POST' $session @{ password=$password } $state.csrfToken
    Assert-Status $created 200 'Alta inicial'
    $cookie = @($created.Headers['Set-Cookie'] | Where-Object { $_ -match 'CeroLicenseManager.Session' }) -join ';'
    if ($cookie -notmatch 'httponly' -or $cookie -notmatch 'samesite=strict') { throw 'La cookie de sesión no tiene las protecciones esperadas.' }
    $state = (Send '/api/auth/csrf' 'GET' $session).Content | ConvertFrom-Json
    if ($state.setupRequired) { throw 'El alta inicial permaneció abierta.' }
    Assert-Status (Send '/api/auth/setup' 'POST' $session @{ password=$password } $state.csrfToken) 409 'Segunda alta'
    Assert-Status (Send '/api/customers' 'GET' $session) 200 'Clientes con sesión'
    $cookieOnly = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    foreach ($cookieValue in $session.Cookies.GetCookies([Uri]$base)) { $cookieOnly.Cookies.Add($cookieValue) }
    Assert-Status (Send '/api/keys/generate' 'POST' $cookieOnly @{}) 400 'Escritura sin CSRF'
    Assert-Status (Send '/api/auth/logout' 'POST' $session @{} $state.csrfToken) 200 'Cierre de sesión'
    Assert-Status (Send '/api/customers' 'GET' $session) 401 'Acceso tras cerrar sesión'
    $state = (Send '/api/auth/csrf' 'GET' $session).Content | ConvertFrom-Json
    Assert-Status (Send '/api/auth/login' 'POST' $session @{ password='incorrecta' } $state.csrfToken) 401 'Contraseña incorrecta'
    $login = Send '/api/auth/login' 'POST' $session @{ password=$password } $state.csrfToken
    Assert-Status $login 200 'Contraseña correcta'
    Assert-Status (Send '/api/auth/me' 'GET' $session) 200 'Sesión renovada'
    $rateSession = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $rateToken = ((Send '/api/auth/csrf' 'GET' $rateSession).Content | ConvertFrom-Json).csrfToken
    $limited = $false
    for ($attempt=0; $attempt -lt 12; $attempt++) {
        if ((Send '/api/auth/login' 'POST' $rateSession @{ password='incorrecta' } $rateToken).StatusCode -eq 429) {
            $limited = $true; break
        }
    }
    if (-not $limited) { throw 'El límite de intentos no respondió con 429.' }
    Write-Host 'Prueba de seguridad correcta: alta única, login, API protegida, CSRF, cookie, logout y límite de intentos.'
}
finally {
    $password = $null
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
}
