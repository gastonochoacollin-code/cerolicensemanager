namespace CeroLicenseManager.Models;

public class Customer
{
    public int Id { get; set; }
    public string CommercialName { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string Rfc { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Status { get; set; } = "ACTIVO";
    public string Notes { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
