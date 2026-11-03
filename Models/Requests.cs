namespace CeroLicenseManager.Models;

public class LicenseGenerateRequest
{
    public int CustomerId { get; set; }
    public string HardwareId { get; set; } = "";
    public DateTime? IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxActiveEvents { get; set; }
    public int? MaxWorkstations { get; set; }
    public int? MaxDatabases { get; set; }
    public List<string> Features { get; set; } = [];
    public string Notes { get; set; } = "";
}

public class LicenseRenewRequest
{
    public DateTime? ExpiresAt { get; set; }
}

public static class LicenseDefaults
{
    public const string ProductName = "Cero Cashless";
    public const string LicenseType = "ANUAL_OFFLINE";
    public static readonly List<string> Features =
    [
        "OFFLINE_OPERATION",
        "NFC_CASHLESS",
        "REPORTS",
        "LOCAL_SERVER",
        "MULTI_EVENT",
        "AUDIT_LOGS"
    ];
}
