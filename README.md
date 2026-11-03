# CeroLicenseManager

Emisor **local** de licencias para CERO Efectivo (`LicenciasCERO`). Administra clientes, genera un par RSA, firma licencias para el Hardware ID del servidor cliente y descarga archivos `.dat`. Usa ASP.NET Core 8, Identity, Entity Framework Core 8 y SQLite; la interfaz HTML/CSS/JavaScript está en `wwwroot/`.

## Seguridad y primera ejecución

La aplicación escucha en `127.0.0.1:5118` de forma predeterminada. Abra `http://127.0.0.1:5118/login.html` **en el mismo equipo**. En una instalación sin usuarios, esa página permite crear una sola vez la contraseña del administrador `gaston`; después, el alta inicial se cierra. No existe contraseña predeterminada. Se exige una contraseña de al menos 14 caracteres, con mayúscula, minúscula, número y símbolo. ASP.NET Core Identity la guarda como hash en SQLite.

Todos los endpoints de clientes, licencias, llaves, descargas, verificación y dashboard requieren sesión en el servidor. Solo quedan públicos el acceso, el alta inicial local, la entrega del token CSRF y `/health` (que devuelve únicamente `ok`). La sesión usa cookie `HttpOnly`, `SameSite=Strict`, dura ocho horas sin renovación deslizante y se invalida al cerrar sesión. Hay bloqueo por fallos de contraseña, limitación de intentos y validación CSRF en todas las escrituras API. Si se usa HTTPS, la cookie se marca `Secure`.

Los datos viven fuera de la aplicación, en `%ProgramData%\CeroLicenseManager` en Windows. El instalador restringe esa carpeta a administradores, SYSTEM y `LocalService`. Contiene `license-manager.db` (incluido el hash del administrador), `keys/`, `exports/`, `data-protection/` y `backups/`; nada de ello va al repositorio ni al paquete. En Windows, las llaves persistentes de sesión se protegen con DPAPI. El servicio se ejecuta como `LocalService` y el instalador no abre firewall.

Para habilitar una dirección distinta de loopback, el código exige **todas** estas opciones explícitas: `Server:AllowRemote=true`, `Server:BindAddress`, `Server:CertificatePath`, `Server:CertificatePasswordFile` y `AllowedHosts` restringido. El instalador entregado configura solo loopback; no publica un endpoint remoto.

## Requisitos, compilación y pruebas

- Desarrollo: .NET 8 SDK, PowerShell 7 para la prueba de seguridad y acceso a NuGet al restaurar por primera vez.
- Equipo Windows x64 de destino: no necesita .NET si se usa el ZIP autocontenido. La instalación del servicio requiere PowerShell como administrador.
- El emisor no necesita nube ni internet para firmar licencias. La restauración inicial de paquetes NuGet sí puede necesitar internet.

```powershell
dotnet restore .\CeroLicenseManager.csproj
dotnet build .\CeroLicenseManager.csproj -c Release
.\Deployment\package-windows.ps1 -Version 'VERSION'
.\tests\security-smoke.ps1 -ExecutablePath '.\artifacts\CeroLicenseManager-win-x64-VERSION\app\CeroLicenseManager.exe'
```

`package-windows.ps1` crea `artifacts\CeroLicenseManager-win-x64-VERSION.zip` y una carpeta equivalente. El ZIP contiene el ejecutable autocontenido, páginas, `install.ps1`, `manage.ps1`, `verify-package.ps1`, esta guía y un manifiesto SHA-256. No contiene base, llaves, licencias emitidas ni datos de clientes. La prueba usa una base y contraseña aleatorias aisladas; comprueba alta única, login, rechazo de accesos anónimos, CSRF y cierre de sesión.

El paquete y la aplicación pueden verificarse sin privilegios de administrador. Antes de depender del servicio en producción, ensaye `install.ps1`, ACL, respaldo, actualización y desinstalación en un Windows x64 de prueba con PowerShell elevado; esa prueba requiere permisos que la compilación por sí sola no proporciona.

## Instalar en otra computadora Windows x64

1. Extraiga el ZIP en una carpeta temporal. Como administrador, compruebe el manifiesto y ejecute el instalador desde esa carpeta:

   ```powershell
   .\verify-package.ps1
   .\install.ps1
   ```

   El instalador copia la aplicación a `%ProgramFiles%\CeroLicenseManager\app`, crea `%ProgramData%\CeroLicenseManager` con ACL restringida, registra el servicio `CeroLicenseManager` como `LocalService` y comprueba `/health` en loopback.
