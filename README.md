# CeroLicenseManager

Panel web **interno** para emitir licencias sin conexión del servidor CERO Efectivo (`LicenciasCERO` / `Cashless.Api`). Está escrito en ASP.NET Core 8, usa Entity Framework Core 8 con SQLite y sirve una interfaz HTML, CSS y JavaScript desde `wwwroot/`.

El emisor administra clientes, genera un par RSA, firma licencias ligadas al Hardware ID del servidor cliente, permite descargarlas como `license.dat` y comprueba firmas. También conserva registros de emisión y permite renovar una licencia. **No tiene autenticación ni autorización**: ejecútelo solo en un equipo de confianza, ligado a loopback y sin exponer el puerto a una red.

## Requisitos y compilación

- .NET 8 SDK para compilar. `dotnet restore` necesita acceso a NuGet en la primera restauración si los paquetes no están en caché.
- Windows, Linux o macOS compatible con .NET 8 para el proyecto fuente. El comando de publicación de abajo crea un paquete autocontenido **Windows x64**.
- Espacio y permisos de escritura para `license-manager.db`, `keys/` y `exports/` en la carpeta desde la que se ejecuta la aplicación.

```powershell
dotnet restore .\CeroLicenseManager.csproj
dotnet build .\CeroLicenseManager.csproj -c Release
dotnet run --project .\CeroLicenseManager.csproj --launch-profile http
```

El perfil `http` usa `http://localhost:5118`. Abra esa URL solo en el equipo del emisor. `Program.cs` aplica las migraciones de `Migrations/` al arrancar; no es obligatorio ejecutar `dotnet ef database update`. El manifiesto `.config/dotnet-tools.json` permite usar `dotnet tool restore` y `dotnet ef` para mantenimiento, si se necesita.

## Instalar en otra computadora

Para compilar directamente allí, copie o clone este repositorio, instale .NET 8 SDK y ejecute los comandos anteriores. Para generar un paquete que no requiera .NET en un equipo Windows x64:

```powershell
dotnet publish .\CeroLicenseManager.csproj -c Release -r win-x64 --self-contained true -p:UseAppHost=true -o .\publish
```

Copie el contenido de `publish/` al equipo nuevo, abra PowerShell en esa carpeta y ejecute:

```powershell
.\CeroLicenseManager.exe --urls http://127.0.0.1:5118
```

Abra `http://127.0.0.1:5118` en el mismo equipo. La primera ejecución crea una base SQLite vacía y aplica migraciones. `appsettings.json` usa `Data Source=license-manager.db` relativa al directorio de trabajo. Mantenga fijo ese directorio o configure `ConnectionStrings:DefaultConnection` para una ruta persistente. No copie al paquete la base ni las claves del equipo de desarrollo.

**Si es una instalación nueva**, genere el par de claves en `/llaves.html` y guarde una copia protegida de `keys/private_key.pem`, `keys/public_key.pem` y `license-manager.db` fuera del equipo. **Si está trasladando un emisor existente**, restaure esas tres piezas desde un respaldo seguro antes de emitir o renovar. Generar otro par invalida la verificación de licencias anteriores con la clave pública anterior; una base nueva también reinicia la secuencia de IDs `CE-AÑO-NNNN`.

## Configuración y archivos

| Ruta | Función |
| --- | --- |
| `Program.cs` | Arranque, migraciones y endpoints de clientes, licencias y claves |
| `CeroLicenseManager.csproj` | .NET 8 y dependencias EF Core/SQLite |
| `Data/AppDbContext.cs`, `Models/`, `Migrations/` | Esquema de clientes, licencias y registros |
| `Services/KeyManagementService.cs` | Generación RSA 4096 y lectura del par de claves |
| `Services/LicenseSigningService.cs` | JSON canónico, firma/verificación RSA-SHA256 y exportación |
| `Services/LicenseIdService.cs` | IDs `CE-AÑO-NNNN` basados en la base local |
| `wwwroot/` | Panel de llaves, clientes y licencias |
| `appsettings.json`, `Properties/launchSettings.json` | SQLite y perfil local de desarrollo |
| `.config/dotnet-tools.json` | Herramienta opcional `dotnet-ef` |

