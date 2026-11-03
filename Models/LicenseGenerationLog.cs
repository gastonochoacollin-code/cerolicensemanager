namespace CeroLicenseManager.Models;

public class LicenseGenerationLog
{
    public int Id { get; set; }
    public int LicenseRecordId { get; set; }
    public string EventType { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string MetadataJson { get; set; } = "{}";
}
