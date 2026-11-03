using CeroLicenseManager.Data;
using Microsoft.EntityFrameworkCore;

namespace CeroLicenseManager.Services;

public class LicenseIdService(AppDbContext db)
{
    public async Task<string> NextIdAsync(int year)
    {
        var prefix = $"CE-{year}-";
        var last = await db.LicenseRecords
            .Where(l => l.LicenseId.StartsWith(prefix))
            .OrderByDescending(l => l.LicenseId)
            .Select(l => l.LicenseId)
            .FirstOrDefaultAsync();

        var next = 1;
        if (!string.IsNullOrWhiteSpace(last) && int.TryParse(last.Split('-').Last(), out var current))
        {
            next = current + 1;
        }

        return $"{prefix}{next:0000}";
    }
}
