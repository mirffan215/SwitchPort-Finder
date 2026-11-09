using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Api.Infrastructure;

/// <summary>
/// `SwitchPortFinder.Api.exe --setup-database [--grant "NT SERVICE\SwitchPortFinder"]`
/// Used by the installer (scripts/install.ps1): creates the database if missing, applies the EF migrations, and gives the service account
/// least-privilege access (db_datareader + db_datawriter: the running service never needs DDL rights). Runs as the person running it
/// (Windows authentication), reading the same configuration files as the service. Never prints connection strings.
/// </summary>
public static class DatabaseSetup
{
    private static readonly Regex DbName = new(@"^[A-Za-z0-9_\-]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex Principal = new(@"^[A-Za-z0-9 _\\$.\-]{1,128}$", RegexOptions.Compiled);

    public static bool IsValidDatabaseName(string? n) => n is not null && DbName.IsMatch(n);
    public static bool IsValidPrincipal(string? p) => p is not null && Principal.IsMatch(p) && p.Count(c => c == '\\') <= 1;
    /// <summary>"DOMAIN\user" / "NT SERVICE\x" are Windows principals (need CREATE LOGIN … FROM WINDOWS); anything else must be an existing SQL login.</summary>
    public static bool IsWindowsPrincipal(string p) => p.Contains('\\');

    public static async Task<int> RunAsync(string[] args)
    {
        var grants = new List<string>();
        for (var i = 0; i < args.Length - 1; i++) if (args[i] == "--grant") grants.Add(args[i + 1]);
        try
        {
            var cfg = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true).AddJsonFile("appsettings.Production.json", optional: true).AddEnvironmentVariables().Build();
            if (!string.Equals(cfg["Database:Provider"] ?? "SqlServer", "SqlServer", StringComparison.OrdinalIgnoreCase)) return Fail("Database:Provider must be SqlServer.");
            var cs = cfg.GetConnectionString("Default") ?? "";
            var b = new SqlConnectionStringBuilder(cs);
            var db = b.InitialCatalog;
            if (!IsValidDatabaseName(db)) return Fail("The database name in ConnectionStrings:Default is missing or contains unsupported characters (letters, digits, _ and - only).");
            foreach (var g in grants) if (!IsValidPrincipal(g)) return Fail($"'{g}' is not a valid account name.");

            Say($"Connecting to {b.DataSource} as the current identity...");
            var master = new SqlConnectionStringBuilder(cs) { InitialCatalog = "master" }.ConnectionString;
            await using (var c = new SqlConnection(master))
            {
                await c.OpenAsync();
                await Exec(c, "IF DB_ID(@n) IS NULL BEGIN DECLARE @s nvarchar(400) = N'CREATE DATABASE ' + QUOTENAME(@n); EXEC(@s); END", ("@n", db));
                Say($"Database [{db}] is present.");
                foreach (var g in grants.Where(IsWindowsPrincipal))
                {
                    await Exec(c, "IF SUSER_ID(@p) IS NULL BEGIN DECLARE @s nvarchar(500) = N'CREATE LOGIN ' + QUOTENAME(@p) + N' FROM WINDOWS'; EXEC(@s); END", ("@p", g));
                    Say($"SQL login for {g} is present.");
                }
            }

            Say("Applying migrations...");
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs).Options;
            await using (var ctx = new AppDbContext(options))
            {
                await ctx.Database.MigrateAsync();
                var pending = (await ctx.Database.GetPendingMigrationsAsync()).Count();
                Say($"Schema is up to date ({pending} pending).");
            }

            if (grants.Count > 0)
            {
                await using var c = new SqlConnection(cs); await c.OpenAsync();
                foreach (var g in grants)
                {
                    await Exec(c, "IF DATABASE_PRINCIPAL_ID(@p) IS NULL BEGIN DECLARE @s nvarchar(500) = N'CREATE USER ' + QUOTENAME(@p) + N' FOR LOGIN ' + QUOTENAME(@p); EXEC(@s); END", ("@p", g));
                    foreach (var role in new[] { "db_datareader", "db_datawriter" })
                        await Exec(c, "DECLARE @s nvarchar(500) = N'ALTER ROLE ' + QUOTENAME(@r) + N' ADD MEMBER ' + QUOTENAME(@p); EXEC(@s);", ("@r", role), ("@p", g));
                    Say($"Granted {g}: db_datareader, db_datawriter on [{db}] (least privilege; no DDL rights).");
                }
            }
            Say("Database setup complete.");
            return 0;
        }
        catch (SqlException ex) { return Fail($"SQL Server error {ex.Number}: {ex.Message}"); }
        catch (Exception ex) { return Fail($"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static async Task Exec(SqlConnection c, string sql, params (string Name, string Value)[] p)
    {
        await using var cmd = c.CreateCommand(); cmd.CommandText = sql;
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    private static void Say(string m) => Console.WriteLine("[setup-database] " + m);
    private static int Fail(string m) { Console.Error.WriteLine("[setup-database] ERROR: " + m); return 1; }
}
