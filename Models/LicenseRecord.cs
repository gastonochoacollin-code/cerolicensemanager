namespace CeroLicenseManager.Models;

public class LicenseRecord
{
    public int Id { get; set; }
    public string LicenseId { get; set; } = "";
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string LicenseType { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string HardwareId { get; set; } = "";
    public int MaxActiveEvents { get; set; }
    public int MaxWorkstations { get; set; }
    public int MaxDatabases { get; set; }
    public string FeaturesJson { get; set; } = "[]";
    public string Status { get; set; } = "ACTIVA";
    public string Notes { get; set; } = "";
    public string Signature { get; set; } = "";
    public string LicenseJson { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
