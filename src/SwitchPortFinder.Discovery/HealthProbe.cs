using System.Diagnostics;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Discovery.Parsing;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Discovery;

/// <summary>Read-only health probe. Latency is the SNMP round trip of the first request (it is not an ICMP ping).</summary>
public sealed class HealthProbe(ISnmpClient snmp, IOptions<SnmpOptions> snmpOpt) : IHealthProbe
{
    public async Task<HealthReading> ProbeAsync(HealthRequest req, CancellationToken ct)
    {
        var o = snmpOpt.Value;
        var target = new SnmpTarget(System.Net.IPAddress.Parse(req.Ip), o.Port, req.Credentials, o.TimeoutMs, o.Retries, o.MaxRepetitions);
        long? uptime; int latency;
        try
        {
            var sw = Stopwatch.StartNew();
            var vbs = await snmp.GetAsync(target, [Oids.SysUpTime], ct);
            latency = (int)sw.ElapsedMilliseconds;
            var up = vbs.FirstOrDefault(v => v.Oid == Oids.SysUpTime);
            uptime = up is { Value.IsMissing: false } ? up.Value.Number / 100 : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (SnmpException ex) { return new HealthReading(false, null, null, null, null, Application.Switches.SnmpConnectionService.Friendly(ex)); }

        var (cpu, mem) = await CpuAndMemory(target, req.Manufacturer, ct);
        IReadOnlyList<InterfaceInfo>? ifaces = null;
        if (req.WithInterfaces)
        {
            try
            {
                var ifT = await snmp.WalkAsync(target, Oids.IfTable, ct);
                IReadOnlyList<SnmpVarBind> ifX = [];
                try { ifX = await snmp.WalkAsync(target, Oids.IfXTable, ct); } catch (SnmpException) { /* 32-bit counters only */ }
                ifaces = InterfaceParser.Parse(ifT, ifX);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (SnmpException) { /* counters unavailable this time: uptime/CPU/memory are still valid */ }
        }
        return new HealthReading(true, uptime, cpu, mem, latency, null, ifaces);
    }

    private async Task<IReadOnlyList<SnmpVarBind>?> TryWalk(SnmpTarget t, string root, CancellationToken ct)
    {
        try { return await snmp.WalkAsync(t, root, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (SnmpException) { return null; }   // the MIB is not supported by this switch
    }

    private async Task<(double? Cpu, double? Mem)> CpuAndMemory(SnmpTarget t, Manufacturer vendor, CancellationToken ct)
    {
        double? cpu = null, mem = null;
        if (vendor == Manufacturer.Cisco)
        {
            if (await TryWalk(t, Oids.CiscoCpuTotalEntry, ct) is { } c) cpu = HealthParser.MaxColumn(c, Oids.CiscoCpuTotalEntry, 8);
            if (await TryWalk(t, Oids.CiscoMemoryPoolEntry, ct) is { } m) mem = HealthParser.CiscoMemory(m);
        }
        else if (vendor == Manufacturer.Juniper)
        {
            if (await TryWalk(t, Oids.JuniperOperatingEntry, ct) is { } j) { cpu = HealthParser.MaxColumn(j, Oids.JuniperOperatingEntry, 8); mem = HealthParser.MaxColumn(j, Oids.JuniperOperatingEntry, 11); }
        }
        // Everything else (and any gap above): the standard HOST-RESOURCES-MIB
        if (cpu is null && await TryWalk(t, Oids.HrProcessorEntry, ct) is { } hc) cpu = HealthParser.HrCpu(hc);
        if (mem is null && await TryWalk(t, Oids.HrStorageEntry, ct) is { } hm) mem = HealthParser.HrMemory(hm);
        return (cpu, mem);
    }
}