`keys/`, `exports/` y `license-manager.db` son datos **locales**, no parte del repositorio ni del paquete publicado. La clave privada se lee del disco para firmar; la API solo descarga la clave pública. El archivo `.gitignore` excluye claves, bases, licencias reales y artefactos, y el proyecto excluye esos archivos de `dotnet publish`.

## Emitir y activar una licencia

1. En `/llaves.html`, genere el par RSA una sola vez o compruebe que el par restaurado coincida.
2. Cree el cliente en `/clientes.html`.
3. En el **servidor cliente** CERO Efectivo, inicie sesión como `SuperAdmin`, abra `/licencia.html` y obtenga el Hardware ID o descargue la solicitud JSON. El ID deriva de `MachineGuid` de Windows y la arquitectura del servidor; al cambiar de equipo normalmente se necesita una licencia nueva.
4. En `/generar-licencia.html`, seleccione cliente, pegue ese Hardware ID y establezca vigencia, límites y funciones. Descargue el `.dat` emitido.
5. Compruebe que `Licensing:PublicKeyPem` en la configuración del servidor cliente corresponde a `keys/public_key.pem` de **este emisor**. La clave pública puede distribuirse; nunca copie `keys/private_key.pem` al cliente.
6. Entregue el `.dat` por un medio confiable. El `SuperAdmin` lo importa en `/licencia.html` del cliente y comprueba que `/api/license/status` indique `canOperate: true`.

El `.dat` contiene `payload`, `signature` y `algorithm`. `LicenseSigningService` ordena las propiedades del payload y firma sus bytes UTF-8 con RSA PKCS#1 v1.5 y SHA-256. `POST /api/licenses/verify` comprueba la firma con la clave pública local. Para renovar, `POST /api/licenses/{id}/renew` firma una nueva fecha de vencimiento y sobrescribe el archivo exportado de ese ID; entregue el nuevo `.dat` e impórtelo en el cliente.

**Suspender o revocar en este panel solo cambia el estado en `license-manager.db`** (`Program.cs`, `SetLicenseStatus`). No modifica un `.dat` ya entregado ni avisa al servidor cliente sin conexión. La retirada local en el cliente tampoco revoca criptográficamente copias existentes. No considere esas acciones una revocación inmediata en campo.

## Seguridad y respaldo

- No exponga el emisor a internet o LAN: sus endpoints de generación, renovación, descarga y claves no comprueban identidad. Use `--urls http://127.0.0.1:5118` y una cuenta local de confianza.
- Restrinja el acceso del sistema operativo a `keys/private_key.pem`, la base y `exports/`. La base contiene datos de clientes, Hardware IDs, JSON de licencia y firmas. Cifre los respaldos.
- Mantenga juntos y respaldados el par de claves y la base. Sin la clave privada original no podrá firmar renovaciones compatibles. Sin la base original puede haber colisión de IDs.
- No publique ni adjunte `keys/`, `exports/`, `license-manager.db`, respaldos o archivos `.env` con secretos. `keys/public_key.pem` es público, pero se excluye del repositorio para evitar confundir pares de entornos distintos.

## Solución de problemas

- **Falla al arrancar:** compruebe .NET 8, permiso de escritura en el directorio de trabajo y la ruta SQLite de `ConnectionStrings:DefaultConnection`.
- **No genera licencia:** confirme que existe un cliente y que `keys/private_key.pem` y `keys/public_key.pem` forman un par válido en `/llaves.html`.
- **El cliente rechaza la firma:** compare las claves públicas del emisor y de `Licensing:PublicKeyPem` en `LicenciasCERO`; emita de nuevo solo con la clave privada correspondiente.
- **El cliente indica hardware distinto:** obtenga una solicitud nueva desde el servidor donde se instalará la licencia y emita un `.dat` para ese ID.
- **Una renovación no aparece en el cliente:** descargue el `.dat` actualizado e impórtelo allí; el proceso es manual y sin conexión.
