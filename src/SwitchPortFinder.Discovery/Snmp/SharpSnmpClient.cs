using System.Net;
using System.Net.Sockets;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;
using Microsoft.Extensions.Logging;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Enums;
using AppSnmpException = SwitchPortFinder.Application.Snmp.SnmpException;
using SharpTimeout = Lextm.SharpSnmpLib.Messaging.TimeoutException;

namespace SwitchPortFinder.Discovery.Snmp;

/// <summary>
/// ISnmpClient backed by Lextm.SharpSnmpLib. Only read operations (GET / GETNEXT / GETBULK / walk) are used;
/// the Set PDU is never constructed anywhere in this code base. Credentials are never logged.
/// </summary>
public sealed class SharpSnmpClient(ILogger<SharpSnmpClient> log) : ISnmpClient
{
    public Task<IReadOnlyList<SnmpVarBind>> GetAsync(SnmpTarget target, IEnumerable<string> oids, CancellationToken ct) =>
        WithRetry(target, ct, async token =>
        {
            var vars = oids.Select(o => new Variable(new ObjectIdentifier(o))).ToList();
            var ep = new IPEndPoint(target.Address, target.Port);
            if (target.Credentials.Version == SnmpVersion.V2c)
                return Convert(await Messenger.GetAsync(VersionCode.V2, ep, new OctetString(target.Credentials.Community ?? ""), vars, token));
            var (privacy, report) = await PrepareV3(target, ep, token);
            var req = new GetRequestMessage(VersionCode.V3, Messenger.NextMessageId, Messenger.NextRequestId,
                new OctetString(target.Credentials.Username ?? ""), new OctetString(target.Credentials.ContextName ?? ""),
                vars, privacy, Messenger.MaxMessageSize, report);
            return ConvertResponse(await req.GetResponseAsync(ep, token));
        });

    public Task<IReadOnlyList<SnmpVarBind>> GetNextAsync(SnmpTarget target, IEnumerable<string> oids, CancellationToken ct) =>
        WithRetry(target, ct, async token =>
        {
            var vars = oids.Select(o => new Variable(new ObjectIdentifier(o))).ToList();
            var ep = new IPEndPoint(target.Address, target.Port);
            if (target.Credentials.Version == SnmpVersion.V2c)
            {
                var req2 = new GetNextRequestMessage(Messenger.NextRequestId, VersionCode.V2, new OctetString(target.Credentials.Community ?? ""), vars);
                return ConvertResponse(await req2.GetResponseAsync(ep, token));
            }
            var (privacy, report) = await PrepareV3(target, ep, token);
            var req = new GetNextRequestMessage(VersionCode.V3, Messenger.NextMessageId, Messenger.NextRequestId,
                new OctetString(target.Credentials.Username ?? ""), new OctetString(target.Credentials.ContextName ?? ""),
                vars, privacy, Messenger.MaxMessageSize, report);
            return ConvertResponse(await req.GetResponseAsync(ep, token));
        });

    public Task<IReadOnlyList<SnmpVarBind>> WalkAsync(SnmpTarget target, string rootOid, CancellationToken ct) =>
        WithRetry(target, ct, async token =>
        {
            var list = new List<Variable>();
            var ep = new IPEndPoint(target.Address, target.Port);
            var root = new ObjectIdentifier(rootOid);
            if (target.Credentials.Version == SnmpVersion.V2c)
            {
                var community = new OctetString(target.Credentials.Community ?? "");
                try
                {
                    await Messenger.BulkWalkAsync(VersionCode.V2, ep, community, OctetString.Empty, root, list, target.MaxRepetitions,
                        WalkMode.WithinSubtree, new DefaultPrivacyProvider(DefaultAuthenticationProvider.Instance), null!, token);
                }
                catch (Exception ex) when (ex is ErrorException or Lextm.SharpSnmpLib.SnmpException)
                {
                    log.LogDebug("GETBULK failed for {Address}, falling back to GETNEXT walk", target.Address);
                    list.Clear();
                    await Messenger.WalkAsync(VersionCode.V2, ep, community, root, list, WalkMode.WithinSubtree, token);
                }
            }
            else
            {
                var (privacy, report) = await PrepareV3(target, ep, token);
                await Messenger.BulkWalkAsync(VersionCode.V3, ep, new OctetString(target.Credentials.Username ?? ""),
                    new OctetString(target.Credentials.ContextName ?? ""), root, list, target.MaxRepetitions,
                    WalkMode.WithinSubtree, privacy, report, token);
            }
            return (IReadOnlyList<SnmpVarBind>)list.Select(ToVarBind).ToList();
        });

