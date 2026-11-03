using CeroLicenseManager.Data;
using CeroLicenseManager.Models;
using CeroLicenseManager.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=license-manager.db"));
builder.Services.AddScoped<KeyManagementService>();
builder.Services.AddScoped<LicenseSigningService>();
builder.Services.AddScoped<LicenseIdService>();
builder.Services.AddScoped<DashboardService>();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "keys"));
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "exports"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseDefaultFiles();
app.UseStaticFiles();

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
    if (string.IsNullOrWhiteSpace(request.HardwareId)) return Results.BadRequest(new { error = "Hardware ID requerido." });

    var issuedAt = DateOnly.FromDateTime(request.IssuedAt ?? DateTime.Today);
    var expiresAt = DateOnly.FromDateTime(request.ExpiresAt ?? DateTime.Today.AddYears(1));
    var licenseId = await licenseIds.NextIdAsync(issuedAt.Year);
    var features = request.Features.Count > 0 ? request.Features : LicenseDefaults.Features;

    var payload = new LicensePayload(
        licenseId,
        customer.CommercialName,
        LicenseDefaults.ProductName,
        LicenseDefaults.LicenseType,
        issuedAt.ToString("yyyy-MM-dd"),
        expiresAt.ToString("yyyy-MM-dd"),
        request.HardwareId.Trim(),
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

app.MapGet("/api/licenses/{id:int}/download-dat", async (int id, AppDbContext db, LicenseSigningService signing) =>
{
    var license = await db.LicenseRecords.FindAsync(id);
    if (license is null) return Results.NotFound();
    var path = await signing.ExportLicenseDat(license);
    return Results.File(path, "application/octet-stream", "license.dat");
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
