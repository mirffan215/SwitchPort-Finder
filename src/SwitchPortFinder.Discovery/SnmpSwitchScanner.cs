using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Discovery.Parsing;
using SwitchPortFinder.Discovery.Providers;
using SwitchPortFinder.Domain;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Discovery;

public sealed class SnmpSwitchScanner(ISnmpClient snmp, IOptions<SnmpOptions> snmpOpt, IOptions<DiscoveryOptions> discOpt, ILogger<SnmpSwitchScanner> log) : ISwitchScanner
{
    public async Task<ScanResult> ScanAsync(ScanRequest req, Func<StepResult, Task> onStep, CancellationToken ct)
    {
        var res = new ScanResult();
        var o = snmpOpt.Value;
        var target = new SnmpTarget(System.Net.IPAddress.Parse(req.Ip), o.Port, req.Credentials, o.TimeoutMs, o.Retries, o.MaxRepetitions);

        async Task Report(StepResult s) { res.Steps.Add(s); await onStep(s); }
        async Task Run(string step, Func<Task<string?>> body)
        {
            await onStep(new StepResult(step, StepState.Running));
            try
            {
                var warn = await body();
                await Report(new StepResult(step, warn is null ? StepState.Ok : StepState.Warning, warn));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (SnmpException ex)
            {
                var msg = SnmpConnectionService.Friendly(ex);
                res.Errors.Add(new ScanError(step, ex.Failure.ToString(), msg));
                log.LogWarning("OidUnavailable or SNMP error in {Step} for {Ip}: {Failure}", step, req.Ip, ex.Failure);
                await Report(new StepResult(step, StepState.Failed, msg));
                if (step == Steps.System) res.Reachable = false;
            }
            catch (Exception ex)
            {
                res.Errors.Add(new ScanError(step, "Unexpected", "Unexpected error in this step. See server log."));
                log.LogError(ex, "Unexpected error in {Step} for {Ip}", step, req.Ip);
                await Report(new StepResult(step, StepState.Failed, "Unexpected error in this step."));
            }
        }

        // 1. System (also decides reachability)
        await Run(Steps.System, async () =>
        {
            var vbs = await snmp.GetAsync(target, [Oids.SysName, Oids.SysDescr, Oids.SysObjectId, Oids.SysUpTime], ct);
            string? T(string oid) => vbs.FirstOrDefault(v => v.Oid == oid) is { Value.IsMissing: false } v ? v.Value.ToString() : null;
            var objId = T(Oids.SysObjectId);
            var (model, serial) = await EntityInfo(target, ct);
            res.System = new SystemInfo(T(Oids.SysName), T(Oids.SysDescr), objId,
                vbs.FirstOrDefault(v => v.Oid == Oids.SysUpTime)?.Value.Number ?? 0, model, serial);
            res.DetectedManufacturer = VendorDetector.Detect(objId);
            return null;
        });
        if (!res.Reachable) return res;

        var effective = VendorDetector.Effective(req.Selected, res.DetectedManufacturer);
        var provider = ProviderRegistry.For(effective);
        var basePortMap = new Dictionary<int, int>();

        // 2. Interfaces
        await Run(Steps.Interfaces, async () =>
        {
            var ifT = await snmp.WalkAsync(target, Oids.IfTable, ct);
            IReadOnlyList<SnmpVarBind> ifX = [];
            string? warn = null;
            try { ifX = await snmp.WalkAsync(target, Oids.IfXTable, ct); }
            catch (SnmpException) { warn = "ifXTable unavailable; using ifDescr names and ifSpeed."; res.Errors.Add(new ScanError(Steps.Interfaces, "OidUnavailable", warn)); }
            res.Interfaces.AddRange(InterfaceParser.Parse(ifT, ifX));
            res.SwitchMacs.UnionWith(InterfaceParser.AllPhysAddresses(ifT));
            if (res.Interfaces.Count == 0) { warn = "No Ethernet interfaces found."; res.Errors.Add(new ScanError(Steps.Interfaces, "Empty", warn)); }
            return warn;
        });

        // 3. VLANs (+ bridge port map + PVID)
        await Run(Steps.Vlans, async () =>
        {
            string? warn = null;
            try { basePortMap = FdbParser.BasePortMap(await snmp.WalkAsync(target, Oids.Dot1dBasePortIfIndex, ct)); }
            catch (SnmpException) { warn = "Bridge port table unavailable."; }
            try
            {
                foreach (var (vlan, _) in OidTable.Column(await snmp.WalkAsync(target, Oids.Dot1qVlanStaticName, ct), Oids.Dot1qVlanStaticName)) res.Vlans.Add(vlan);
            }
            catch (SnmpException) { /* try vendor MIB below */ }
            if (res.Vlans.Count == 0 && provider.HasCiscoVlanMibs)
            {
                try { foreach (var (vlan, v) in OidTable.Column(await snmp.WalkAsync(target, Oids.CiscoVtpVlanState, ct), Oids.CiscoVtpVlanState)) if (v.Number == 1) res.Vlans.Add(vlan); }
                catch (SnmpException) { }
            }
            try
            {
                foreach (var (bp, v) in OidTable.Column(await snmp.WalkAsync(target, Oids.Dot1qPvid, ct), Oids.Dot1qPvid))
                    if (basePortMap.TryGetValue(bp, out var ifx)) res.PvidByIfIndex[ifx] = (int)v.Number;
            }
            catch (SnmpException) { }
            if (res.PvidByIfIndex.Count == 0 && provider.HasCiscoVlanMibs)
            {
                try { foreach (var (ifx, v) in OidTable.Column(await snmp.WalkAsync(target, Oids.CiscoVmVlan, ct), Oids.CiscoVmVlan)) res.PvidByIfIndex[ifx] = (int)v.Number; }
                catch (SnmpException) { }
            }
            res.Vlans.Sort();
            if (res.Vlans.Count == 0) warn ??= "No VLAN information available.";
            if (warn is not null) res.Errors.Add(new ScanError(Steps.Vlans, "OidUnavailable", warn));
            return warn;
        });

        // 4. MAC table
        var own = res.SwitchMacs.Union(res.Interfaces.Where(i => i.Mac is not null).Select(i => i.Mac!)).ToHashSet();
        await Run(Steps.MacTable, async () =>
        {
            var all = new List<FdbEntry>();
            string source = "";
            var fdbToVlan = new Dictionary<int, int>();
            try { fdbToVlan = FdbParser.FdbToVlan(await snmp.WalkAsync(target, Oids.Dot1qVlanFdbId, ct)); } catch (SnmpException) { }
            try
            {
                all = FdbParser.ParseDot1q(await snmp.WalkAsync(target, Oids.Dot1qTpFdbTable, ct), basePortMap, fdbToVlan);
                if (all.Count > 0) source = "Q-BRIDGE-MIB";
            }
            catch (SnmpException) { res.Errors.Add(new ScanError(Steps.MacTable, "OidUnavailable", "Q-BRIDGE-MIB dot1qTpFdbTable unavailable.")); }

            if (all.Count == 0 && !provider.UsesPerVlanFdb)
            {
                try
                {
                    all = FdbParser.ParseDot1d(await snmp.WalkAsync(target, Oids.Dot1dTpFdbTable, ct), basePortMap, null);
                    if (all.Count > 0) source = "BRIDGE-MIB";
                }
                catch (SnmpException) { res.Errors.Add(new ScanError(Steps.MacTable, "OidUnavailable", "BRIDGE-MIB dot1dTpFdbTable unavailable.")); }
            }

            // Cisco (and last resort for others): BRIDGE-MIB is per VLAN, addressed with community@vlan / context vlan-N.
            if (all.Count == 0 && res.Vlans.Count > 0)
            {
                all = await PerVlanFdb(target, res.Vlans, ct);
                if (all.Count > 0) source = "BRIDGE-MIB (per-VLAN)";
            }

            res.FdbSource = source;
            res.Fdb.AddRange(FdbParser.Usable(all, own));
            if (res.Fdb.Count == 0)
            {
                const string m = "MAC table is empty or unsupported.";
                res.Errors.Add(new ScanError(Steps.MacTable, "Empty", m));
                return m;
            }
            return null;
        });

        // 5. ARP (only on Layer 3 devices / flagged ARP sources)
        await Run(Steps.Arp, async () =>
        {
            var fwd = (await TryGet(target, Oids.IpForwarding, ct))?.Number == 1;
            if (!req.UseAsArpSource && !fwd) { return null; }   // normal for access switches: skipped silently
            List<ArpInfo> arp = [];
            try { arp = ArpParser.ParsePhysical(await snmp.WalkAsync(target, Oids.IpNetToPhysicalTable, ct)); } catch (SnmpException) { }
            if (arp.Count == 0)
            {
                try { arp = ArpParser.ParseMedia(await snmp.WalkAsync(target, Oids.IpNetToMediaTable, ct)); }
                catch (SnmpException) { res.Errors.Add(new ScanError(Steps.Arp, "OidUnavailable", "ARP tables unavailable.")); }
            }
            res.Arp.AddRange(arp);
            res.IsLayer3 = fwd && arp.Count > 0;
            if (arp.Count == 0) { const string m = "ARP table is empty."; res.Errors.Add(new ScanError(Steps.Arp, "Empty", m)); return m; }
            return null;
        });

        // 6. LLDP (primary neighbor protocol for every manufacturer)
        await Run(Steps.Lldp, async () =>
        {
            IReadOnlyList<SnmpVarBind> rem, man, loc;
            try { rem = await snmp.WalkAsync(target, Oids.LldpRemTable, ct); }
            catch (SnmpException) { return LldpUnavailable(res); }
            try { man = await snmp.WalkAsync(target, Oids.LldpRemManAddrTable, ct); } catch (SnmpException) { man = []; }
            try { loc = await snmp.WalkAsync(target, Oids.LldpLocPortTable, ct); } catch (SnmpException) { loc = []; }
            var chassis = await TryGetRaw(target, Oids.LldpLocChassisId, ct);
            var sub = await TryGet(target, Oids.LldpLocChassisIdSubtype, ct);
            if (chassis is { Length: 6 } && sub?.Number == 4 && MacAddress.FromBytes(chassis) is { } cm) res.SwitchMacs.Add(cm);
            res.LldpCollected = true;
            if (rem.Count == 0 && chassis is null) return LldpUnavailable(res);   // neither neighbors nor a local chassis id: LLDP is off or unsupported
            res.Neighbors.AddRange(NeighborParser.Lldp(rem, man, NeighborParser.LocalPorts(loc), res.Interfaces));
            return null;
        });

        // 7. CDP (Cisco only; optional, never required)
        if (provider.SupportsCdp)
        {
            await Run(Steps.Cdp, async () =>
            {
                try
                {
                    var cdp = NeighborParser.Cdp(await snmp.WalkAsync(target, Oids.CdpCacheTable, ct), res.Interfaces);
                    res.CdpCollected = true;
                    res.Neighbors.AddRange(cdp);
                    return null;
                }
                catch (SnmpException)
                {
                    const string m = "CDP unavailable.";
                    res.Errors.Add(new ScanError(Steps.Cdp, "OidUnavailable", m));
                    log.LogInformation("OidUnavailable: CDP cache on {Ip}", req.Ip);
                    return m;
                }
            });
        }
        else await onStep(new StepResult(Steps.Cdp, StepState.Skipped, "CDP does not apply to this manufacturer."));
        return res;
    }

    private static string LldpUnavailable(ScanResult res)
    {
        const string m = "LLDP unavailable. Continuing with MAC and interface discovery...";
        res.Errors.Add(new ScanError(Steps.Lldp, "OidUnavailable", m));
        return m;
    }

    private async Task<byte[]?> TryGetRaw(SnmpTarget t, string oid, CancellationToken ct) => (await TryGet(t, oid, ct))?.Bytes;

    /// <summary>Per-VLAN FDB. Short timeouts and bounded parallelism keep this fast; VLANs with no data are skipped.</summary>
    internal async Task<List<FdbEntry>> PerVlanFdb(SnmpTarget baseTarget, IEnumerable<int> vlans, CancellationToken ct)
    {
        var opt = discOpt.Value;
        var all = new List<FdbEntry>();
        using var gate = new SemaphoreSlim(Math.Max(1, opt.PerVlanParallelism));
        var tasks = vlans.Where(v => v is not (>= 1002 and <= 1005)).Select(async vlan =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var t = baseTarget with { Credentials = VlanCredentials(baseTarget.Credentials, vlan), TimeoutMs = Math.Min(baseTarget.TimeoutMs, opt.PerVlanTimeoutMs), Retries = 0 };
                var map = FdbParser.BasePortMap(await snmp.WalkAsync(t, Oids.Dot1dBasePortIfIndex, ct));
                if (map.Count == 0) return;
                var rows = FdbParser.ParseDot1d(await snmp.WalkAsync(t, Oids.Dot1dTpFdbTable, ct), map, vlan);
                lock (all) all.AddRange(rows);
            }
            catch (SnmpException ex) { log.LogDebug("VLAN {Vlan}: no FDB data ({Failure})", vlan, ex.Failure); }
            finally { gate.Release(); }
        }).ToList();
        await Task.WhenAll(tasks);
        return all;
    }

    /// <summary>SNMPv2c: community@vlan. SNMPv3: context name vlan-N.</summary>
    public static SnmpCredentials VlanCredentials(SnmpCredentials c, int vlan) =>
        c.Version == SnmpVersion.V2c ? c with { Community = $"{c.Community}@{vlan}" } : c with { ContextName = $"vlan-{vlan}" };

    private async Task<SnmpValue?> TryGet(SnmpTarget t, string oid, CancellationToken ct)
    {
        try { var r = await snmp.GetAsync(t, [oid], ct); return r.FirstOrDefault() is { Value.IsMissing: false } v ? v.Value : null; }
        catch (SnmpException) { return null; }
    }

    private async Task<(string? model, string? serial)> EntityInfo(SnmpTarget t, CancellationToken ct)
    {
        string? First(IEnumerable<SnmpVarBind> v, string root) =>
            OidTable.Column(v, root).Where(c => !c.Value.IsMissing && c.Value.ToString().Trim().Length > 0).Select(c => c.Value.ToString().Trim()).FirstOrDefault();
        try
        {
            var m = First(await snmp.WalkAsync(t, Oids.EntPhysicalModel, ct), Oids.EntPhysicalModel);
            var s = First(await snmp.WalkAsync(t, Oids.EntPhysicalSerial, ct), Oids.EntPhysicalSerial);
            return (m, s);
        }
        catch (SnmpException) { return (null, null); }   // optional; many devices lack ENTITY-MIB
    }
}
