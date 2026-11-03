using CeroLicenseManager.Models;
using Microsoft.EntityFrameworkCore;

namespace CeroLicenseManager.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<LicenseRecord> LicenseRecords => Set<LicenseRecord>();
    public DbSet<LicenseGenerationLog> LicenseGenerationLogs => Set<LicenseGenerationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().Property(c => c.CommercialName).HasMaxLength(200);
        modelBuilder.Entity<Customer>().Property(c => c.Status).HasMaxLength(40);
        modelBuilder.Entity<LicenseRecord>().HasIndex(l => l.LicenseId).IsUnique();
        modelBuilder.Entity<LicenseRecord>().Property(l => l.LicenseId).HasMaxLength(40);
        modelBuilder.Entity<LicenseRecord>().Property(l => l.Status).HasMaxLength(40);
    }
}
