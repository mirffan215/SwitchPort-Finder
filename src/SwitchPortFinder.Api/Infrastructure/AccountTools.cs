using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SwitchPortFinder.Infrastructure.Persistence;
using SwitchPortFinder.Infrastructure.Security;

namespace SwitchPortFinder.Api.Infrastructure;

/// <summary>
/// `SwitchPortFinder.Api.exe --reset-password &lt;username&gt; [--reset-mfa]` (used by scripts/reset-admin.ps1): sets a new password for an existing account when nobody can sign in.
/// The new password comes from the SPF_NEW_PASSWORD environment variable (never from the command line). Runs as the person running it (Windows authentication to SQL Server)
/// and never prints the password or the connection string. Also clears a lockout and ends the account's open sessions.
/// </summary>
public static class AccountTools
{
    public static async Task<int> RunAsync(string[] args)
    {
        var i = Array.IndexOf(args, "--reset-password");
        var user = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        if (string.IsNullOrWhiteSpace(user)) { Console.Error.WriteLine("ERROR: give the user name, e.g. --reset-password admin"); return 2; }
        var pw = Environment.GetEnvironmentVariable("SPF_NEW_PASSWORD");
        if (string.IsNullOrEmpty(pw)) { Console.Error.WriteLine("ERROR: the new password was not supplied (SPF_NEW_PASSWORD)."); return 2; }
        if (PasswordHasher.Policy(pw, user) is { } problem) { Console.Error.WriteLine("ERROR: " + problem); return 2; }
        try
        {
            var cfg = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true).AddJsonFile("appsettings.Production.json", optional: true).AddEnvironmentVariables().Build();
            var cs = cfg.GetConnectionString("Default") ?? "";
            await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options);
            var norm = user.Trim().ToLowerInvariant();
            var u = await db.Users.FirstOrDefaultAsync(x => x.UsernameNormalized == norm);
            if (u is null) { Console.Error.WriteLine($"ERROR: there is no user named '{user}'."); return 1; }
            u.PasswordHash = PasswordHasher.Hash(pw); u.FailedLogins = 0; u.LockedUntil = null; u.Disabled = false; u.SessionVersion++;
            if (args.Contains("--reset-mfa")) { MfaService.Clear(u); db.MfaDevices.RemoveRange(db.MfaDevices.Where(d => d.UserId == u.Id)); }
            await db.SaveChangesAsync();
            Console.WriteLine($"Password for '{u.Username}' was reset{(args.Contains("--reset-mfa") ? " and two-factor was cleared" : "")}. Restart the SwitchPort Finder service.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}"); return 1; }
    }
}
