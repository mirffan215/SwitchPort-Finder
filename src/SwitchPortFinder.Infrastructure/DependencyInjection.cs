using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Import;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Infrastructure.Discovery;
using SwitchPortFinder.Infrastructure.Persistence;
using SwitchPortFinder.Infrastructure.Security;

namespace SwitchPortFinder.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "SqlServer";
        var conn = config.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        services.AddDbContext<AppDbContext>(o =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)) o.UseSqlite(conn);   // sandbox/tests only
            else o.UseSqlServer(conn, s => s.EnableRetryOnFailure());
        });

        var keysPath = config["Security:KeysPath"] ?? Path.Combine(AppContext.BaseDirectory, "keys");
        Directory.CreateDirectory(keysPath);
        var dp = services.AddDataProtection().SetApplicationName("SwitchPortFinder").PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        if (OperatingSystem.IsWindows()) dp.ProtectKeysWithDpapi(protectToLocalMachine: true);

        services.AddMemoryCache();
        services.AddSingleton<ICredentialProtector, DataProtectionCredentialProtector>();
        services.AddSingleton<IUserAuthorizationService, AllowAllAuthorizationService>();
        services.AddSingleton<IImportPreviewStore, ImportPreviewStore>();
        services.AddScoped<ISwitchRepository, SwitchRepository>();
        services.AddScoped<ICredentialProfileRepository, CredentialProfileRepository>();
        services.AddScoped<IImportBatchRepository, ImportBatchRepository>();
        services.AddSingleton<CredentialCodec>();
        services.AddScoped<SnmpConnectionService>();
        services.AddScoped<SwitchService>();
        services.AddScoped<CredentialProfileService>();
        services.AddScoped<SwitchImportService>();
        services.AddSingleton<ScanQueue>();
        services.AddSingleton<IScanQueue>(sp => sp.GetRequiredService<ScanQueue>());
        services.AddSingleton<IHostnameResolver, DnsHostnameResolver>();
        services.AddSingleton<SettingsService>();
        services.AddScoped<SwitchPortFinder.Infrastructure.Security.UserService>();
        services.AddScoped<SwitchPortFinder.Infrastructure.Security.MfaService>();
        services.AddScoped<ClassificationService>();
        services.AddSingleton<SwitchPortFinder.Infrastructure.Notifications.MailSender>();
        services.AddScoped<SwitchPortFinder.Infrastructure.Notifications.AlertService>();
        services.AddScoped<SwitchPortFinder.Infrastructure.Reports.ReportEngine>();
        services.AddScoped<SwitchPortFinder.Infrastructure.Reports.ReportService>();
        services.AddScoped<SwitchPortFinder.Infrastructure.Reports.SwitchNameLookup>();
        services.AddScoped<ManualDeviceService>();
        services.AddScoped<DeviceCategoryService>();
        services.AddScoped<AccessPointService>();
        services.AddScoped<ScanResultWriter>();
        services.AddScoped<DiscoveryJobRunner>();
        services.AddScoped<DiscoveryService>();
        services.AddScoped<PortQueryService>();
        services.AddScoped<DeviceQueryService>();
        services.AddScoped<SearchService>();
        services.AddScoped<TraceService>();
        services.AddScoped<TopologyService>();
        services.AddScoped<DashboardService>();
        services.AddHostedService<DiscoveryWorker>();
        services.AddHostedService<VendorBackfillService>();
        services.AddHostedService<HealthPollService>();
        services.AddHostedService<SwitchPortFinder.Infrastructure.Notifications.AlertWorker>();
        return services;
    }
}
