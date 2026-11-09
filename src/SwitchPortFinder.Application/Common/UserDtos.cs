using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Common;

public record UserDto(int Id, string Username, UserRole Role, bool Disabled, bool Locked, DateTime CreatedAt, DateTime? LastLoginAt);
public record CreateUserRequest(string Username, string Password, UserRole Role);
public record UpdateUserRequest(UserRole? Role, bool? Disabled, string? NewPassword);
public record LoginRequest(string Username, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record MeDto(bool AuthRequired, bool Authenticated, string? Username, UserRole? Role);
