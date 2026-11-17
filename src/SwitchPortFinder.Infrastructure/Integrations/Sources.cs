using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwitchPortFinder.Infrastructure.Integrations;

public sealed record DhcpLease(string Mac, string Ip, string? HostName, string? Scope = null, string? State = null, string? Expires = null, string? Description = null, bool Reserved = false);
public sealed record DirectoryComputer(string Name, string? DnsName, string? Os, string? Ou);
public sealed record IntuneDevice(string? Name, string? User, string? Os, string? Compliance, string? WifiMac, string? EthMac, string? Serial);

public interface IDhcpLeaseSource { Task<IReadOnlyList<DhcpLease>> ReadAsync(string server, CancellationToken ct); }
public interface IDirectorySource { Task<IReadOnlyList<DirectoryComputer>> ReadAsync(DirectoryConfig c, string password, CancellationToken ct); }
public interface IIntuneSource { Task<IReadOnlyList<IntuneDevice>> ReadAsync(IntuneConfig c, string secret, CancellationToken ct); }

public sealed class DhcpConfig { public string Server { get; set; } = ""; }
public sealed class DirectoryConfig { public string Server { get; set; } = ""; public int Port { get; set; } = 636; public string BaseDn { get; set; } = ""; public string BindUser { get; set; } = ""; public bool TrustServerCertificate { get; set; } }
public sealed class IntuneConfig { public string TenantId { get; set; } = ""; public string ClientId { get; set; } = ""; }

public static partial class Parse
{
    [GeneratedRegex("^[A-Za-z0-9]([A-Za-z0-9.-]{0,251}[A-Za-z0-9])?$")] private static partial Regex HostRx();
    public static bool IsHost(string? s) => s is { Length: > 0 and <= 253 } && HostRx().IsMatch(s);
    /// <summary>"AA-BB-CC-DD-EE-FF", "aa:bb:cc:dd:ee:ff" or "aabbccddeeff" → "AABBCCDDEEFF"; null when not a MAC.</summary>
    public static string? Mac(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var h = new string(s.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return h.Length == 12 && h != "000000000000" ? h : null;
    }
    /// <summary>First label of a DNS name, as the portal shows host names ("pc-12.corp.local" → "pc-12").</summary>
    public static string? ShortName(string? s) { if (string.IsNullOrWhiteSpace(s)) return null; var t = s.Trim().TrimEnd('.'); var i = t.IndexOf('.'); t = i > 0 ? t[..i] : t; return t.Length is > 0 and <= 100 && !t.Any(char.IsControl) ? t : null; }
    /// <summary>CSV from the PowerShell script: Kind,IPAddress,ClientId,HostName,ScopeId,State,Expires,Description (Kind = lease or reservation). Columns are found by header name.</summary>
    public static List<DhcpLease> LeaseCsv(string csv)
    {
        var list = new List<DhcpLease>(); Dictionary<string, int>? col = null;
        foreach (var raw in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = Fields(raw.TrimEnd('\r'));
            if (col is null) { col = f.Select((n, i) => (n.Trim(), i)).ToDictionary(x => x.Item1, x => x.i, StringComparer.OrdinalIgnoreCase); continue; }
            string? C(string n) => col.TryGetValue(n, out var i) && i < f.Count && !string.IsNullOrWhiteSpace(f[i]) ? f[i].Trim() : null;
            if (Mac(C("ClientId")) is not { } mac || C("IPAddress") is not { } ip || !System.Net.IPAddress.TryParse(ip, out _)) continue;
            list.Add(new DhcpLease(mac, ip, C("HostName"), C("ScopeId"), C("State"), C("Expires"), C("Description"), string.Equals(C("Kind"), "reservation", StringComparison.OrdinalIgnoreCase)));
        }
        return list;
    }
    private static List<string> Fields(string line)
    {
        var r = new List<string>(); var sb = new StringBuilder(); var q = false;
        foreach (var c in line) { if (c == '"') q = !q; else if (c == ',' && !q) { r.Add(sb.ToString()); sb.Clear(); } else sb.Append(c); }
        r.Add(sb.ToString()); return r;
    }
    /// <summary>"CN=PC-1,OU=Sales,OU=Staff,DC=corp,DC=local" → "Sales / Staff".</summary>
    public static string? OuPath(string? dn) => dn is null ? null : string.Join(" / ", dn.Split(',').Select(p => p.Trim()).Where(p => p.StartsWith("OU=", StringComparison.OrdinalIgnoreCase)).Select(p => p[3..])) is { Length: > 0 } s ? s : null;
}

/// <summary>Windows Server DHCP, read-only: PowerShell Get-DhcpServerv4Lease run on the portal server (needs the DHCP RSAT module and a read permission for the service account).</summary>
public sealed class PowerShellDhcpSource : IDhcpLeaseSource
{
    public async Task<IReadOnlyList<DhcpLease>> ReadAsync(string server, CancellationToken ct)
    {
        if (!Parse.IsHost(server)) throw new InvalidOperationException("The DHCP server name is not valid.");
        var script = $"$ErrorActionPreference='Stop'; $sv='{server}'; $sc=Get-DhcpServerv4Scope -ComputerName $sv; " +
            "$l=@($sc | Get-DhcpServerv4Lease -ComputerName $sv | Select-Object @{n='Kind';e={'lease'}},IPAddress,ClientId,HostName,ScopeId,@{n='State';e={[string]$_.AddressState}},@{n='Expires';e={if($_.LeaseExpiryTime){$_.LeaseExpiryTime.ToString('s')}}},@{n='Description';e={''}}); " +
            "$r=@(); try {{ $r=@($sc | Get-DhcpServerv4Reservation -ComputerName $sv | Select-Object @{n='Kind';e={'reservation'}},IPAddress,ClientId,@{n='HostName';e={$_.Name}},ScopeId,@{n='State';e={'Reserved'}},@{n='Expires';e={''}},Description) }} catch {{ }}; " +
            "[Console]::Error.WriteLine('SPF-DIAG scopes=' + @($sc).Count + ' leases=' + $l.Count + ' reservations=' + $r.Count); " +
            "($l + $r) | ConvertTo-Csv -NoTypeInformation";
        var psi = new ProcessStartInfo("powershell.exe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }) psi.ArgumentList.Add(a);
        Process p;
        try { p = Process.Start(psi)!; } catch (Exception) { throw new InvalidOperationException("PowerShell could not be started. Reading Windows DHCP needs Windows PowerShell and the DHCP management tools (RSAT) on the portal server."); }
        using (p)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(90));
            var outT = p.StandardOutput.ReadToEndAsync(cts.Token); var errT = p.StandardError.ReadToEndAsync(cts.Token);
            try { await p.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { try { p.Kill(true); } catch { } throw new InvalidOperationException("The DHCP server did not answer within 90 seconds."); }
            if (p.ExitCode != 0) throw new InvalidOperationException(Explain(await errT));
            var leases = Parse.LeaseCsv(await outT);
            if (leases.Count == 0) throw new InvalidOperationException(ExplainEmpty(await errT));
            return leases;
        }
    }
    /// <summary>Nothing came back although PowerShell did not fail: say how many scopes the portal could see, which tells permission problems from empty scopes.</summary>
    public static string ExplainEmpty(string err)
    {
        var m = System.Text.RegularExpressions.Regex.Match(err ?? "", @"SPF-DIAG scopes=(\d+) leases=(\d+) reservations=(\d+)");
        if (!m.Success) return "The DHCP server returned no leases. " + Clip(err ?? "");
        var scopes = int.Parse(m.Groups[1].Value); var leases = int.Parse(m.Groups[2].Value);
        if (scopes == 0) return "The portal could not see any DHCP scope on that server. This usually means the portal server's computer account is not allowed to read it: add that computer to the \"DHCP Users\" group, then restart the portal server (or run klist -li 0x3e7 purge) and try again.";
        if (leases == 0) return $"The portal sees {scopes} scope(s) but they returned no leases (reservations: {m.Groups[3].Value}). Check that the scopes have active leases, and that the account has read access to them.";
        return $"The server returned {leases} lease(s) but the portal could not read the answer. Please report this.";
    }

