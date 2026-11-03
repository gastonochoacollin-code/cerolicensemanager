using CeroLicenseManager.Data;
using Microsoft.EntityFrameworkCore;

namespace CeroLicenseManager.Services;

public class DashboardService(AppDbContext db)
{
    public async Task<object> GetSummaryAsync()
    {
        var today = DateTime.Today;
        var soon = today.AddDays(30);
        return new
        {
            totalClientes = await db.Customers.CountAsync(),
            licenciasActivas = await db.LicenseRecords.CountAsync(l => l.Status == "ACTIVA" && l.ExpiresAt >= today),
            licenciasPorVencer = await db.LicenseRecords.CountAsync(l => l.Status == "ACTIVA" && l.ExpiresAt >= today && l.ExpiresAt <= soon),
            licenciasVencidas = await db.LicenseRecords.CountAsync(l => l.ExpiresAt < today)
        };
    }
}