2. En el **mismo equipo**, abra `http://127.0.0.1:5118/login.html` y cree la contraseña de `gaston`. Genere el par RSA en `/llaves.html` una sola vez.
3. Si está trasladando un emisor existente, **detenga primero la instancia antigua** y use `install.ps1 -LegacyDataPath 'RUTA_ANTIGUA'`. Esa ruta debe contener `license-manager.db` y `keys/private_key.pem` más `keys/public_key.pem`; el script copia esos archivos y las exportaciones sin borrar el origen. No genere llaves nuevas si necesita renovar licencias anteriores.

La versión anterior guardaba la base y las llaves junto al ejecutable. El arranque nuevo detecta esa situación y se detiene antes de crear una base vacía por accidente. Para pruebas aisladas puede configurar la variable `CERO_LICENSE_DATA_ROOT` a una ruta **absoluta**; no la apunte a una carpeta compartida o pública.

## Actualizar, respaldar y desinstalar

Ejecute como administrador. El `manage.ps1` instalado está en `%ProgramFiles%\CeroLicenseManager`; para actualizar use el **paquete nuevo descomprimido**:

```powershell
.\manage.ps1 -Action status
.\manage.ps1 -Action backup
.\manage.ps1 -Action update -PackagePath 'RUTA_DEL_PAQUETE_NUEVO'
.\manage.ps1 -Action uninstall
```

El respaldo detiene brevemente el servicio para copiar SQLite de forma consistente y guarda base, claves, exportaciones y llaves de sesión en `%ProgramData%\CeroLicenseManager\backups\recovery-...`. **Ese respaldo contiene secretos y datos personales**: guárdelo cifrado y restringido fuera del equipo. La actualización verifica el manifiesto, hace respaldo, instala la nueva aplicación y conserva la anterior para reversión. La desinstalación retira servicio y aplicación; deja intactos base, llaves, exportaciones y respaldos en ProgramData. En una migración a otro Windows, las llaves DPAPI de sesión no son portables; cierre sesiones y regenérelas en el nuevo equipo.

## Emitir y activar

1. Inicie sesión como `gaston`, compruebe el par en `/llaves.html` y cree el cliente en `/clientes.html`.
2. Obtenga el Hardware ID desde `/licencia.html` del servidor CERO Efectivo. En `/generar-licencia.html` indique cliente, ID, vigencia y límites. Descargue el `.dat`.
3. Compruebe que `Licensing:PublicKeyPem` del servidor cliente corresponde a `keys/public_key.pem` de **este** emisor. Entregue únicamente el `.dat` y la clave **pública** por un medio confiable; nunca copie `private_key.pem` al cliente.
4. El `SuperAdmin` importa el `.dat` en el servidor cliente y verifica `canOperate: true` en `/api/license/status`.

[LicenseSigningService.cs](Services/LicenseSigningService.cs) firma el JSON canónico del `payload` con RSA-SHA256 y verifica su firma. [Program.cs](Program.cs) permite renovar y descargar un `.dat` actualizado; la importación en el cliente es manual. Suspender o revocar en este panel solo cambia el estado en SQLite: **no invalida una copia `.dat` ya entregada** a un servidor sin conexión.

## Archivos y problemas frecuentes

`Program.cs` registra endpoints y seguridad; `Endpoints/AuthEndpoints.cs` implementa alta y sesión; `Data/AppDbContext.cs` y `Migrations/` definen las tablas; `Services/AppPaths.cs` resuelve el almacenamiento; `Services/KeyManagementService.cs` y `Services/LicenseSigningService.cs` administran y usan las llaves. `.gitignore` y `CeroLicenseManager.csproj` excluyen datos reales y secretos de Git y de `dotnet publish`.

- **No arranca:** compruebe el servicio, la ruta de ProgramData y sus ACL. Si detecta datos heredados, use `-LegacyDataPath` antes de iniciar.
- **No se puede iniciar sesión:** confirme que está en el mismo equipo, revise si la cuenta ya fue creada y espere el fin del bloqueo tras intentos fallidos.
- **Firma rechazada:** compare la clave pública del emisor con `Licensing:PublicKeyPem` del cliente y use el Hardware ID de ese servidor.
- **Par incompleto:** restaure ambos PEM desde respaldo; la aplicación no sobrescribe un archivo de llave existente al generar otro par.
