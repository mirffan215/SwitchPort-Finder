using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MimeKit;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Notifications;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Notifications;

/// <summary>Mail-server settings, alert rules, the evaluator that turns the health/traffic history into alerts, and the batched e-mails.</summary>
public class AlertService(AppDbContext db, MailSender mail, ICredentialProtector protector, ILogger<AlertService> log)
{
    private const int MaxNewPerCycle = 200;
    private static readonly string[] Percent = ["UplinkUtilization", "PortUtilization", "HighCpu", "HighMemory"];

    // ------------------------------------------------------------------ configuration
    private async Task<NotificationConfig> ConfigAsync(CancellationToken ct) => await db.NotificationConfigs.FirstOrDefaultAsync(ct) ?? new NotificationConfig();

    private static NotificationConfigDto ToDto(NotificationConfig c) => new(c.Enabled, c.Host, c.Port, c.Security, c.Username, !string.IsNullOrEmpty(c.EncryptedPassword), c.FromAddress, c.FromName,
        MailSender.ParseRecipients(c.Recipients).Select(r => r.Address).ToList(), c.TrustServerCertificate, c.PublicBaseUrl);

    public async Task<NotificationConfigDto> GetConfigAsync(CancellationToken ct) => ToDto(await ConfigAsync(ct));

    public async Task<NotificationConfigDto> UpdateConfigAsync(NotificationConfigRequest r, string actor, CancellationToken ct)
    {
        var host = r.Host?.Trim() ?? "";
        if (host.Length > 255 || host.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) throw new ValidationFailedException("host", "Enter a host name or IP address without spaces.");
        if (r.Port is < 1 or > 65535) throw new ValidationFailedException("port", "The port must be between 1 and 65535.");
        if (!Enum.IsDefined(r.Security)) throw new ValidationFailedException("security", "Unknown security mode.");
        if (!MailSender.TryAddress(r.FromAddress, out var from)) throw new ValidationFailedException("fromAddress", "Enter a valid From address.");
        var rcpt = (r.Recipients ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (rcpt.Count > 20 || rcpt.Any(x => !MailSender.TryAddress(x, out _))) throw new ValidationFailedException("recipients", "Up to 20 valid e-mail addresses.");
        string? url = null;
        if (!string.IsNullOrWhiteSpace(r.PublicBaseUrl))
        {
            if (!Uri.TryCreate(r.PublicBaseUrl.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) throw new ValidationFailedException("publicBaseUrl", "Enter a full address such as https://switchport.example.com:8450");
            url = u.GetLeftPart(UriPartial.Authority);
        }
        var c = await db.NotificationConfigs.FirstOrDefaultAsync(ct);
        if (c is null) db.NotificationConfigs.Add(c = new NotificationConfig());
        var hasPassword = r.Password is null ? !string.IsNullOrEmpty(c.EncryptedPassword) : r.Password.Length > 0;
        if (r.Security == SmtpSecurity.None && (hasPassword || !string.IsNullOrWhiteSpace(r.Username))) throw new ValidationFailedException("security", "A user name and password are never sent over an unencrypted connection. Choose STARTTLS or SSL/TLS, or remove the credentials (IP-restricted internal relay).");
        c.Enabled = r.Enabled; c.Host = host; c.Port = r.Port; c.Security = r.Security; c.Username = string.IsNullOrWhiteSpace(r.Username) ? null : r.Username.Trim();
        if (r.Password is not null) c.EncryptedPassword = r.Password.Length == 0 ? null : protector.Protect(r.Password);
        c.FromAddress = from.Address; c.FromName = string.IsNullOrWhiteSpace(r.FromName) ? "SwitchPort Finder" : r.FromName.Trim()[..Math.Min(100, r.FromName.Trim().Length)];
        c.Recipients = string.Join("; ", rcpt); c.TrustServerCertificate = r.TrustServerCertificate; c.PublicBaseUrl = url; c.UpdatedAt = DateTime.UtcNow;
        if (r.Enabled && MailSender.ConfigProblem(c) is { } p) throw new ValidationFailedException("host", p);
        await db.SaveChangesAsync(ct);
        log.LogWarning("NotificationConfigChanged: mail server settings updated by {Actor} (enabled: {Enabled})", actor, c.Enabled);
        return ToDto(c);
    }

    public async Task<TestMailResult> SendTestAsync(string? to, CancellationToken ct)
    {
        var c = await ConfigAsync(ct);
        var rcpt = MailSender.ParseRecipients(string.IsNullOrWhiteSpace(to) ? c.Recipients : to);
        if (rcpt.Count == 0) return new TestMailResult(false, "Add at least one recipient (or enter an address to send the test to).");
        var res = await mail.SendAsync(c, rcpt, new MailMessageData("[SwitchPort Finder] Test message", "This is a test message from SwitchPort Finder.\nIf you can read this, e-mail notifications are working.", "<p>This is a test message from <b>SwitchPort Finder</b>.</p><p>If you can read this, e-mail notifications are working.</p>"), ct);
        await LogAsync(string.Join("; ", rcpt.Select(r => r.Address)), "Test message", res, ct);
        return new TestMailResult(res.Success, res.Success ? $"Test message sent to {string.Join(", ", rcpt.Select(r => r.Address))}." : res.Message);
    }

    private async Task LogAsync(string to, string subject, MailResult r, CancellationToken ct)
    {
        db.NotificationLog.Add(new NotificationLogEntry { At = DateTime.UtcNow, Recipients = to.Length > 2000 ? to[..2000] : to, Subject = subject.Length > 300 ? subject[..300] : subject, Success = r.Success, Error = r.Success ? null : r.Message[..Math.Min(500, r.Message.Length)] });
        await db.SaveChangesAsync(ct);
        var old = await db.NotificationLog.OrderByDescending(x => x.At).Skip(500).Select(x => x.Id).ToListAsync(ct);   // keep the last 500
        if (old.Count > 0) await db.NotificationLog.Where(x => old.Contains(x.Id)).ExecuteDeleteAsync(ct);
    }

    public async Task<List<NotificationLogDto>> LogAsync(CancellationToken ct) => (await db.NotificationLog.AsNoTracking().OrderByDescending(x => x.At).Take(50).ToListAsync(ct)).Select(x => new NotificationLogDto(x.At, x.Recipients, x.Subject, x.Success, x.Error)).ToList();

    // ------------------------------------------------------------------ rules
    public async Task EnsureDefaultRulesAsync(CancellationToken ct)
    {
        if (await db.AlertRules.AnyAsync(ct)) return;
        AlertRule R(string n, AlertRuleType t, double th, int dur, int cool = 240) => new() { Name = n, Type = t, Threshold = th, DurationMinutes = dur, CooldownMinutes = cool, CreatedAt = DateTime.UtcNow };
        db.AlertRules.AddRange(R("Uplink utilization above 80%", AlertRuleType.UplinkUtilization, 80, 10), R("Switch unreachable", AlertRuleType.SwitchUnreachable, 0, 10, 60), R("CPU above 90%", AlertRuleType.HighCpu, 90, 10),
            R("Memory above 90%", AlertRuleType.HighMemory, 90, 10), R("Packet drops above 5/s", AlertRuleType.PacketDrops, 5, 10), R("Switch restarted", AlertRuleType.SwitchRestarted, 0, 15, 0));
        await db.SaveChangesAsync(ct);
    }

    private async Task<AlertRuleDto> ToDto(AlertRule r, CancellationToken ct) => new(r.Id, r.Name, r.Type, r.Enabled, r.Threshold, r.DurationMinutes, r.SwitchId, r.SwitchId is { } s ? await db.Switches.Where(x => x.Id == s).Select(x => x.Name).FirstOrDefaultAsync(ct) : null, r.CooldownMinutes, r.NotifyOnResolve);

    public async Task<List<AlertRuleDto>> RulesAsync(CancellationToken ct)
    {
        var names = await db.Switches.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return (await db.AlertRules.AsNoTracking().OrderBy(r => r.Type).ThenBy(r => r.Name).ToListAsync(ct)).Select(r => new AlertRuleDto(r.Id, r.Name, r.Type, r.Enabled, r.Threshold, r.DurationMinutes, r.SwitchId, r.SwitchId is { } s ? names.GetValueOrDefault(s) : null, r.CooldownMinutes, r.NotifyOnResolve)).ToList();
    }

    private async Task Apply(AlertRule e, AlertRuleRequest r, CancellationToken ct)
    {
        var name = r.Name?.Trim() ?? ""; if (name.Length is < 1 or > 150 || name.Any(char.IsControl)) throw new ValidationFailedException("name", "Give the rule a name (up to 150 characters).");
        if (!Enum.IsDefined(r.Type)) throw new ValidationFailedException("type", "Unknown rule type.");
        var t = r.Type;
        if (t is AlertRuleType.UplinkUtilization or AlertRuleType.PortUtilization or AlertRuleType.HighCpu or AlertRuleType.HighMemory) { if (r.Threshold is < 1 or > 100) throw new ValidationFailedException("threshold", "Choose a percentage between 1 and 100."); }
        else if (t == AlertRuleType.HighLatency) { if (r.Threshold is < 1 or > 60000) throw new ValidationFailedException("threshold", "Choose a latency between 1 and 60000 ms."); }
        else if (t is AlertRuleType.PacketDrops or AlertRuleType.PortErrors) { if (r.Threshold is < 0.1 or > 1_000_000 || double.IsNaN(r.Threshold)) throw new ValidationFailedException("threshold", "Choose a rate between 0.1 and 1,000,000 packets per second."); }
        if (r.DurationMinutes is < 0 or > 1440) throw new ValidationFailedException("durationMinutes", "Duration must be 0 to 1440 minutes.");
        if (r.CooldownMinutes is < 0 or > 10080) throw new ValidationFailedException("cooldownMinutes", "Reminder interval must be 0 to 10080 minutes.");
        if (r.SwitchId is { } sid && !await db.Switches.AnyAsync(x => x.Id == sid, ct)) throw new ValidationFailedException("switchId", "That switch does not exist.");
        e.Name = name; e.Type = t; e.Enabled = r.Enabled; e.Threshold = t is AlertRuleType.SwitchUnreachable or AlertRuleType.SwitchRestarted ? 0 : r.Threshold; e.DurationMinutes = r.DurationMinutes; e.SwitchId = r.SwitchId; e.CooldownMinutes = r.CooldownMinutes; e.NotifyOnResolve = r.NotifyOnResolve;
    }

    public async Task<AlertRuleDto> CreateRuleAsync(AlertRuleRequest r, CancellationToken ct)
    {
        if (await db.AlertRules.CountAsync(ct) >= 100) throw new ConflictException("Too many rules. Delete some first.");
        var e = new AlertRule { CreatedAt = DateTime.UtcNow }; await Apply(e, r, ct); db.AlertRules.Add(e); await db.SaveChangesAsync(ct); return await ToDto(e, ct);
    }

    public async Task<AlertRuleDto> UpdateRuleAsync(int id, AlertRuleRequest r, CancellationToken ct)
    {
        var e = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Rule not found."); await Apply(e, r, ct); await db.SaveChangesAsync(ct); return await ToDto(e, ct);
    }

    public async Task DeleteRuleAsync(int id, CancellationToken ct)
    {
        var e = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Rule not found."); db.AlertRules.Remove(e); await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ alerts (read)
    public async Task<AlertsResponse> AlertsAsync(CancellationToken ct)
    {
        AlertEventDto D(AlertEvent e) => new(e.Id, e.RuleId, e.Rule!.Name, e.Rule.Type, e.Subject, e.Title, e.Detail, e.Value, e.State, e.FirstAt, e.LastAt, e.ResolvedAt, e.NotifyCount);
        var active = await db.AlertEvents.AsNoTracking().Include(e => e.Rule).Where(e => e.State == AlertState.Firing).OrderByDescending(e => e.FirstAt).Take(200).ToListAsync(ct);
        var recent = await db.AlertEvents.AsNoTracking().Include(e => e.Rule).Where(e => e.State == AlertState.Resolved).OrderByDescending(e => e.ResolvedAt).Take(50).ToListAsync(ct);
        return new AlertsResponse(await db.AlertEvents.CountAsync(e => e.State == AlertState.Firing, ct), active.Select(D).ToList(), recent.Select(D).ToList());
    }

    // ------------------------------------------------------------------ evaluation
    private sealed record Finding(string Subject, string Title, string Detail, double? Value, bool Breach, bool Clear);
    private sealed record PortInfo(int Id, int SwitchId, string Name, string SwitchName, PortType Type, bool Up);

    /// <summary>One pass over all enabled rules: open, update and resolve alerts, then send one batched e-mail for what needs telling.</summary>
    public async Task<int> EvaluateAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow; var created = 0;
        var rules = await db.AlertRules.Where(r => r.Enabled).ToListAsync(ct);
        foreach (var rule in rules)
        {
            List<Finding> findings;
            try { findings = await FindingsAsync(rule, now, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Alert rule {Rule} could not be evaluated", rule.Id); continue; }
            var open = await db.AlertEvents.Where(e => e.RuleId == rule.Id && e.State == AlertState.Firing).ToDictionaryAsync(e => e.Subject, ct);
            foreach (var f in findings.Where(f => f.Breach))
            {
                if (open.TryGetValue(f.Subject, out var ev)) { ev.LastAt = now; ev.Value = f.Value; ev.Detail = Trunc(f.Detail, 1000); ev.Title = Trunc(f.Title, 300); }
                else if (created < MaxNewPerCycle) { db.AlertEvents.Add(new AlertEvent { RuleId = rule.Id, Subject = f.Subject, Title = Trunc(f.Title, 300), Detail = Trunc(f.Detail, 1000), Value = f.Value, State = AlertState.Firing, FirstAt = now, LastAt = now }); created++; }
            }
            var byKey = findings.ToDictionary(f => f.Subject);
            foreach (var (subject, ev) in open)
            {
                var f = byKey.GetValueOrDefault(subject);
                if (f is { Breach: true }) continue;
                if (f is null || f.Clear) { ev.State = AlertState.Resolved; ev.ResolvedAt = now; ev.ResolveNotified = !rule.NotifyOnResolve || rule.Type == AlertRuleType.SwitchRestarted || ev.NotifyCount == 0; }
            }
        }
        await db.SaveChangesAsync(ct);
        await SendPendingAsync(now, ct);
        await db.AlertEvents.Where(e => e.State == AlertState.Resolved && e.ResolvedAt < now.AddDays(-90)).ExecuteDeleteAsync(ct);   // history older than 90 days
        return created;
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];

    private async Task<List<Finding>> FindingsAsync(AlertRule rule, DateTime now, CancellationToken ct)
    {
        var dur = Math.Max(0, rule.DurationMinutes); var window = now.AddMinutes(-Math.Max(dur, 1) - 15);
        switch (rule.Type)
        {
            case AlertRuleType.UplinkUtilization or AlertRuleType.PortUtilization or AlertRuleType.PacketDrops or AlertRuleType.PortErrors:
            {
                var q = db.PortMetrics.AsNoTracking().Where(m => m.At >= window);
                if (rule.SwitchId is { } sid) q = q.Where(m => m.SwitchId == sid);
                var samples = (await q.Select(m => new { m.PortId, m.At, m.UtilPct, m.DiscardsPerSec, m.ErrorsPerSec }).ToListAsync(ct)).GroupBy(m => m.PortId).ToList();
                var ids = samples.Select(g => g.Key).ToList();
                var ports = await db.SwitchPorts.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new PortInfo(p.Id, p.SwitchId, p.Name, p.Switch!.Name, p.PortType, p.OperUp)).ToDictionaryAsync(p => p.Id, ct);
                double? Val(dynamic m) => rule.Type switch { AlertRuleType.PacketDrops => (double?)m.DiscardsPerSec, AlertRuleType.PortErrors => (double?)m.ErrorsPerSec, _ => (double?)m.UtilPct };
                var unit = rule.Type is AlertRuleType.PacketDrops or AlertRuleType.PortErrors ? " packets/s" : " %"; var result = new List<Finding>();
                foreach (var g in samples)
                {
                    if (!ports.TryGetValue(g.Key, out var p)) continue;
                    if (rule.Type == AlertRuleType.UplinkUtilization && p.Type != PortType.SwitchUplink) continue;
                    var s = g.OrderBy(x => x.At).Select(x => (x.At, V: rule.Type switch { AlertRuleType.PacketDrops => (double?)x.DiscardsPerSec, AlertRuleType.PortErrors => (double?)x.ErrorsPerSec, _ => x.UtilPct })).Where(x => x.V is not null).Select(x => (x.At, V: x.V!.Value)).ToList();
                    var (breach, clear, latest) = Sustained(s, rule.Threshold, dur, now);
                    var what = rule.Type switch { AlertRuleType.UplinkUtilization => "Uplink utilization", AlertRuleType.PortUtilization => "Port utilization", AlertRuleType.PacketDrops => "Packet drops", _ => "Port errors" };
                    result.Add(new Finding($"port:{p.Id}", $"{what}: {p.SwitchName} {p.Name} at {latest:0.#}{unit}", $"{what} on {p.SwitchName} {p.Name} is {latest:0.#}{unit} (limit {rule.Threshold:0.#}{unit}{(dur > 0 ? $" for {dur} min" : "")}).", latest, breach, clear));
                }
                return result;
            }
            case AlertRuleType.HighCpu or AlertRuleType.HighMemory or AlertRuleType.HighLatency:
            {
                var q = db.SwitchMetrics.AsNoTracking().Where(m => m.At >= window && m.Reachable);
                if (rule.SwitchId is { } sid) q = q.Where(m => m.SwitchId == sid);
                var rows = await q.Select(m => new { m.SwitchId, Name = m.Switch!.Name, m.At, m.CpuPct, m.MemPct, m.LatencyMs }).ToListAsync(ct);
                var unit = rule.Type == AlertRuleType.HighLatency ? " ms" : " %"; var what = rule.Type switch { AlertRuleType.HighCpu => "CPU", AlertRuleType.HighMemory => "Memory", _ => "Latency" }; var result = new List<Finding>();
                foreach (var g in rows.GroupBy(r => r.SwitchId))
                {
                    var s = g.OrderBy(x => x.At).Select(x => (x.At, V: rule.Type switch { AlertRuleType.HighCpu => x.CpuPct, AlertRuleType.HighMemory => x.MemPct, _ => x.LatencyMs is { } l ? (double?)l : null })).Where(x => x.V is not null).Select(x => (x.At, V: x.V!.Value)).ToList();
                    if (s.Count == 0) continue;
                    var (breach, clear, latest) = Sustained(s, rule.Threshold, dur, now);
                    result.Add(new Finding($"switch:{g.Key}", $"{what} high on {g.First().Name}: {latest:0.#}{unit}", $"{what} of {g.First().Name} is {latest:0.#}{unit} (limit {rule.Threshold:0.#}{unit}{(dur > 0 ? $" for {dur} min" : "")}).", latest, breach, clear));
                }
                return result;
            }
            case AlertRuleType.SwitchUnreachable:
            {
                var q = db.SwitchMetrics.AsNoTracking().Where(m => m.At >= now.AddHours(-48));
                if (rule.SwitchId is { } sid) q = q.Where(m => m.SwitchId == sid);
                var rows = await q.Select(m => new { m.SwitchId, Name = m.Switch!.Name, m.At, m.Reachable }).ToListAsync(ct); var result = new List<Finding>();
                foreach (var g in rows.GroupBy(r => r.SwitchId))
                {
                    var ordered = g.OrderBy(x => x.At).ToList(); var latest = ordered[^1];
                    if (latest.Reachable) { result.Add(new Finding($"switch:{g.Key}", "", "", null, false, true)); continue; }
                    var lastUp = ordered.LastOrDefault(x => x.Reachable)?.At ?? ordered[0].At; var down = (now - lastUp).TotalMinutes;
                    result.Add(new Finding($"switch:{g.Key}", $"Switch unreachable: {latest.Name}", $"{latest.Name} has not answered SNMP since {lastUp:yyyy-MM-dd HH:mm} UTC ({down:0} min).", down, down >= dur, false));
                }
                return result;
            }
            case AlertRuleType.SwitchRestarted:
            {
                var from = now.AddMinutes(-Math.Max(dur, 5)); var q = db.SwitchMetrics.AsNoTracking().Where(m => m.At >= from.AddHours(-2) && m.Reachable && m.UptimeSeconds != null);
                if (rule.SwitchId is { } sid) q = q.Where(m => m.SwitchId == sid);
                var rows = await q.Select(m => new { m.SwitchId, Name = m.Switch!.Name, m.At, Up = m.UptimeSeconds!.Value }).ToListAsync(ct); var result = new List<Finding>();
                foreach (var g in rows.GroupBy(r => r.SwitchId))
                {
                    var o = g.OrderBy(x => x.At).ToList(); DateTime? restartAt = null; long up = 0;
                    for (var i = 1; i < o.Count; i++) if (o[i].Up + 5 < o[i - 1].Up && o[i].At >= from) { restartAt = o[i].At; up = o[i].Up; }
                    result.Add(new Finding($"switch:{g.Key}", $"Switch restarted: {g.First().Name}", $"{g.First().Name} restarted around {restartAt:yyyy-MM-dd HH:mm} UTC (uptime now {up / 60} min).", up, restartAt != null, restartAt == null));
                }
                return result;
            }
        }
        return [];
    }

    /// <summary>
    /// Sustained-threshold logic. Breach: every reading in the window is at/above the limit and the readings span at least 70 % of the duration (duration 0 = latest reading only).
    /// Clear (hysteresis): the latest reading is below 90 % of the limit, so an alert does not flap around the threshold.
    /// </summary>
    public static (bool Breach, bool Clear, double Latest) Sustained(List<(DateTime At, double V)> s, double threshold, int durationMinutes, DateTime now)
    {
        if (s.Count == 0) return (false, false, 0);
        var latest = s[^1].V; var stale = (now - s[^1].At).TotalMinutes > Math.Max(durationMinutes, 1) + 30;
        if (stale) return (false, false, latest);   // no fresh data: neither open nor close anything
        var inWindow = s.Where(x => x.At >= now.AddMinutes(-durationMinutes - 15) && (durationMinutes == 0 || x.At >= s[^1].At.AddMinutes(-durationMinutes))).ToList();
        bool breach;
        if (durationMinutes == 0) breach = latest >= threshold;
        else breach = inWindow.Count >= 2 && inWindow.All(x => x.V >= threshold) && (inWindow[^1].At - inWindow[0].At).TotalMinutes >= durationMinutes * 0.7;
        return (breach, latest < threshold * 0.9, latest);
    }

    // ------------------------------------------------------------------ e-mail
    private async Task SendPendingAsync(DateTime now, CancellationToken ct)
    {
        var cfg = await ConfigAsync(ct);
        var rcpt = MailSender.ParseRecipients(cfg.Recipients);
        if (!cfg.Enabled || rcpt.Count == 0 || MailSender.ConfigProblem(cfg) is not null) return;
        var rules = await db.AlertRules.AsNoTracking().ToDictionaryAsync(r => r.Id, ct);
        var firing = await db.AlertEvents.Include(e => e.Rule).Where(e => e.State == AlertState.Firing).ToListAsync(ct);
        var fresh = firing.Where(e => e.NotifyCount == 0).ToList();
        var reminders = firing.Where(e => e.NotifyCount > 0 && e.Rule!.CooldownMinutes > 0 && e.LastNotifiedAt is { } l && l.AddMinutes(e.Rule.CooldownMinutes) <= now).ToList();
        var resolved = await db.AlertEvents.Include(e => e.Rule).Where(e => e.State == AlertState.Resolved && !e.ResolveNotified && e.NotifyCount > 0 && e.ResolvedAt > now.AddHours(-24)).ToListAsync(ct);
        if (fresh.Count + reminders.Count + resolved.Count == 0) return;

        var (subject, text, html) = Compose(cfg, fresh, reminders, resolved, now);
        var res = await mail.SendAsync(cfg, rcpt, new MailMessageData(subject, text, html), ct);
        await LogAsync(string.Join("; ", rcpt.Select(r => r.Address)), subject, res, ct);
        if (!res.Success) { log.LogWarning("AlertMailFailed: {Count} notification(s) will be retried", fresh.Count + reminders.Count + resolved.Count); return; }
        foreach (var e in fresh.Concat(reminders)) { e.NotifyCount++; e.LastNotifiedAt = now; }
        foreach (var e in resolved) e.ResolveNotified = true;
        await db.SaveChangesAsync(ct);
    }

    public static (string Subject, string Text, string Html) Compose(NotificationConfig cfg, List<AlertEvent> fresh, List<AlertEvent> reminders, List<AlertEvent> resolved, DateTime now)
    {
        string subject = fresh.Count == 1 && reminders.Count + resolved.Count == 0 ? $"[ALERT] {fresh[0].Title}"
            : resolved.Count == 1 && fresh.Count + reminders.Count == 0 ? $"[RESOLVED] {resolved[0].Title}"
            : $"[SwitchPort Finder] {fresh.Count} new, {reminders.Count} ongoing, {resolved.Count} resolved";
        var link = string.IsNullOrEmpty(cfg.PublicBaseUrl) ? "" : cfg.PublicBaseUrl;
        var t = new StringBuilder(); var h = new StringBuilder("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#0f1b2d\"><h2 style=\"margin:0 0 8px\">SwitchPort Finder alerts</h2>");
        void Section(string title, string color, IEnumerable<AlertEvent> list, string word)
        {
            var l = list.ToList(); if (l.Count == 0) return;
            t.AppendLine($"{title} ({l.Count})"); h.Append($"<h3 style=\"color:{color};margin:14px 0 4px\">{WebUtility.HtmlEncode(title)} ({l.Count})</h3><ul style=\"padding-left:18px;margin:0\">");
            foreach (var e in l) { t.AppendLine($"  - {e.Title}\n    {e.Detail}\n    since {e.FirstAt:yyyy-MM-dd HH:mm} UTC"); h.Append($"<li style=\"margin-bottom:6px\"><b>{WebUtility.HtmlEncode(e.Title)}</b><br><span style=\"color:#586677\">{WebUtility.HtmlEncode(e.Detail)} Since {e.FirstAt:yyyy-MM-dd HH:mm} UTC.</span></li>"); }
            t.AppendLine(); h.Append("</ul>");
        }
        Section("New alerts", "#be123c", fresh, "new"); Section("Still active", "#92400e", reminders, "ongoing"); Section("Resolved", "#047857", resolved, "resolved");
        if (link.Length > 0) { t.AppendLine($"Open the portal: {link}"); h.Append($"<p style=\"margin-top:16px\"><a href=\"{WebUtility.HtmlEncode(link)}\">Open SwitchPort Finder</a></p>"); }
        t.AppendLine("You receive this because e-mail notifications are enabled in SwitchPort Finder (Settings > Notifications)."); h.Append("<p style=\"color:#586677;font-size:12px\">You receive this because e-mail notifications are enabled in SwitchPort Finder (Settings &gt; Notifications).</p></div>");
        return (subject, t.ToString(), h.ToString());
    }
}
