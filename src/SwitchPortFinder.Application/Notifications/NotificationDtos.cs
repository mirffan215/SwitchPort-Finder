using SwitchPortFinder.Domain.Entities;

namespace SwitchPortFinder.Application.Notifications;

/// <summary>Never contains the password: only whether one is stored.</summary>
public record NotificationConfigDto(bool Enabled, string Host, int Port, SmtpSecurity Security, string? Username, bool HasPassword, string FromAddress, string FromName, IReadOnlyList<string> Recipients, bool TrustServerCertificate, string? PublicBaseUrl);

/// <summary>Password: null = keep the stored one, "" = remove it, anything else = replace it.</summary>
public record NotificationConfigRequest(bool Enabled, string Host, int Port, SmtpSecurity Security, string? Username, string? Password, string FromAddress, string? FromName, IReadOnlyList<string>? Recipients, bool TrustServerCertificate, string? PublicBaseUrl);

public record AlertRuleDto(int Id, string Name, AlertRuleType Type, bool Enabled, double Threshold, int DurationMinutes, int? SwitchId, string? SwitchName, int CooldownMinutes, bool NotifyOnResolve);
public record AlertRuleRequest(string Name, AlertRuleType Type, bool Enabled, double Threshold, int DurationMinutes, int? SwitchId, int CooldownMinutes, bool NotifyOnResolve);

public record AlertEventDto(long Id, int RuleId, string RuleName, AlertRuleType Type, string Subject, string Title, string Detail, double? Value, AlertState State, DateTime FirstAt, DateTime LastAt, DateTime? ResolvedAt, int NotifyCount);
public record AlertsResponse(int ActiveCount, IReadOnlyList<AlertEventDto> Active, IReadOnlyList<AlertEventDto> Recent);
public record NotificationLogDto(DateTime At, string Recipients, string Subject, bool Success, string? Error);
public record TestMailRequest(string? To);
public record TestMailResult(bool Success, string Message);
