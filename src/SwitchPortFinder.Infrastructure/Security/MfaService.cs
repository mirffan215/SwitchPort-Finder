using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Security;

/// <summary>Authenticator-app (TOTP) two-factor authentication: enrolment, login verification, recovery codes. Secrets are encrypted at rest; codes are single-use; failures count toward the account lockout.</summary>
public class MfaService(AppDbContext db, IDataProtectionProvider dp, IMemoryCache cache, IOptions<AuthOptions> options, ILogger<MfaService> log)
{
    private AuthOptions Opt => options.Value;
    private readonly IDataProtector _secrets = dp.CreateProtector("SwitchPortFinder.Mfa.Secret.v1");
    private readonly IDataProtector _challenge = dp.CreateProtector("SwitchPortFinder.Mfa.Challenge.v1");
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    public bool Required(Domain.Enums.UserRole role) => Opt.RequireMfaForAdmins && role == Domain.Enums.UserRole.Admin;

    public string MakeChallenge(int userId, int sessionVersion) => _challenge.Protect($"{userId}|{sessionVersion}|{DateTimeOffset.UtcNow.Add(ChallengeLifetime).ToUnixTimeSeconds()}");

    /// <summary>Returns the user id, or null when the token is forged, expired or was issued before a logout/password change.</summary>
    public async Task<int?> ReadChallengeAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2000) return null;
        try
        {
            var parts = _challenge.Unprotect(token).Split('|');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var id) || !int.TryParse(parts[1], out var ver) || !long.TryParse(parts[2], out var exp) || exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return null;
            var current = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => (int?)u.SessionVersion).FirstOrDefaultAsync(ct);
            return current == ver ? id : null;
        }
        catch (System.Security.Cryptography.CryptographicException) { return null; }
    }

    private async Task<Domain.Entities.AppUser> Get(int id, CancellationToken ct) => await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User not found.");

    private async Task<bool> RegisterFailure(Domain.Entities.AppUser u, CancellationToken ct)
    {
        u.FailedLogins++;
        if (u.FailedLogins >= Opt.MaxFailedLogins) { u.LockedUntil = DateTime.UtcNow.AddSeconds(Opt.LockoutSeconds); u.FailedLogins = 0; log.LogWarning("AccountLocked: {User} after repeated wrong two-factor codes", u.Username); }
        await db.SaveChangesAsync(ct); return false;
    }

    private void Evict(int id) => cache.Remove("user-state:" + id);

    /// <summary>Step 1 of enrolment: needs the current password again, creates a pending secret.</summary>
    public async Task<MfaSetupDto> StartSetupAsync(int id, string password, CancellationToken ct)
    {
        var u = await Get(id, ct);
        if (u.LockedUntil > DateTime.UtcNow) throw new ConflictException("Account is temporarily locked.");
        if (!PasswordHasher.Verify(password ?? "", u.PasswordHash)) { await RegisterFailure(u, ct); throw new ValidationFailedException("password", "Password is incorrect."); }
        if (u.MfaEnabled) throw new ConflictException("Two-factor authentication is already enabled. Disable it first to enrol a new device.");
        var secret = Totp.NewSecret();
        u.MfaPendingSecretProtected = _secrets.Protect(Totp.Base32(secret)); await db.SaveChangesAsync(ct);
        return new MfaSetupDto(Totp.Base32(secret), Totp.Uri(Opt.MfaIssuer, u.Username, secret));
    }

    /// <summary>Step 2: the user proves the app works by entering a current code. Returns the one-time recovery codes and the new session version.</summary>
    public async Task<(List<string> Codes, int Version)> EnableAsync(int id, string code, CancellationToken ct)
    {
        var u = await Get(id, ct);
        if (u.MfaPendingSecretProtected is null) throw new ValidationFailedException("code", "Start the set-up first.");
        var secret = Totp.FromBase32(_secrets.Unprotect(u.MfaPendingSecretProtected))!;
        if (Totp.Verify(secret, code, DateTimeOffset.UtcNow) is not { } step) { await RegisterFailure(u, ct); throw new ValidationFailedException("code", "That code is not valid. Check the time on your phone and try the next code."); }
        var codes = Totp.NewRecoveryCodes();
        u.MfaEnabled = true; u.MfaSecretProtected = u.MfaPendingSecretProtected; u.MfaPendingSecretProtected = null; u.MfaLastStep = step;
        u.MfaRecoveryHashes = JsonSerializer.Serialize(codes.Select(Totp.HashRecovery)); u.FailedLogins = 0;
        u.SessionVersion++;    // other sessions of this user end; the caller re-issues the current one
        await db.SaveChangesAsync(ct); Evict(id);
        log.LogInformation("MfaEnabled: {User}", u.Username);
        return (codes, u.SessionVersion);
    }

    /// <summary>Login step 2: a 6-digit code, or one unused recovery code. Failures count toward the lockout.</summary>
    public async Task<bool> VerifyLoginAsync(int id, string input, CancellationToken ct)
    {
        var u = await Get(id, ct);
        if (u.Disabled || !u.MfaEnabled || u.MfaSecretProtected is null) return false;
        if (u.LockedUntil > DateTime.UtcNow) return false;
        var secret = Totp.FromBase32(_secrets.Unprotect(u.MfaSecretProtected))!;
        var digits = new string((input ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == Totp.Digits && !(input ?? "").Any(char.IsLetter))
        {
            if (Totp.Verify(secret, digits, DateTimeOffset.UtcNow, u.MfaLastStep) is { } step) { u.MfaLastStep = step; u.FailedLogins = 0; await db.SaveChangesAsync(ct); return true; }
            log.LogWarning("LoginFailed: wrong two-factor code for {User}", u.Username); return await RegisterFailure(u, ct);
        }
        var hashes = JsonSerializer.Deserialize<List<string>>(u.MfaRecoveryHashes ?? "[]") ?? [];
        var h = Totp.HashRecovery(input ?? "");
        if (Totp.NormalizeRecovery(input).Length == 12 && hashes.Remove(h))
        {
            u.MfaRecoveryHashes = JsonSerializer.Serialize(hashes); u.FailedLogins = 0; await db.SaveChangesAsync(ct);
            log.LogWarning("RecoveryCodeUsed: {User} signed in with a recovery code ({Left} left)", u.Username, hashes.Count); return true;
        }
        log.LogWarning("LoginFailed: wrong two-factor/recovery code for {User}", u.Username); return await RegisterFailure(u, ct);
    }

    private async Task<Domain.Entities.AppUser> Reauth(int id, string password, string code, CancellationToken ct)
    {
        var u = await Get(id, ct);
        if (!PasswordHasher.Verify(password ?? "", u.PasswordHash)) { await RegisterFailure(u, ct); throw new ValidationFailedException("password", "Password is incorrect."); }
        if (!await VerifyLoginAsync(id, code, ct)) throw new ValidationFailedException("code", "That code is not valid.");
        return await Get(id, ct);
    }

    /// <summary>Turns MFA off for the caller (needs password + a current code). The controller blocks this for administrators when policy requires MFA.</summary>
    public async Task<int> DisableAsync(int id, string password, string code, CancellationToken ct)
    {
        var u = await Reauth(id, password, code, ct); Clear(u); u.SessionVersion++; await db.SaveChangesAsync(ct); Evict(id);
        log.LogWarning("MfaDisabled: {User}", u.Username); return u.SessionVersion;
    }

    public async Task<(List<string> Codes, int Version)> RegenerateRecoveryCodesAsync(int id, string password, string code, CancellationToken ct)
    {
        var u = await Reauth(id, password, code, ct); var codes = Totp.NewRecoveryCodes();
        u.MfaRecoveryHashes = JsonSerializer.Serialize(codes.Select(Totp.HashRecovery)); await db.SaveChangesAsync(ct);
        return (codes, u.SessionVersion);
    }

    /// <summary>Administrator reset for a user who lost their device. The user must enrol again (administrators are forced to by policy).</summary>
    public static void Clear(Domain.Entities.AppUser u) { u.MfaEnabled = false; u.MfaSecretProtected = null; u.MfaPendingSecretProtected = null; u.MfaRecoveryHashes = null; u.MfaLastStep = 0; }

    public int RecoveryCodesLeft(Domain.Entities.AppUser u) => JsonSerializer.Deserialize<List<string>>(u.MfaRecoveryHashes ?? "[]")?.Count ?? 0;
}
