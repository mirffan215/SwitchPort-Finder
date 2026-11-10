using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SwitchPortFinder.Api.Infrastructure;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Discovery;
using SwitchPortFinder.Infrastructure;
using SwitchPortFinder.Infrastructure.Persistence;

// `--setup-database [--grant "<account>"]`: used by the installer; creates/migrates the database without starting the web host.
if (args.Contains("--setup-database")) return await DatabaseSetup.RunAsync(args);

// A Windows service starts with C:\Windows\System32 as its working directory; without this the app would not find its appsettings.Production.json or wwwroot.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});

builder.Host.UseWindowsService(o => o.ServiceName = "SwitchPortFinder");   // no-op unless started by the Windows Service Control Manager
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.Configure<SnmpOptions>(builder.Configuration.GetSection(SnmpOptions.Section));
builder.Services.Configure<DiscoveryOptions>(builder.Configuration.GetSection(DiscoveryOptions.Section));
builder.Services.Configure<ExportOptions>(builder.Configuration.GetSection(ExportOptions.Section));
builder.Services.Configure<ImportOptions>(builder.Configuration.GetSection(ImportOptions.Section));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection(SecurityOptions.Section));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.Section));
var authOptions = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
builder.WebHost.ConfigureKestrel(k => { k.Limits.MaxRequestBodySize = 20 * 1024 * 1024; k.AddServerHeader = false; });
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDiscovery();
builder.Services.AddHttpContextAccessor();
builder.Services.RemoveAll<SwitchPortFinder.Application.Abstractions.IUserAuthorizationService>();
builder.Services.AddSingleton<SwitchPortFinder.Application.Abstractions.IUserAuthorizationService, HttpContextUserAuthorizationService>();

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
    {
        // The default response echoes .NET type names, internal parameter names and JSON paths. Keep only field names and safe messages.
        var parameters = ctx.ActionDescriptor.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string[]>();
        foreach (var (key, entry) in ctx.ModelState.Where(e => e.Value?.Errors.Count > 0))
        {
            var k = key.StartsWith("$.") ? key[2..] : key;
            if (k.Length == 0 || parameters.Contains(k)) { errors["body"] = ["A valid request body is required."]; continue; }
            errors[k] = entry!.Errors.Select(e => e.Exception is not null || e.ErrorMessage.Contains("System.", StringComparison.Ordinal) ? "Invalid value." : e.ErrorMessage).Distinct().ToArray();
        }
        return new BadRequestObjectResult(new ValidationProblemDetails(errors) { Status = 400, Title = "Validation failed" }) { ContentTypes = { "application/problem+json" } };
    });
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = builder.Configuration.GetValue("Import:MaxFileBytes", 10 * 1024 * 1024));

builder.Services.AddScoped<IAuthorizationHandler, AdminOnlyHandler>();
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Policies.AdminOnly, p => p.AddRequirements(new AdminOnlyRequirement()));
    if (authOptions.IsLocal) o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();   // every endpoint needs a login unless [AllowAnonymous]
});
if (authOptions.IsLocal)
    builder.Services.AddAuthentication(AuthSetup.Scheme).AddCookie(AuthSetup.Scheme, o => AuthSetup.Configure(o, authOptions));

// The SNMP test endpoints make the server send packets to a user-supplied address: rate-limit them per client.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var perMinute = builder.Configuration.GetValue("Security:SnmpTestsPerMinute", 30);
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authOptions.LoginsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("snmp-test", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<SecurityMiddleware>();
app.UseSerilogRequestLogging();
app.UseRateLimiter();

// Database: SQL Server uses migrations; Sqlite (sandbox/tests only) uses EnsureCreated.
if (app.Configuration.GetValue("Database:AutoMigrate", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsSqlite()) db.Database.EnsureCreated(); else db.Database.Migrate();
}
await app.Services.GetRequiredService<SwitchPortFinder.Infrastructure.Persistence.SettingsService>().LoadAsync();
using (var scope = app.Services.CreateScope()) { try { await scope.ServiceProvider.GetRequiredService<SwitchPortFinder.Infrastructure.Security.UserService>().EnsureBootstrapAdminAsync(default); } catch (Exception ex) { app.Logger.LogWarning(ex, "Could not create the bootstrap administrator (is the database migrated?)"); } }

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Plain HTTP in V1. To enable HTTPS later add an Https endpoint under Kestrel:Endpoints and call app.UseHttpsRedirection().
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<WriteRequiresAdminMiddleware>();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapFallback("/api/{**path}", () => Results.Problem(statusCode: 404, title: "Not found")).AllowAnonymous();   // unknown API paths are JSON 404s, never the HTML shell
app.MapFallbackToFile("index.html").AllowAnonymous();   // React SPA routes (/switches/1 etc.)

app.Run();
return 0;

public partial class Program;