    // ---------------- helpers ----------------

    private async Task<IReadOnlyList<SnmpVarBind>> WithRetry(
        SnmpTarget target, CancellationToken ct, Func<CancellationToken, Task<IReadOnlyList<SnmpVarBind>>> op)
    {
        var attempts = Math.Max(0, target.Retries) + 1;
        AppSnmpException? last = null;
        for (var i = 0; i < attempts; i++)
        {
            ct.ThrowIfCancellationRequested();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(target.TimeoutMs);
            try { return await op(cts.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { last = new AppSnmpException(SnmpFailure.Timeout, "SNMP request timed out."); }
            catch (SharpTimeout)
            { last = new AppSnmpException(SnmpFailure.Timeout, "SNMP request timed out."); }
            catch (SocketException ex)
            { last = new AppSnmpException(SnmpFailure.Unreachable, "Network error: " + ex.SocketErrorCode, ex); }
            catch (DecryptionException ex)
            { last = new AppSnmpException(SnmpFailure.Decryption, "SNMPv3 decryption failed.", ex); break; }
            catch (AppSnmpException ex)
            {
                last = ex;
                if (ex.Failure is not SnmpFailure.Timeout) break;   // auth/config errors will not fix themselves
            }
            catch (ErrorException ex)
            { last = new AppSnmpException(SnmpFailure.Protocol, "SNMP error response.", ex); break; }
        }
        throw last ?? new AppSnmpException(SnmpFailure.Other, "SNMP request failed.");
    }

    /// <summary>SNMPv3: discover the remote engine, then build the auth/priv provider.</summary>
    private static async Task<(IPrivacyProvider, ISnmpMessage)> PrepareV3(SnmpTarget t, IPEndPoint ep, CancellationToken ct)
    {
        var discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
        var report = await discovery.GetResponseAsync(ep, ct);
        return (BuildPrivacy(t.Credentials), report);
    }

    // MD5, SHA-1 and DES are cryptographically weak but are still what many switches are configured with; they must stay selectable (read-only SNMP).
#pragma warning disable CS0618
    internal static IPrivacyProvider BuildPrivacy(SnmpCredentials c)
    {
        IAuthenticationProvider auth = c.AuthProtocol switch
        {
            SnmpAuthProtocol.None => DefaultAuthenticationProvider.Instance,
            SnmpAuthProtocol.Md5 => new MD5AuthenticationProvider(new OctetString(c.AuthPassword ?? "")),
            SnmpAuthProtocol.Sha1 => new SHA1AuthenticationProvider(new OctetString(c.AuthPassword ?? "")),
            SnmpAuthProtocol.Sha256 => new SHA256AuthenticationProvider(new OctetString(c.AuthPassword ?? "")),
            SnmpAuthProtocol.Sha384 => new SHA384AuthenticationProvider(new OctetString(c.AuthPassword ?? "")),
            SnmpAuthProtocol.Sha512 => new SHA512AuthenticationProvider(new OctetString(c.AuthPassword ?? "")),
            _ => throw new ArgumentOutOfRangeException(nameof(c))
        };
        if (c.AuthProtocol == SnmpAuthProtocol.None || c.PrivProtocol == SnmpPrivProtocol.None)
            return new DefaultPrivacyProvider(auth);
        var phrase = new OctetString(c.PrivPassword ?? "");
        return c.PrivProtocol switch
        {
            SnmpPrivProtocol.Des => new DESPrivacyProvider(phrase, auth),
            SnmpPrivProtocol.Aes128 => new AESPrivacyProvider(phrase, auth),
            SnmpPrivProtocol.Aes192 => new AES192PrivacyProvider(phrase, auth),
            SnmpPrivProtocol.Aes256 => new AES256PrivacyProvider(phrase, auth),
            _ => throw new ArgumentOutOfRangeException(nameof(c))
        };
    }

#pragma warning restore CS0618

    private static IReadOnlyList<SnmpVarBind> ConvertResponse(ISnmpMessage response)
    {
        var pdu = response.Pdu();
        if (pdu.TypeCode == SnmpType.ReportPdu)
        {
            var id = pdu.Variables.FirstOrDefault()?.Id;
            if (id is null) throw new AppSnmpException(SnmpFailure.Protocol, "SNMPv3 report with no detail.");
            if (id == Messenger.AuthenticationFailure) throw new AppSnmpException(SnmpFailure.AuthenticationFailed, "Authentication failed.");
            if (id == Messenger.DecryptionError) throw new AppSnmpException(SnmpFailure.Decryption, "Decryption failed.");
            if (id == Messenger.UnknownSecurityName) throw new AppSnmpException(SnmpFailure.UnknownUser, "Unknown user.");
            if (id == Messenger.UnsupportedSecurityLevel) throw new AppSnmpException(SnmpFailure.UnsupportedSecurityLevel, "Unsupported security level.");
            throw new AppSnmpException(SnmpFailure.Protocol, Messenger.GetErrorMessage(id));
        }
        return Convert(response.Variables());
    }

    private static IReadOnlyList<SnmpVarBind> Convert(IList<Variable> vars) => vars.Select(ToVarBind).ToList();

    internal static SnmpVarBind ToVarBind(Variable v) => new(v.Id.ToString().TrimStart('.'), ToValue(v.Data));

    internal static SnmpValue ToValue(ISnmpData d) => d switch
    {
        Integer32 i => new SnmpValue(SnmpValueKind.Integer, i.ToInt32()),
        Counter32 c => new SnmpValue(SnmpValueKind.Counter, c.ToUInt32()),
        Counter64 c => new SnmpValue(SnmpValueKind.Counter, unchecked((long)c.ToUInt64())),
        Gauge32 g => new SnmpValue(SnmpValueKind.Gauge, g.ToUInt32()),
        TimeTicks t => new SnmpValue(SnmpValueKind.TimeTicks, t.ToUInt32()),
        OctetString o => new SnmpValue(SnmpValueKind.OctetString, 0, o.GetRaw(), PrintableOrHex(o)),
        ObjectIdentifier o => new SnmpValue(SnmpValueKind.ObjectId, 0, null, o.ToString().TrimStart('.')),
        IP ip => new SnmpValue(SnmpValueKind.IpAddress, 0, null, ip.ToIPAddress().ToString()),
        Null => new SnmpValue(SnmpValueKind.Null),
        NoSuchObject => new SnmpValue(SnmpValueKind.NoSuchObject),
        NoSuchInstance => new SnmpValue(SnmpValueKind.NoSuchInstance),
        EndOfMibView => new SnmpValue(SnmpValueKind.EndOfMibView),
        _ => new SnmpValue(SnmpValueKind.Other, 0, null, d.ToString())
    };

    private static string PrintableOrHex(OctetString o)
    {
        var raw = o.GetRaw();
        // Text only if every byte is printable ASCII/whitespace or valid multi-byte UTF-8 text; otherwise hex (MAC addresses etc.).
        var text = System.Text.Encoding.UTF8.GetString(raw);
        return raw.Length == 0 || text.All(ch => !char.IsControl(ch) || ch is '\r' or '\n' or '\t') && !text.Contains('�')
            ? text
            : System.Convert.ToHexString(raw);
    }
}
