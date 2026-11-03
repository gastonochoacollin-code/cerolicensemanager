using CeroLicenseManager.Data;
using CeroLicenseManager.Models;
using CeroLicenseManager.Services;
using CeroLicenseManager.Endpoints;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();
var paths = AppPaths.Create(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(paths);

var bindAddress = builder.Configuration["Server:BindAddress"] ?? "127.0.0.1";
var port = builder.Configuration.GetValue("Server:Port", 5118);
if (!IPAddress.TryParse(bindAddress, out var address) || port is < 1 or > 65535)
    throw new InvalidOperationException("Server:BindAddress o Server:Port inválido.");
var certificatePath = builder.Configuration["Server:CertificatePath"];
var certificatePasswordFile = builder.Configuration["Server:CertificatePasswordFile"];
if (!IPAddress.IsLoopback(address) &&
    (!builder.Configuration.GetValue<bool>("Server:AllowRemote") || string.IsNullOrWhiteSpace(certificatePath)))
    throw new InvalidOperationException("El acceso remoto requiere Server:AllowRemote y un certificado TLS explícitos.");
if (!IPAddress.IsLoopback(address) && builder.Configuration["AllowedHosts"] is (null or "*"))
    throw new InvalidOperationException("El acceso remoto requiere AllowedHosts explícito.");
if (!string.IsNullOrWhiteSpace(certificatePath) &&
    (string.IsNullOrWhiteSpace(certificatePasswordFile) || !File.Exists(certificatePath) || !File.Exists(certificatePasswordFile)))
    throw new InvalidOperationException("Falta el certificado TLS o su contraseña protegida.");
builder.WebHost.ConfigureKestrel(options => options.Listen(address, port, listen =>
{
    if (!string.IsNullOrWhiteSpace(certificatePath))
        listen.UseHttps(certificatePath, File.ReadAllText(certificatePasswordFile!).Trim());
}));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = paths.DatabasePath }.ConnectionString));
builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 14;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.User.RequireUniqueEmail = false;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "CeroLicenseManager.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.LoginPath = "/login.html";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("CeroLicenseManager")
    .PersistKeysToFileSystem(new DirectoryInfo(paths.DataProtectionPath));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "CeroLicenseManager.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddScoped<KeyManagementService>();
builder.Services.AddScoped<LicenseSigningService>();
builder.Services.AddScoped<LicenseIdService>();
builder.Services.AddScoped<DashboardService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = "Error interno." });
}));
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XFrameOptions = "DENY";
    await next();
});
app.UseRouting();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
        && !path.Equals("/health", StringComparison.OrdinalIgnoreCase)
        && !path.Equals("/login.html", StringComparison.OrdinalIgnoreCase)
        && !path.Equals("/login.js", StringComparison.OrdinalIgnoreCase)
        && !path.Equals("/app.css", StringComparison.OrdinalIgnoreCase)
        && context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.Redirect("/login.html");
        return;
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method))
    {
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>()
                .ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Solicitud inválida." });
            return;
        }
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuthEndpoints();

app.MapGet("/api/customers", async (AppDbContext db) =>
    await db.Customers.OrderBy(c => c.CommercialName).ToListAsync());

app.MapGet("/api/customers/{id:int}", async (int id, AppDbContext db) =>
    await db.Customers.FindAsync(id) is { } customer ? Results.Ok(customer) : Results.NotFound());

app.MapPost("/api/customers", async (Customer customer, AppDbContext db) =>
{
    customer.Id = 0;
    customer.Status = string.IsNullOrWhiteSpace(customer.Status) ? "ACTIVO" : customer.Status.Trim().ToUpperInvariant();
    customer.CreatedAt = DateTime.UtcNow;
    customer.UpdatedAt = DateTime.UtcNow;
    db.Customers.Add(customer);
    await db.SaveChangesAsync();
    return Results.Created($"/api/customers/{customer.Id}", customer);
});

app.MapPut("/api/customers/{id:int}", async (int id, Customer input, AppDbContext db) =>
{
    var customer = await db.Customers.FindAsync(id);
    if (customer is null) return Results.NotFound();

    customer.CommercialName = input.CommercialName;
    customer.LegalName = input.LegalName;
    customer.Rfc = input.Rfc;
    customer.ContactName = input.ContactName;
    customer.Email = input.Email;
    customer.Phone = input.Phone;
    customer.Status = string.IsNullOrWhiteSpace(input.Status) ? customer.Status : input.Status.Trim().ToUpperInvariant();
    customer.Notes = input.Notes;
    customer.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(customer);
});

