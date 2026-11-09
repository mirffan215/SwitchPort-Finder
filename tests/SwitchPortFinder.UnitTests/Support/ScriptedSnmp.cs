using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.TestSupport;

/// <summary>
/// Replays recorded/synthetic SNMP data without a network. Walks are keyed by (ip, credential key, root OID) where the credential key is
/// the community (v2c) or context name (v3) — this is what makes Cisco per-VLAN indexing testable. Missing walks return an empty subtree.
/// </summary>
public sealed class ScriptedSnmp : ISnmpClient
{
    public Dictionary<string, Dictionary<string, List<SnmpVarBind>>> Walks { get; } = [];   // key: ip|credKey → root → rows
    public Dictionary<string, SnmpValue> Gets { get; } = [];                                  // key: ip|oid
    public HashSet<string> Unreachable { get; } = [];
    public HashSet<string> ThrowOnWalk { get; } = [];
    public HashSet<string> ThrowUnexpected { get; } = [];                                     // key: ip|root → InvalidOperationException (a bug, not an SNMP error)
    public HashSet<string> AuthFail { get; } = [];                                            // ip → SNMPv3 authentication failure                                         // key: ip|root → SnmpException
    public List<(string Ip, string CredKey, string Root)> WalkLog { get; } = [];

    public static string CredKey(SnmpCredentials c) => c.Version == SnmpVersion.V2c ? c.Community ?? "" : c.ContextName ?? "";

    public Task<IReadOnlyList<SnmpVarBind>> GetAsync(SnmpTarget t, IEnumerable<string> oids, CancellationToken ct)
    {
        var ip = t.Address.ToString();
        if (Unreachable.Contains(ip)) throw new SnmpException(SnmpFailure.Timeout, "timeout");
        if (AuthFail.Contains(ip)) throw new SnmpException(SnmpFailure.AuthenticationFailed, "auth");
        IReadOnlyList<SnmpVarBind> r = oids.Select(o => new SnmpVarBind(o, Gets.TryGetValue($"{ip}|{o}", out var v) ? v : new SnmpValue(SnmpValueKind.NoSuchInstance))).ToList();
        return Task.FromResult(r);
    }

