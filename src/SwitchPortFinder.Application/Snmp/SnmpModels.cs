using System.Net;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Snmp;

/// <summary>In-memory credentials. ToString is redacted so accidental logging cannot leak secrets.</summary>
public sealed record SnmpCredentials
{
    public SnmpVersion Version { get; init; } = SnmpVersion.V2c;
    public string? Community { get; init; }
    public string? Username { get; init; }
    public SnmpAuthProtocol AuthProtocol { get; init; }
    public string? AuthPassword { get; init; }
    public SnmpPrivProtocol PrivProtocol { get; init; }
    public string? PrivPassword { get; init; }
    public string? ContextName { get; init; }
    public override string ToString() => $"SnmpCredentials({Version}, <redacted>)";
}

public sealed record SnmpTarget(IPAddress Address, int Port, SnmpCredentials Credentials, int TimeoutMs, int Retries, int MaxRepetitions = 10);

public enum SnmpValueKind { Null, Integer, Counter, Gauge, TimeTicks, OctetString, ObjectId, IpAddress, NoSuchObject, NoSuchInstance, EndOfMibView, Other }

public sealed record SnmpValue(SnmpValueKind Kind, long Number = 0, byte[]? Bytes = null, string? Text = null)
{
    public bool IsMissing => Kind is SnmpValueKind.NoSuchObject or SnmpValueKind.NoSuchInstance or SnmpValueKind.EndOfMibView or SnmpValueKind.Null;
    public override string ToString() => Kind switch
    {
        SnmpValueKind.Integer or SnmpValueKind.Counter or SnmpValueKind.Gauge or SnmpValueKind.TimeTicks => Number.ToString(),
        _ => Text ?? string.Empty
    };
}

public sealed record SnmpVarBind(string Oid, SnmpValue Value);

public enum SnmpFailure { Timeout, Unreachable, AuthenticationFailed, Decryption, UnknownUser, UnsupportedSecurityLevel, Protocol, Other }

public class SnmpException(SnmpFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public SnmpFailure Failure { get; } = failure;
}
