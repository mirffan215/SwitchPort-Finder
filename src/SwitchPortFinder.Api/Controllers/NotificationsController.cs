using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SwitchPortFinder.Api.Infrastructure;
using SwitchPortFinder.Application.Notifications;
using SwitchPortFinder.Infrastructure.Notifications;

namespace SwitchPortFinder.Api.Controllers;

/// <summary>Mail-server settings and alert rules: administrators only (even to read: they describe your monitoring and mail setup).</summary>
[ApiController]
[Route("api/notifications")]
[Authorize(Policy = Policies.AdminOnly)]
public class NotificationsController(AlertService alerts, SwitchPortFinder.Application.Abstractions.IUserAuthorizationService who) : ControllerBase
{
    [HttpGet("config")] public async Task<IActionResult> GetConfig(CancellationToken ct) => Ok(await alerts.GetConfigAsync(ct));
    [HttpPut("config")] public async Task<IActionResult> PutConfig(NotificationConfigRequest r, CancellationToken ct) => Ok(await alerts.UpdateConfigAsync(r, who.CurrentUserName, ct));
    [HttpPost("test"), EnableRateLimiting("snmp-test")] public async Task<IActionResult> Test(TestMailRequest r, CancellationToken ct) => Ok(await alerts.SendTestAsync(r.To, ct));
    [HttpGet("log")] public async Task<IActionResult> Log(CancellationToken ct) => Ok(await alerts.LogAsync(ct));
    [HttpGet("rules")] public async Task<IActionResult> Rules(CancellationToken ct) => Ok(await alerts.RulesAsync(ct));
    [HttpPost("rules")] public async Task<IActionResult> CreateRule(AlertRuleRequest r, CancellationToken ct) => StatusCode(201, await alerts.CreateRuleAsync(r, ct));
    [HttpPut("rules/{id:int}")] public async Task<IActionResult> UpdateRule(int id, AlertRuleRequest r, CancellationToken ct) => Ok(await alerts.UpdateRuleAsync(id, r, ct));
    [HttpDelete("rules/{id:int}")] public async Task<IActionResult> DeleteRule(int id, CancellationToken ct) { await alerts.DeleteRuleAsync(id, ct); return NoContent(); }
}

/// <summary>Active and recent alerts: visible to every signed-in user (read-only).</summary>
[ApiController]
public class AlertsController(AlertService alerts) : ControllerBase
{
    [HttpGet("api/alerts")] public async Task<IActionResult> Get(CancellationToken ct) => Ok(await alerts.AlertsAsync(ct));
}
