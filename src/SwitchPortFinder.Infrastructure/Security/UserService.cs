using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Security;

public enum LoginOutcome { Ok, Invalid, Locked, Disabled }

public class UserService(AppDbContext db, IMemoryCache cache, IOptions<AuthOptions> options, ILogger<UserService> log)
{
    private AuthOptions Opt => options.Value;
    private static string Norm(string u) => u.Trim().ToLowerInvariant();
    private static string CacheKey(int id) => "user-state:" + id;

    /// <summary>Verifies credentials. Unknown user and wrong password are indistinguishable (same message, same hashing cost).</summary>
    public async Task<(LoginOutcome Outcome, AppUser? User)> AuthenticateAsync(string username, string password, CancellationToken ct)
    {
        var n = Norm(username ?? "");
        var user = n.Length == 0 ? null : await db.Users.FirstOrDefaultAsync(u => u.UsernameNormalized == n, ct);
        var now = DateTime.UtcNow;
        if (user is null) { PasswordHasher.Verify(password ?? "", PasswordHasher.Dummy); log.LogWarning("LoginFailed: unknown account"); return (LoginOutcome.Invalid, null); }
        if (user.LockedUntil is { } until && until > now) { log.LogWarning("LoginFailed: account {User} is locked", user.Username); return (LoginOutcome.Locked, null); }
        var ok = PasswordHasher.Verify(password ?? "", user.PasswordHash);
        if (!ok)
        {
            user.FailedLogins++;
            if (user.FailedLogins >= Opt.MaxFailedLogins) { user.LockedUntil = now.AddSeconds(Opt.LockoutSeconds); user.FailedLogins = 0; log.LogWarning("AccountLocked: {User} after repeated failures", user.Username); }
            await db.SaveChangesAsync(ct);
            log.LogWarning("LoginFailed: bad password for {User}", user.Username);
            return (LoginOutcome.Invalid, null);
        }
        if (user.Disabled) { log.LogWarning("LoginFailed: account {User} is disabled", user.Username); return (LoginOutcome.Disabled, null); }
        // With two-factor on, the password alone proves nothing: keep the failure counter (so wrong codes lock the account) until the code is accepted.
        if (user.MfaEnabled) { log.LogInformation("LoginPasswordOk: {User} (two-factor code required)", user.Username); return (LoginOutcome.Ok, user); }
        user.FailedLogins = 0; user.LockedUntil = null; user.LastLoginAt = now;
        await db.SaveChangesAsync(ct);
        log.LogInformation("LoginSucceeded: {User}", user.Username);
        return (LoginOutcome.Ok, user);
    }

