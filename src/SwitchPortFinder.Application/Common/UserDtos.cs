using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Common;

public record UserDto(int Id, string Username, UserRole Role, bool Disabled, bool Locked, DateTime CreatedAt, DateTime? LastLoginAt, bool MfaEnabled = false);
public record CreateUserRequest(string Username, string Password, UserRole Role);
public record UpdateUserRequest(UserRole? Role, bool? Disabled, string? NewPassword, bool? ResetMfa = null);
public record LoginRequest(string Username, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
/// <summary>MfaRequired + MfaChallenge: the password was right but a code is still needed (no session yet). MfaSetupRequired: signed in, but policy demands enrolment first.</summary>
public record MeDto(bool AuthRequired, bool Authenticated, string? Username, UserRole? Role, bool MfaEnabled = false, bool MfaSetupRequired = false, bool MfaRequired = false, string? MfaChallenge = null);
public record MfaStartRequest(string Password, string? Name = null, string? Code = null);
public record MfaDeviceDto(int Id, string Name, DateTime CreatedAt, DateTime? LastUsedAt);
public record MfaCodeRequest(string Code);
public record MfaLoginRequest(string Challenge, string Code);
public record MfaDisableRequest(string Password, string Code);
public record MfaSetupDto(string Secret, string OtpauthUri);
public record MfaEnabledDto(IReadOnlyList<string> RecoveryCodes);
