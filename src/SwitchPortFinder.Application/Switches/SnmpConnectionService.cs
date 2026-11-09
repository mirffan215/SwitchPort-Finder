using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Switches;

public class SnmpConnectionService(ISnmpClient snmp, IOptions<SnmpOptions> options, ILogger<SnmpConnectionService> log)
{
    public SnmpTarget BuildTarget(string ip, SnmpCredentials creds)
    {
        var o = options.Value;
        return new SnmpTarget(IPAddress.Parse(ip), o.Port, creds, o.TimeoutMs, o.Retries, o.MaxRepetitions);
    }

    /// <summary>Real SNMP GET of the system group. Messages never contain credentials.</summary>
    public async Task<TestConnectionResult> TestAsync(string ip, SnmpCredentials creds, Manufacturer selected, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var vbs = await snmp.GetAsync(BuildTarget(ip, creds),
                [Oids.SysDescr, Oids.SysObjectId, Oids.SysUpTime, Oids.SysName], ct);
            string? Text(string oid) => vbs.FirstOrDefault(v => v.Oid == oid) is { Value.IsMissing: false } v ? v.Value.ToString() : null;
            var sysObj = Text(Oids.SysObjectId);
            long? up = vbs.FirstOrDefault(v => v.Oid == Oids.SysUpTime) is { Value.IsMissing: false } u ? u.Value.Number : null;
            var detected = VendorDetector.Detect(sysObj);
            log.LogInformation("SNMP test succeeded for {Ip} in {Elapsed} ms (detected {Vendor})", ip, sw.ElapsedMilliseconds, detected);
            return new TestConnectionResult(true, "SNMP connection successful", null, Text(Oids.SysName), Text(Oids.SysDescr), sysObj,
                up, detected, VendorDetector.DisplayName(detected), VendorDetector.Mismatch(selected, detected), sw.ElapsedMilliseconds);
        }
        catch (SnmpException ex)
        {
            if (ex.Failure == SnmpFailure.AuthenticationFailed) log.LogWarning("SnmpAuthenticationFailed for {Ip}", ip);
            else log.LogWarning("SwitchUnreachable or SNMP error for {Ip}: {Failure}", ip, ex.Failure);
            return Fail(ex.Failure.ToString(), Friendly(ex), sw);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Unexpected SNMP test failure for {Ip}", ip);
            return Fail("Other", "Unexpected error while contacting the switch. See server log.", sw);
        }
    }

    private static TestConnectionResult Fail(string code, string msg, Stopwatch sw) =>
        new(false, msg, code, null, null, null, null, null, null, false, sw.ElapsedMilliseconds);

    public static string Friendly(SnmpException ex) => ex.Failure switch
    {
        SnmpFailure.Timeout => "No response from the switch (timeout). Check the IP address, that SNMP is enabled, the community/user, and any ACL or firewall for UDP 161.",
        SnmpFailure.Unreachable => "The switch could not be reached (network unreachable or connection refused).",
        SnmpFailure.AuthenticationFailed => "SNMPv3 authentication failed. Check the username, authentication protocol and password.",
        SnmpFailure.Decryption => "SNMPv3 privacy (decryption) failed. Check the privacy protocol and password.",
        SnmpFailure.UnknownUser => "The switch does not recognise this SNMPv3 username.",
        SnmpFailure.UnsupportedSecurityLevel => "The switch rejected the SNMPv3 security level (auth/priv combination).",
        SnmpFailure.Protocol => "The switch returned an SNMP protocol error.",
        _ => "SNMP request failed."
    };
}
