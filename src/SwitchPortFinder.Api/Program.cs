using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SwitchPortFinder.Api.Infrastructure;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Discovery;
using SwitchPortFinder.Infrastructure;
using SwitchPortFinder.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console());

builder.Services.Configure<SnmpOptions>(builder.Configuration.GetSection(SnmpOptions.Section));
builder.Services.Configure<DiscoveryOptions>(builder.Configuration.GetSection(DiscoveryOptions.Section));
builder.Services.Configure<ExportOptions>(builder.Configuration.GetSection(ExportOptions.Section));
builder.Services.Configure<ImportOptions>(builder.Configuration.GetSection(ImportOptions.Section));
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddDiscovery();

builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = builder.Configuration.GetValue("Import:MaxFileBytes", 10 * 1024 * 1024));

builder.Services.AddScoped<IAuthorizationHandler, AdminOnlyHandler>();
builder.Services.AddAuthorization(o => o.AddPolicy(Policies.AdminOnly, p => p.AddRequirements(new AdminOnlyRequirement())));

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

// Database: SQL Server uses migrations; Sqlite (sandbox/tests only) uses EnsureCreated.
if (app.Configuration.GetValue("Database:AutoMigrate", false))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsSqlite()) db.Database.EnsureCreated(); else db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Plain HTTP in V1. To enable HTTPS later add an Https endpoint under Kestrel:Endpoints and call app.UseHttpsRedirection().
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapFallbackToFile("index.html");   // React SPA routes (/switches/1 etc.)

app.Run();

public partial class Program;