    /// <summary>Checked on every request by the cookie handler (cached 15 s) so disabling/deleting/demoting a user takes effect quickly.</summary>
    public async Task<(bool Valid, UserRole Role, int Version, bool Mfa)> SessionStateAsync(int id, CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey(id), out (bool, UserRole, int, bool) hit)) return hit;
        var u = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Disabled, x.Role, x.SessionVersion, x.MfaEnabled }).FirstOrDefaultAsync(ct);
        var state = u is null || u.Disabled ? (false, UserRole.Viewer, 0, false) : (true, u.Role, u.SessionVersion, u.MfaEnabled);
        cache.Set(CacheKey(id), state, TimeSpan.FromSeconds(15));
        return state;
    }

    /// <summary>Re-authentication for sensitive actions (credential export). Wrong passwords count toward the lockout like a failed login.</summary>
    public async Task<bool> VerifyPasswordAsync(int id, string password, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null || u.Disabled || u.LockedUntil > DateTime.UtcNow) return false;
        if (PasswordHasher.Verify(password ?? "", u.PasswordHash)) return true;
        u.FailedLogins++; if (u.FailedLogins >= Opt.MaxFailedLogins) { u.LockedUntil = DateTime.UtcNow.AddSeconds(Opt.LockoutSeconds); u.FailedLogins = 0; }
        await db.SaveChangesAsync(ct); log.LogWarning("ReauthFailed: wrong password for {User} on a sensitive action", u.Username); return false;
    }

    public async Task<AppUser> GetForSessionAsync(int id, CancellationToken ct) => await db.Users.AsNoTracking().FirstAsync(x => x.Id == id, ct);

    public async Task MarkLoginAsync(int id, CancellationToken ct) { var u = await db.Users.FirstAsync(x => x.Id == id, ct); u.LastLoginAt = DateTime.UtcNow; u.LockedUntil = null; await db.SaveChangesAsync(ct); log.LogInformation("LoginSucceeded: {User} (two-factor)", u.Username); }

    /// <summary>Invalidates every existing session cookie of the user (logout, password change). Returns the new version.</summary>
    public async Task<int> RevokeSessionsAsync(int id, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u is null) return 0;
        u.SessionVersion++; await db.SaveChangesAsync(ct); cache.Remove(CacheKey(id));
        return u.SessionVersion;
    }

    public async Task<List<UserDto>> ListAsync(CancellationToken ct) =>
        (await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<UserDto> CreateAsync(CreateUserRequest r, string actor, CancellationToken ct)
    {
        var name = r.Username?.Trim() ?? "";
        if (name.Length is < 3 or > 100 || name.Any(char.IsControl) || name.Contains(' ')) throw new ValidationFailedException("username", "Username must be 3–100 characters with no spaces.");
        if (PasswordHasher.Policy(r.Password, name) is { } bad) throw new ValidationFailedException("password", bad);
        if (!Enum.IsDefined(r.Role)) throw new ValidationFailedException("role", "Unknown role.");
        if (await db.Users.AnyAsync(u => u.UsernameNormalized == Norm(name), ct)) throw new ConflictException($"User '{name}' already exists.");
        var u = new AppUser { Username = name, UsernameNormalized = Norm(name), PasswordHash = PasswordHasher.Hash(r.Password), Role = r.Role, CreatedAt = DateTime.UtcNow };
        db.Users.Add(u); await db.SaveChangesAsync(ct);
        log.LogInformation("UserCreated: {User} ({Role}) by {Actor}", u.Username, u.Role, actor);
        return ToDto(u);
    }

    public async Task<UserDto> UpdateAsync(int id, UpdateUserRequest r, string actor, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User not found.");
        var losesAdmin = u.Role == UserRole.Admin && !u.Disabled && ((r.Role is { } nr && nr != UserRole.Admin) || r.Disabled == true);
        if (losesAdmin && !await db.Users.AnyAsync(x => x.Id != id && x.Role == UserRole.Admin && !x.Disabled, ct))
            throw new ConflictException("This is the last active administrator; it cannot be demoted or disabled.");
        if (r.Role is { } role) { if (!Enum.IsDefined(role)) throw new ValidationFailedException("role", "Unknown role."); if (role != u.Role) u.SessionVersion++; u.Role = role; }
        if (r.Disabled is { } d) u.Disabled = d;
        if (r.ResetMfa == true) { MfaService.Clear(u); db.MfaDevices.RemoveRange(db.MfaDevices.Where(d => d.UserId == id)); u.SessionVersion++; log.LogWarning("MfaReset: {User} by {Actor}", u.Username, actor); }
        if (r.NewPassword is not null)
        {
            if (PasswordHasher.Policy(r.NewPassword, u.Username) is { } bad) throw new ValidationFailedException("newPassword", bad);
            u.PasswordHash = PasswordHasher.Hash(r.NewPassword); u.FailedLogins = 0; u.LockedUntil = null; u.SessionVersion++;   // an admin reset signs the user out everywhere
        }
        await db.SaveChangesAsync(ct); cache.Remove(CacheKey(id));
        log.LogInformation("UserUpdated: {User} by {Actor}", u.Username, actor);
        return ToDto(u);
    }

    public async Task DeleteAsync(int id, string actor, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User not found.");
        if (u.Role == UserRole.Admin && !u.Disabled && !await db.Users.AnyAsync(x => x.Id != id && x.Role == UserRole.Admin && !x.Disabled, ct))
            throw new ConflictException("This is the last active administrator; it cannot be deleted.");
        db.Users.Remove(u); await db.SaveChangesAsync(ct); cache.Remove(CacheKey(id));
        log.LogInformation("UserDeleted: {User} by {Actor}", u.Username, actor);
    }

    /// <summary>Returns the new session version so the caller can keep the current browser signed in while every other session is revoked.</summary>
    public async Task<int> ChangeOwnPasswordAsync(int id, ChangePasswordRequest r, CancellationToken ct)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User not found.");
        if (!PasswordHasher.Verify(r.CurrentPassword ?? "", u.PasswordHash)) throw new ValidationFailedException("currentPassword", "Current password is incorrect.");
        if (PasswordHasher.Policy(r.NewPassword, u.Username) is { } bad) throw new ValidationFailedException("newPassword", bad);
        u.PasswordHash = PasswordHasher.Hash(r.NewPassword); u.SessionVersion++; await db.SaveChangesAsync(ct); cache.Remove(CacheKey(id));
        log.LogInformation("PasswordChanged: {User}", u.Username);
        return u.SessionVersion;
    }

    /// <summary>Creates the first administrator from configuration when Local mode is on and there are no users. Never logs the password.</summary>
    public async Task EnsureBootstrapAdminAsync(CancellationToken ct)
    {
        if (!Opt.IsLocal || await db.Users.AnyAsync(ct)) return;
        var b = Opt.BootstrapAdmin;
        if (string.IsNullOrWhiteSpace(b.Password)) { log.LogError("Authentication:Mode is Local but there are no users and Authentication:BootstrapAdmin:Password is not set: nobody can sign in."); return; }
        await CreateAsync(new CreateUserRequest(b.Username, b.Password, UserRole.Admin), "bootstrap", ct);
        log.LogWarning("A bootstrap administrator '{User}' was created from configuration. Sign in, change the password and remove it from the configuration.", b.Username);
    }

    private static UserDto ToDto(AppUser u) => new(u.Id, u.Username, u.Role, u.Disabled, u.LockedUntil > DateTime.UtcNow, u.CreatedAt, u.LastLoginAt, u.MfaEnabled);
}