app.MapPost("/api/customers/{id:int}/activate", async (int id, AppDbContext db) =>
    await SetCustomerStatus(id, "ACTIVO", db));

app.MapPost("/api/customers/{id:int}/suspend", async (int id, AppDbContext db) =>
    await SetCustomerStatus(id, "SUSPENDIDO", db));

app.MapGet("/api/licenses", async (int? customerId, string? status, AppDbContext db) =>
{
    var query = db.LicenseRecords.AsQueryable();
    if (customerId.HasValue) query = query.Where(l => l.CustomerId == customerId.Value);
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(l => l.Status == status.ToUpper());
    return await query.OrderByDescending(l => l.CreatedAt).ToListAsync();
});

app.MapGet("/api/licenses/{id:int}", async (int id, AppDbContext db) =>
    await db.LicenseRecords.FindAsync(id) is { } license ? Results.Ok(license) : Results.NotFound());

app.MapPost("/api/licenses/generate", async (
    LicenseGenerateRequest request,
    AppDbContext db,
    LicenseIdService licenseIds,
    LicenseSigningService signing) =>
{
    var customer = await db.Customers.FindAsync(request.CustomerId);
    if (customer is null) return Results.BadRequest(new { error = "Cliente no encontrado." });
    if (!Regex.IsMatch(request.HardwareId ?? "", "^[0-9A-Fa-f]{64}$"))
        return Results.BadRequest(new { error = "Hardware ID inválido." });
    if (customer.Status != "ACTIVO")
        return Results.BadRequest(new { error = "El cliente debe estar activo." });

    var issuedAt = DateOnly.FromDateTime(request.IssuedAt ?? DateTime.Today);
    var expiresAt = DateOnly.FromDateTime(request.ExpiresAt ?? DateTime.Today.AddYears(1));
    if (expiresAt < issuedAt)
        return Results.BadRequest(new { error = "La fecha de vencimiento debe ser posterior a la emisión." });
    if (new[] { request.MaxActiveEvents ?? 3, request.MaxWorkstations ?? 10, request.MaxDatabases ?? 5 }
        .Any(limit => limit is < 1 or > 10000))
        return Results.BadRequest(new { error = "Los límites deben estar entre 1 y 10000." });
    var licenseId = await licenseIds.NextIdAsync(issuedAt.Year);
    var features = request.Features.Count > 0 ? request.Features : LicenseDefaults.Features;

    var payload = new LicensePayload(
        licenseId,
        customer.CommercialName,
        LicenseDefaults.ProductName,
        LicenseDefaults.LicenseType,
        issuedAt.ToString("yyyy-MM-dd"),
        expiresAt.ToString("yyyy-MM-dd"),
        request.HardwareId!.Trim(),
        request.MaxActiveEvents ?? 3,
        request.MaxWorkstations ?? 10,
        request.MaxDatabases ?? 5,
        features,
        "ACTIVA",
        request.Notes ?? "Licencia anual offline no transferible");

    var signature = signing.SignPayload(payload);
    var licenseJson = signing.BuildLicenseJson(payload, signature);

    var record = new LicenseRecord
    {
        LicenseId = licenseId,
        CustomerId = customer.Id,
        CustomerName = customer.CommercialName,
        ProductName = payload.ProductName,
        LicenseType = payload.LicenseType,
        IssuedAt = issuedAt.ToDateTime(TimeOnly.MinValue),
        ExpiresAt = expiresAt.ToDateTime(TimeOnly.MinValue),
        HardwareId = payload.HardwareId,
        MaxActiveEvents = payload.MaxActiveEvents,
        MaxWorkstations = payload.MaxWorkstations,
        MaxDatabases = payload.MaxDatabases,
        FeaturesJson = JsonSerializer.Serialize(features),
        Status = payload.Status,
        Notes = payload.Notes,
        Signature = signature,
        LicenseJson = licenseJson,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    db.LicenseRecords.Add(record);
    await db.SaveChangesAsync();
    await signing.ExportLicenseDat(record);
    db.LicenseGenerationLogs.Add(new LicenseGenerationLog
    {
        LicenseRecordId = record.Id,
        EventType = "GENERATED",
        Message = $"Licencia {licenseId} generada para {customer.CommercialName}.",
        CreatedAt = DateTime.UtcNow,
        MetadataJson = JsonSerializer.Serialize(new { record.HardwareId, record.ExpiresAt })
    });
    await db.SaveChangesAsync();
    return Results.Created($"/api/licenses/{record.Id}", record);
});

app.MapPost("/api/licenses/{id:int}/renew", async (int id, LicenseRenewRequest request, AppDbContext db, LicenseSigningService signing) =>
{
    var existing = await db.LicenseRecords.FindAsync(id);
    if (existing is null) return Results.NotFound();
    if (request.ExpiresAt.HasValue && request.ExpiresAt.Value.Date < DateTime.Today)
        return Results.BadRequest(new { error = "La nueva fecha debe ser vigente." });

    var payload = signing.GetPayload(existing.LicenseJson) with
    {
        IssuedAt = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd"),
        ExpiresAt = DateOnly.FromDateTime(request.ExpiresAt ?? DateTime.Today.AddYears(1)).ToString("yyyy-MM-dd"),
        Status = "ACTIVA"
    };
    existing.IssuedAt = DateTime.Today;
    existing.ExpiresAt = DateTime.Parse(payload.ExpiresAt);
    existing.Status = "ACTIVA";
    existing.Signature = signing.SignPayload(payload);
    existing.LicenseJson = signing.BuildLicenseJson(payload, existing.Signature);
    existing.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    await signing.ExportLicenseDat(existing);
    return Results.Ok(existing);
});

app.MapPost("/api/licenses/{id:int}/suspend", async (int id, AppDbContext db) => await SetLicenseStatus(id, "SUSPENDIDA", db));
app.MapPost("/api/licenses/{id:int}/revoke", async (int id, AppDbContext db) => await SetLicenseStatus(id, "REVOCADA", db));

app.MapGet("/api/licenses/{id:int}/download-dat", async (int id, AppDbContext db) =>
{
    var license = await db.LicenseRecords.FindAsync(id);
    if (license is null) return Results.NotFound();
    return Results.File(System.Text.Encoding.UTF8.GetBytes(license.LicenseJson), "application/octet-stream", "license.dat");
});

app.MapGet("/api/licenses/{id:int}/download-json", async (int id, AppDbContext db) =>
{
    var license = await db.LicenseRecords.FindAsync(id);
    return license is null
        ? Results.NotFound()
        : Results.Text(license.LicenseJson, "application/json");
});

app.MapPost("/api/licenses/verify", async (JsonElement body, LicenseSigningService signing) =>
{
    var licenseJson = body.ValueKind == JsonValueKind.String ? body.GetString() ?? "" : body.GetRawText();
    var result = await signing.VerifyLicense(licenseJson);
    return Results.Ok(result);
});

app.MapGet("/api/keys/status", (KeyManagementService keys) => Results.Ok(keys.GetStatus()));
app.MapPost("/api/keys/generate", (KeyManagementService keys) => Results.Ok(new { generated = keys.GenerateKeys(), exists = keys.KeysExist() }));
app.MapGet("/api/keys/public", (KeyManagementService keys) =>
    keys.KeysExist()
        ? Results.File(System.Text.Encoding.UTF8.GetBytes(keys.GetPublicKey()), "application/x-pem-file", "public_key.pem")
        : Results.NotFound(new { error = "No existen llaves." }));

app.MapGet("/api/dashboard/summary", async (DashboardService dashboard) => Results.Ok(await dashboard.GetSummaryAsync()));

app.MapFallbackToFile("index.html");

app.Run();

static async Task<IResult> SetCustomerStatus(int id, string status, AppDbContext db)
{
    var customer = await db.Customers.FindAsync(id);
    if (customer is null) return Results.NotFound();
    customer.Status = status;
    customer.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(customer);
}

static async Task<IResult> SetLicenseStatus(int id, string status, AppDbContext db)
{
    var license = await db.LicenseRecords.FindAsync(id);
    if (license is null) return Results.NotFound();
    license.Status = status;
    license.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();
    return Results.Ok(license);
}
