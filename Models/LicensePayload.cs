using System.Text.Json.Serialization;

namespace CeroLicenseManager.Models;

public record LicensePayload(
    [property: JsonPropertyName("licenseId")] string LicenseId,
    [property: JsonPropertyName("customerName")] string CustomerName,
    [property: JsonPropertyName("productName")] string ProductName,
    [property: JsonPropertyName("licenseType")] string LicenseType,
    [property: JsonPropertyName("issuedAt")] string IssuedAt,
    [property: JsonPropertyName("expiresAt")] string ExpiresAt,
    [property: JsonPropertyName("hardwareId")] string HardwareId,
    [property: JsonPropertyName("maxActiveEvents")] int MaxActiveEvents,
    [property: JsonPropertyName("maxWorkstations")] int MaxWorkstations,
    [property: JsonPropertyName("maxDatabases")] int MaxDatabases,
    [property: JsonPropertyName("features")] IReadOnlyList<string> Features,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("notes")] string Notes);