    public Task<IReadOnlyList<SnmpVarBind>> GetNextAsync(SnmpTarget t, IEnumerable<string> o, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<SnmpVarBind>> WalkAsync(SnmpTarget t, string root, CancellationToken ct)
    {
        var ip = t.Address.ToString(); var key = CredKey(t.Credentials);
        lock (WalkLog) WalkLog.Add((ip, key, root));
        if (Unreachable.Contains(ip)) throw new SnmpException(SnmpFailure.Timeout, "timeout");
        if (ThrowOnWalk.Contains($"{ip}|{root}")) throw new SnmpException(SnmpFailure.Timeout, "timeout");
        if (ThrowUnexpected.Contains($"{ip}|{root}")) throw new InvalidOperationException("boom-internal-detail-XYZ");
        IReadOnlyList<SnmpVarBind> r = Walks.TryGetValue($"{ip}|{key}", out var byRoot) && byRoot.TryGetValue(root, out var rows) ? rows : [];
        return Task.FromResult(r);
    }

    public void Clear() { ThrowUnexpected.Clear(); AuthFail.Clear(); Walks.Clear(); Gets.Clear(); Unreachable.Clear(); ThrowOnWalk.Clear(); WalkLog.Clear(); }

    // ---------- fluent setup ----------
    public ScriptedSnmp WithSystem(string ip, string name, string objectId, string descr = "test switch")
    {
        Gets[$"{ip}|{Oids.SysName}"] = Txt(name); Gets[$"{ip}|{Oids.SysDescr}"] = Txt(descr);
        Gets[$"{ip}|{Oids.SysObjectId}"] = new SnmpValue(SnmpValueKind.ObjectId, 0, null, objectId);
        Gets[$"{ip}|{Oids.SysUpTime}"] = new SnmpValue(SnmpValueKind.TimeTicks, 1000);
        return this;
    }

    public ScriptedSnmp Walk(string ip, string credKey, string root, IEnumerable<SnmpVarBind> rows)
    {
        var k = $"{ip}|{credKey}";
        if (!Walks.TryGetValue(k, out var d)) Walks[k] = d = [];
        if (!d.TryGetValue(root, out var l)) d[root] = l = [];
        l.AddRange(rows);
        return this;
    }

    public static SnmpValue Txt(string s) => new(SnmpValueKind.OctetString, 0, System.Text.Encoding.UTF8.GetBytes(s), s);
    public static SnmpValue Int(long n) => new(SnmpValueKind.Integer, n);
    public static SnmpValue Bytes(byte[] b) => new(SnmpValueKind.OctetString, 0, b, Convert.ToHexString(b));
    public static byte[] MacBytes(string mac) => Convert.FromHexString(mac.Replace(":", "").Replace("-", ""));
    public static string Octets(string mac) => string.Join('.', MacBytes(mac).Select(b => (int)b));
    public static SnmpVarBind Vb(string oid, SnmpValue v) => new(oid, v);

    // ---------- table builders ----------
    public static IEnumerable<SnmpVarBind> IfTable(params (int idx, string name, int type, bool up, string? mac)[] rows)
    {
        foreach (var r in rows)
        {
            yield return Vb($"{Oids.IfTable}.2.{r.idx}", Txt("descr-" + r.name));
            yield return Vb($"{Oids.IfTable}.3.{r.idx}", Int(r.type));
            yield return Vb($"{Oids.IfTable}.4.{r.idx}", Int(1500));
            yield return Vb($"{Oids.IfTable}.5.{r.idx}", new SnmpValue(SnmpValueKind.Gauge, 1_000_000_000));
            if (r.mac is not null) yield return Vb($"{Oids.IfTable}.6.{r.idx}", Bytes(MacBytes(r.mac)));
            yield return Vb($"{Oids.IfTable}.7.{r.idx}", Int(1));
            yield return Vb($"{Oids.IfTable}.8.{r.idx}", Int(r.up ? 1 : 2));
        }
    }

    public static IEnumerable<SnmpVarBind> IfXTable(params (int idx, string name, long highSpeed, string alias)[] rows)
    {
        foreach (var r in rows)
        {
            yield return Vb($"{Oids.IfXTable}.1.{r.idx}", Txt(r.name));
            yield return Vb($"{Oids.IfXTable}.15.{r.idx}", new SnmpValue(SnmpValueKind.Gauge, r.highSpeed));
            yield return Vb($"{Oids.IfXTable}.18.{r.idx}", Txt(r.alias));
        }
    }

    public static IEnumerable<SnmpVarBind> BasePortMap(params (int bridgePort, int ifIndex)[] rows) =>
        rows.Select(r => Vb($"{Oids.Dot1dBasePortIfIndex}.{r.bridgePort}", Int(r.ifIndex)));

    /// <summary>status: 3 learned, 4 self, 5 mgmt.</summary>
    public static IEnumerable<SnmpVarBind> Dot1dFdb(params (string mac, int port, int status)[] rows)
    {
        foreach (var r in rows)
        {
            yield return Vb($"{Oids.Dot1dTpFdbTable}.1.{Octets(r.mac)}", Bytes(MacBytes(r.mac)));
            yield return Vb($"{Oids.Dot1dTpFdbTable}.2.{Octets(r.mac)}", Int(r.port));
            yield return Vb($"{Oids.Dot1dTpFdbTable}.3.{Octets(r.mac)}", Int(r.status));
        }
    }

    public static IEnumerable<SnmpVarBind> Dot1qFdb(params (int fdbId, string mac, int port, int status)[] rows)
    {
        foreach (var r in rows)
        {
            yield return Vb($"{Oids.Dot1qTpFdbTable}.2.{r.fdbId}.{Octets(r.mac)}", Int(r.port));
            yield return Vb($"{Oids.Dot1qTpFdbTable}.3.{r.fdbId}.{Octets(r.mac)}", Int(r.status));
        }
    }

    public static IEnumerable<SnmpVarBind> ArpMedia(params (int ifIndex, string ip, string mac, int type)[] rows)
    {
        foreach (var r in rows)
        {
            yield return Vb($"{Oids.IpNetToMediaTable}.2.{r.ifIndex}.{r.ip}", Bytes(MacBytes(r.mac)));
            yield return Vb($"{Oids.IpNetToMediaTable}.4.{r.ifIndex}.{r.ip}", Int(r.type));
        }
    }

    /// <summary>state: 1 reachable, 2 stale, 5 invalid, 7 incomplete.</summary>
    public static IEnumerable<SnmpVarBind> ArpPhysical(params (int ifIndex, string ip, string mac, int state)[] rows)
    {
        foreach (var r in rows)
        {
            yield return Vb($"{Oids.IpNetToPhysicalTable}.4.{r.ifIndex}.1.4.{r.ip}", Bytes(MacBytes(r.mac)));
            yield return Vb($"{Oids.IpNetToPhysicalTable}.7.{r.ifIndex}.1.4.{r.ip}", Int(r.state));
        }
    }

    // ---------- LLDP / CDP builders ----------
    public static IEnumerable<SnmpVarBind> LldpLocPort(int localPort, int subtype, string id, string desc = "")
    {
        yield return Vb($"{Oids.LldpLocPortTable}.2.{localPort}", Int(subtype));
        yield return Vb($"{Oids.LldpLocPortTable}.3.{localPort}", Txt(id));
        yield return Vb($"{Oids.LldpLocPortTable}.4.{localPort}", Txt(desc));
    }

    /// <param name="caps">first octet of the capability bitmap, e.g. 0x20 = bridge, 0x28 = bridge+router, 0x04 = telephone.</param>
    public static IEnumerable<SnmpVarBind> LldpRem(int localPort, int remIdx, int chassisSub, byte[] chassis, int portSub, byte[] port, string sysName, string portDesc = "", string sysDescr = "", byte caps = 0)
    {
        var i = $"1.{localPort}.{remIdx}";
        yield return Vb($"{Oids.LldpRemTable}.4.{i}", Int(chassisSub));
        yield return Vb($"{Oids.LldpRemTable}.5.{i}", Bytes(chassis));
        yield return Vb($"{Oids.LldpRemTable}.6.{i}", Int(portSub));
        yield return Vb($"{Oids.LldpRemTable}.7.{i}", Bytes(port));
        yield return Vb($"{Oids.LldpRemTable}.8.{i}", Txt(portDesc));
        yield return Vb($"{Oids.LldpRemTable}.9.{i}", Txt(sysName));
        yield return Vb($"{Oids.LldpRemTable}.10.{i}", Txt(sysDescr));
        yield return Vb($"{Oids.LldpRemTable}.11.{i}", Bytes([caps]));
        yield return Vb($"{Oids.LldpRemTable}.12.{i}", Bytes([caps]));
    }

    public static IEnumerable<SnmpVarBind> LldpMan(int localPort, int remIdx, string ipv4)
    {
        var o = string.Join('.', ipv4.Split('.'));
        yield return Vb($"{Oids.LldpRemManAddrTable}.3.1.{localPort}.{remIdx}.1.4.{o}", Int(2));
    }

    public static IEnumerable<SnmpVarBind> CdpRow(int ifIndex, int devIdx, string? ip, string deviceId, string devicePort, string platform, uint caps)
    {
        var i = $"{ifIndex}.{devIdx}";
        if (ip is not null) { yield return Vb($"{Oids.CdpCacheTable}.3.{i}", Int(1)); yield return Vb($"{Oids.CdpCacheTable}.4.{i}", Bytes(System.Net.IPAddress.Parse(ip).GetAddressBytes())); }
        yield return Vb($"{Oids.CdpCacheTable}.5.{i}", Txt("IOS"));
        yield return Vb($"{Oids.CdpCacheTable}.6.{i}", Txt(deviceId));
        yield return Vb($"{Oids.CdpCacheTable}.7.{i}", Txt(devicePort));
        yield return Vb($"{Oids.CdpCacheTable}.8.{i}", Txt(platform));
        yield return Vb($"{Oids.CdpCacheTable}.9.{i}", Bytes([(byte)(caps >> 24), (byte)(caps >> 16), (byte)(caps >> 8), (byte)caps]));
    }
}