    /// <summary>A plain-language reason and fix for the two most common PowerShell failures, otherwise the (shortened) error.</summary>
    public static string Explain(string err)
    {
        if (err.Contains("is not recognized as the name of a cmdlet", StringComparison.OrdinalIgnoreCase))
            return "The DHCP management tools are not installed on the portal server. On that server run, as Administrator:  Install-WindowsFeature RSAT-DHCP  (Windows Server) and press Run now again.";
        if (err.Contains("access is denied", StringComparison.OrdinalIgnoreCase) || err.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase) || err.Contains("not authorized", StringComparison.OrdinalIgnoreCase))
            return "The DHCP server refused the connection. Add the portal server's computer account (for example AMG\\SERVERNAME$) to the \"DHCP Users\" group on the DHCP server, wait a few minutes, and run again.";
        if (err.Contains("RPC server is unavailable", StringComparison.OrdinalIgnoreCase) || err.Contains("cannot connect", StringComparison.OrdinalIgnoreCase))
            return "The DHCP server could not be reached. Check the server name and that the portal server can reach it (RPC, TCP 135 and the dynamic RPC ports).";
        return "The DHCP server could not be read: " + Clip(err);
    }

    private static string Clip(string s) { s = s.Trim().Replace('\r', ' ').Replace('\n', ' '); return s.Length > 300 ? s[..300] : s; }
}

