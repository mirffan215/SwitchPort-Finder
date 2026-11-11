using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SwitchPortFinder.Infrastructure.Notifications;

/// <summary>Evaluates the alert rules once a minute (after the health poll has had time to write new readings).</summary>
public sealed class AlertWorker(IServiceScopeFactory scopes, Microsoft.Extensions.Configuration.IConfiguration cfg, ILogger<AlertWorker> log) : BackgroundService
{
    public static TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue(cfg, "Notifications:Worker", true)) return;   // tests drive the evaluator directly
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(40), stop);
            try { using var s = scopes.CreateScope(); await s.ServiceProvider.GetRequiredService<AlertService>().EnsureDefaultRulesAsync(stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Default alert rules could not be created (database not ready?)"); }
            while (!stop.IsCancellationRequested)
            {
                try { using var s = scopes.CreateScope(); var n = await s.ServiceProvider.GetRequiredService<AlertService>().EvaluateAsync(stop); if (n > 0) log.LogWarning("AlertsOpened: {Count} new alert(s)", n); }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Alert evaluation failed"); }
                await Task.Delay(Interval, stop);
            }
        }
        catch (OperationCanceledException) { }
    }
}
