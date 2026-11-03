using CeroLicenseManager.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CeroLicenseManager.Services;

public class LicenseSigningService(KeyManagementService keys, AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string SignPayload(object payload)
    {
        if (!keys.KeysExist()) throw new InvalidOperationException("Primero genera el par de llaves.");
        var canonicalJson = Canonicalize(payload);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(keys.GetPrivateKeyPath()));
        var signature = rsa.SignData(Encoding.UTF8.GetBytes(canonicalJson), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    public async Task<object> VerifyLicense(string licenseJson)
    {
        if (!keys.KeysExist()) return new { valid = false, error = "No existen llaves." };

        try
        {
            using var doc = JsonDocument.Parse(licenseJson);
            var root = doc.RootElement;
            var payload = root.GetProperty("payload");
            var signature = root.GetProperty("signature").GetString() ?? "";
            var algorithm = root.GetProperty("algorithm").GetString() ?? "";
            if (algorithm != "RSA-SHA256") return new { valid = false, error = "Algoritmo no soportado." };

            var canonicalJson = Canonicalize(payload);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(keys.GetPublicKey());
            var isValid = rsa.VerifyData(
                Encoding.UTF8.GetBytes(canonicalJson),
                Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            return new { valid = isValid, algorithm };
        }
        catch
        {
            await Task.CompletedTask;
            return new { valid = false, error = "Licencia inválida." };
        }
    }

    public LicensePayload GetPayload(string licenseJson)
    {
        using var doc = JsonDocument.Parse(licenseJson);
        return doc.RootElement.GetProperty("payload").Deserialize<LicensePayload>()!;
    }

    public string BuildLicenseJson(LicensePayload payload, string signature)
    {
        var envelope = new { payload, signature, algorithm = "RSA-SHA256" };
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public async Task<string> ExportLicenseDat(LicenseRecord record)
    {
        var exportPath = paths.ExportsPath;
        Directory.CreateDirectory(exportPath);
        var filePath = Path.Combine(exportPath, $"{record.LicenseId}.license.dat");
        await File.WriteAllTextAsync(filePath, record.LicenseJson, Encoding.UTF8);
        return filePath;
    }

    private static string Canonicalize(object value)
    {
        using var doc = JsonSerializer.SerializeToDocument(value);
        return Canonicalize(doc.RootElement);
    }

    private static string Canonicalize(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            WriteCanonical(element, writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
        }
    }
}
