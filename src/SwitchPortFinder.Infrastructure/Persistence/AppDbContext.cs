using Microsoft.EntityFrameworkCore;
using SwitchPortFinder.Domain.Entities;

namespace SwitchPortFinder.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Switch> Switches => Set<Switch>();
    public DbSet<SwitchMetricSample> SwitchMetrics => Set<SwitchMetricSample>();
    public DbSet<PortMetricSample> PortMetrics => Set<PortMetricSample>();
    public DbSet<ReportDefinition> Reports => Set<ReportDefinition>();
    public DbSet<NotificationConfig> NotificationConfigs => Set<NotificationConfig>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
    public DbSet<NotificationLogEntry> NotificationLog => Set<NotificationLogEntry>();
    public DbSet<SwitchCredential> SwitchCredentials => Set<SwitchCredential>();
    public DbSet<CredentialProfile> CredentialProfiles => Set<CredentialProfile>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<SwitchPort> SwitchPorts => Set<SwitchPort>();
    public DbSet<DeviceObservation> Observations => Set<DeviceObservation>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<ArpEntry> ArpEntries => Set<ArpEntry>();
    public DbSet<DiscoveryJob> DiscoveryJobs => Set<DiscoveryJob>();
    public DbSet<DiscoveryError> DiscoveryErrors => Set<DiscoveryError>();
    public DbSet<SwitchNeighbor> Neighbors => Set<SwitchNeighbor>();
    public DbSet<SwitchMac> SwitchMacs => Set<SwitchMac>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SwitchMetricSample>(e => { e.HasOne(x => x.Switch).WithMany().HasForeignKey(x => x.SwitchId).OnDelete(DeleteBehavior.Cascade); e.HasIndex(x => new { x.SwitchId, x.At }); e.HasIndex(x => x.At); });
        // No foreign key to the port: SQL Server forbids a second cascade path (switch -> port and switch -> sample). Samples are removed with the switch by the retention job.
        b.Entity<PortMetricSample>(e => { e.HasIndex(x => new { x.PortId, x.At }); e.HasIndex(x => x.At); e.HasIndex(x => new { x.SwitchId, x.At }); e.Ignore(x => x.Port); });
        b.Entity<NotificationConfig>(e => { e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); e.Property(x => x.Host).HasMaxLength(255); e.Property(x => x.Username).HasMaxLength(255); e.Property(x => x.FromAddress).HasMaxLength(255); e.Property(x => x.FromName).HasMaxLength(100); e.Property(x => x.Recipients).HasMaxLength(2000); e.Property(x => x.PublicBaseUrl).HasMaxLength(255); });
        b.Entity<CategoryRule>(e => { e.Property(x => x.Name).HasMaxLength(150).IsRequired(); e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20); e.Property(x => x.VendorContains).HasMaxLength(100); e.Property(x => x.HostnameStartsWith).HasMaxLength(100); e.Property(x => x.HostnameContains).HasMaxLength(100); e.Property(x => x.PortDescriptionContains).HasMaxLength(100); e.Property(x => x.NeighborContains).HasMaxLength(100); e.Property(x => x.NeighborCapability).HasMaxLength(20); });
        b.Entity<AlertRule>(e => { e.Property(x => x.Name).HasMaxLength(150).IsRequired(); e.HasIndex(x => x.Type); });
        b.Entity<AlertEvent>(e => { e.HasOne(x => x.Rule).WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade); e.Property(x => x.Subject).HasMaxLength(60).IsRequired(); e.Property(x => x.Title).HasMaxLength(300); e.Property(x => x.Detail).HasMaxLength(1000); e.HasIndex(x => new { x.RuleId, x.Subject, x.State }); e.HasIndex(x => x.State); });
        b.Entity<NotificationLogEntry>(e => { e.Property(x => x.Recipients).HasMaxLength(2000); e.Property(x => x.Subject).HasMaxLength(300); e.Property(x => x.Error).HasMaxLength(500); e.HasIndex(x => x.At); });
        b.Entity<ReportDefinition>(e => { e.Property(x => x.Name).HasMaxLength(150).IsRequired(); e.Property(x => x.Json).IsRequired(); e.Property(x => x.CreatedBy).HasMaxLength(100); e.HasIndex(x => x.Name); });
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
            e.Property(x => x.SerialNumber).HasMaxLength(100);
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
        b.Entity<SwitchPort>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Descr).HasMaxLength(255);
            e.Property(x => x.Alias).HasMaxLength(255);
            e.Property(x => x.PhysAddress).HasMaxLength(12).IsFixedLength().IsUnicode(false);
            e.HasIndex(x => new { x.SwitchId, x.IfIndex }).IsUnique();
            e.HasIndex(x => x.Name);
            e.HasOne(x => x.Switch).WithMany().HasForeignKey(x => x.SwitchId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SwitchNeighbor>(e =>
        {
            e.Property(x => x.RemoteKey).HasMaxLength(300).IsRequired();
            e.Property(x => x.ChassisId).HasMaxLength(255);
            e.Property(x => x.ChassisMac).HasMaxLength(12).IsFixedLength().IsUnicode(false);
            e.Property(x => x.RemotePortId).HasMaxLength(255);
            e.Property(x => x.RemotePortDescr).HasMaxLength(255);
            e.Property(x => x.SysName).HasMaxLength(255);
            e.Property(x => x.SysDescr).HasMaxLength(500);
            e.Property(x => x.Platform).HasMaxLength(255);
            e.Property(x => x.MgmtIp).HasMaxLength(45);
            e.Property(x => x.Evidence).HasMaxLength(500);
            e.HasIndex(x => new { x.PortId, x.Protocol, x.RemoteKey }).IsUnique();
            e.HasIndex(x => x.SwitchId);
            e.HasIndex(x => x.MatchedSwitchId);
            e.HasOne(x => x.Port).WithMany().HasForeignKey(x => x.PortId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Switch>().WithMany().HasForeignKey(x => x.SwitchId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(100).IsRequired();
            e.Property(x => x.UsernameNormalized).HasMaxLength(100).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(300).IsRequired();
            e.HasIndex(x => x.UsernameNormalized).IsUnique();
        });
        b.Entity<AppSetting>(e => { e.HasKey(x => x.Key); e.Property(x => x.Key).HasMaxLength(100); e.Property(x => x.Value).IsRequired(); });
        b.Entity<SwitchMac>(e =>
        {
            e.Property(x => x.Mac).HasMaxLength(12).IsFixedLength().IsUnicode(false).IsRequired();
            e.HasIndex(x => new { x.SwitchId, x.Mac }).IsUnique();
            e.HasIndex(x => x.Mac);
            e.HasOne<Switch>().WithMany().HasForeignKey(x => x.SwitchId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DeviceObservation>(e =>
        {
            e.Property(x => x.Mac).HasMaxLength(12).IsFixedLength().IsUnicode(false).IsRequired();
            e.HasIndex(x => new { x.SwitchId, x.Mac }).IsUnique();
            e.HasIndex(x => x.Mac);
            e.HasIndex(x => x.PortId);
            e.HasIndex(x => x.LastSeen);
            e.HasOne(x => x.Port).WithMany().HasForeignKey(x => x.PortId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Switch>().WithMany().HasForeignKey(x => x.SwitchId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<Device>(e =>
        {
            e.Property(x => x.Mac).HasMaxLength(12).IsFixedLength().IsUnicode(false).IsRequired();
            e.Property(x => x.Hostname).HasMaxLength(255).IsRequired();
            e.Property(x => x.Vendor).HasMaxLength(100);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20); e.Property(x => x.CategoryReason).HasMaxLength(200); e.HasIndex(x => x.Category);
            e.Property(x => x.Owner).HasMaxLength(100); e.Property(x => x.Notes).HasMaxLength(1000); e.Property(x => x.ManualUpdatedBy).HasMaxLength(100);
            e.Property(x => x.IpAddress).HasMaxLength(45);
            e.HasIndex(x => x.Mac).IsUnique();
            e.HasIndex(x => x.IpAddress);
            e.HasIndex(x => x.Hostname);
            e.HasIndex(x => x.LastSeen);
            e.HasIndex(x => x.SwitchId);
            e.HasIndex(x => x.PortId);
            e.HasIndex(x => new { x.SwitchId, x.LastSeen });
            e.HasOne(x => x.Switch).WithMany().HasForeignKey(x => x.SwitchId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Port).WithMany().HasForeignKey(x => x.PortId).OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ArpEntry>(e =>
        {
            e.Property(x => x.Ip).HasMaxLength(45).IsRequired();
            e.Property(x => x.Mac).HasMaxLength(12).IsFixedLength().IsUnicode(false).IsRequired();
            e.HasIndex(x => new { x.SourceSwitchId, x.Ip }).IsUnique();
            e.HasIndex(x => x.Mac);
            e.HasOne<Switch>().WithMany().HasForeignKey(x => x.SourceSwitchId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DiscoveryJob>(e =>
        {
            e.Property(x => x.Summary).HasMaxLength(500);
            e.HasIndex(x => x.QueuedAt);
            e.HasIndex(x => new { x.SwitchId, x.Status });
            e.HasOne(x => x.Switch).WithMany().HasForeignKey(x => x.SwitchId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Errors).WithOne().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DiscoveryError>(e =>
        {
            e.Property(x => x.Step).HasMaxLength(100);
            e.Property(x => x.Code).HasMaxLength(100);
            e.Property(x => x.Message).HasMaxLength(1000);
        });
        b.Entity<ImportBatch>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.ImportedBy).HasMaxLength(100).IsRequired();
        });
    }
}
