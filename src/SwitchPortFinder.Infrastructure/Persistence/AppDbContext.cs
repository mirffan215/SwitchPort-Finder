using Microsoft.EntityFrameworkCore;
using SwitchPortFinder.Domain.Entities;

namespace SwitchPortFinder.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Switch> Switches => Set<Switch>();
    public DbSet<SwitchCredential> SwitchCredentials => Set<SwitchCredential>();
    public DbSet<CredentialProfile> CredentialProfiles => Set<CredentialProfile>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Switch>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ManagementIp).HasMaxLength(45).IsRequired();
            e.Property(x => x.ManufacturerOther).HasMaxLength(100);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.SysObjectId).HasMaxLength(200);
            e.Property(x => x.SysName).HasMaxLength(255);
            e.Property(x => x.SysDescr).HasMaxLength(1000);
            e.HasIndex(x => x.ManagementIp).IsUnique();
            e.HasIndex(x => x.Name);
            e.HasOne(x => x.Credential).WithOne(x => x.Switch).HasForeignKey<SwitchCredential>(x => x.SwitchId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CredentialProfile).WithMany().HasForeignKey(x => x.CredentialProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ImportBatch>().WithMany().HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<SwitchCredential>(e =>
        {
            e.Property(x => x.ContextName).HasMaxLength(100);
            e.Property(x => x.EncryptedSecrets).IsRequired();
        });
        b.Entity<CredentialProfile>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.ContextName).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
        });
        b.Entity<ImportBatch>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.ImportedBy).HasMaxLength(100).IsRequired();
        });
    }
}
