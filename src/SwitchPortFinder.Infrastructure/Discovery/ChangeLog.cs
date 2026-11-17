using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>Records the few events the dashboard feed cannot work out afterwards, because nothing else keeps the "before" value.</summary>
public static class ChangeLog
{
    /// <summary>The 32-bit uptime counter wraps to 0 after about 497 days: that is not a restart.</summary>
    private const long WrapGuardSeconds = 480L * 86400;

    public static string Span(long seconds) => seconds >= 86400 ? $"{seconds / 86400} d {seconds % 86400 / 3600} h" : seconds >= 3600 ? $"{seconds / 3600} h {seconds % 3600 / 60} min" : $"{Math.Max(1, seconds / 60)} min";

    /// <summary>Call BEFORE the new uptime is stored on the switch: a clear drop means the switch restarted.</summary>
    public static void NoteUptime(AppDbContext db, Switch sw, long? newUptime, DateTime now)
    {
        if (newUptime is not { } up || sw.UptimeSeconds is not { } prev) return;
        if (prev > up + 120 && prev < WrapGuardSeconds)
            db.NetworkEvents.Add(new NetworkEvent { At = now.AddSeconds(-up), Kind = "reboot", SwitchId = sw.Id, Title = $"{sw.Name} restarted", Detail = $"it had been up {Span(prev)}; up again for {Span(up)}" });
    }
}