/// <summary>Active Directory over LDAPS, read-only: lists computer objects with a service account.</summary>
public sealed class LdapDirectorySource : IDirectorySource
{
    public Task<IReadOnlyList<DirectoryComputer>> ReadAsync(DirectoryConfig c, string password, CancellationToken ct) => Task.Run<IReadOnlyList<DirectoryComputer>>(() =>
    {
        using var conn = new System.DirectoryServices.Protocols.LdapConnection(new System.DirectoryServices.Protocols.LdapDirectoryIdentifier(c.Server, c.Port), new System.Net.NetworkCredential(c.BindUser, password), System.DirectoryServices.Protocols.AuthType.Basic);
        conn.SessionOptions.ProtocolVersion = 3; conn.SessionOptions.SecureSocketLayer = true; conn.Timeout = TimeSpan.FromSeconds(30);
        if (c.TrustServerCertificate) conn.SessionOptions.VerifyServerCertificate = (_, _) => true;
        try { conn.Bind(); } catch (System.DirectoryServices.Protocols.LdapException ex) { throw new InvalidOperationException("The directory sign-in failed (" + ex.Message + "). Check the server name, the LDAPS port, the account and the certificate."); }
        var list = new List<DirectoryComputer>(); byte[] cookie = [];
        do
        {
            ct.ThrowIfCancellationRequested();
            var req = new System.DirectoryServices.Protocols.SearchRequest(c.BaseDn, "(&(objectCategory=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))", System.DirectoryServices.Protocols.SearchScope.Subtree, "name", "dNSHostName", "operatingSystem", "distinguishedName");
            var page = new System.DirectoryServices.Protocols.PageResultRequestControl(500) { Cookie = cookie }; req.Controls.Add(page);
            var resp = (System.DirectoryServices.Protocols.SearchResponse)conn.SendRequest(req);
            foreach (System.DirectoryServices.Protocols.SearchResultEntry e in resp.Entries)
            {
                string? A(string n) => e.Attributes.Contains(n) && e.Attributes[n].Count > 0 ? e.Attributes[n][0]?.ToString() : null;
                if (A("name") is { } name) list.Add(new DirectoryComputer(name, A("dNSHostName"), A("operatingSystem"), Parse.OuPath(A("distinguishedName"))));
            }
            cookie = resp.Controls.OfType<System.DirectoryServices.Protocols.PageResultResponseControl>().FirstOrDefault()?.Cookie ?? [];
        } while (cookie.Length > 0 && list.Count < 100_000);
        return list;
    }, ct);
}

/// <summary>Microsoft Intune through Microsoft Graph with an app registration (client credentials), read-only. Only the two Microsoft hosts are ever contacted.</summary>
public sealed class GraphIntuneSource(IHttpClientFactory http) : IIntuneSource
{
    private const string GraphHost = "graph.microsoft.com";
    public async Task<IReadOnlyList<IntuneDevice>> ReadAsync(IntuneConfig c, string secret, CancellationToken ct)
    {
        if (!Guid.TryParse(c.TenantId, out _)) throw new InvalidOperationException("The tenant ID must be a GUID.");
        var cl = http.CreateClient("intune"); cl.Timeout = TimeSpan.FromSeconds(60);
        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{c.TenantId}/oauth2/v2.0/token")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = c.ClientId, ["client_secret"] = secret, ["scope"] = "https://graph.microsoft.com/.default", ["grant_type"] = "client_credentials" }) };
        using var tr = await cl.SendAsync(tokenReq, ct);
        if (!tr.IsSuccessStatusCode) throw new InvalidOperationException($"Microsoft sign-in failed (HTTP {(int)tr.StatusCode}). Check the tenant ID, client ID and secret.");
        var token = JsonDocument.Parse(await tr.Content.ReadAsStringAsync(ct)).RootElement.GetProperty("access_token").GetString()!;
        var list = new List<IntuneDevice>();
        var url = $"https://{GraphHost}/v1.0/deviceManagement/managedDevices?$select=deviceName,userPrincipalName,operatingSystem,osVersion,complianceState,wiFiMacAddress,ethernetMacAddress,serialNumber&$top=500";
        for (var pages = 0; url != null && pages < 200; pages++)
        {
            using var rq = new HttpRequestMessage(HttpMethod.Get, url); rq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var r = await cl.SendAsync(rq, ct);
            if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"Intune could not be read (HTTP {(int)r.StatusCode}). The app needs the read-only application permission DeviceManagementManagedDevices.Read.All with admin consent.");
            var root = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct)).RootElement;
            foreach (var d in root.GetProperty("value").EnumerateArray())
            {
                string? S(string n) => d.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                list.Add(new IntuneDevice(S("deviceName"), S("userPrincipalName"), $"{S("operatingSystem")} {S("osVersion")}".Trim(), S("complianceState"), S("wiFiMacAddress"), S("ethernetMacAddress"), S("serialNumber")));
            }
            var next = root.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() : null;
            url = next is not null && Uri.TryCreate(next, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == GraphHost ? next : null;   // never follow a link to another host
        }
        return list;
    }
}
