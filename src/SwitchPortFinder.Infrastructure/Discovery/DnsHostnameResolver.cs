using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>Reverse DNS with a hard timeout and positive/negative caching. A DNS failure never fails a scan.</summary>
public class DnsHostnameResolver(IMemoryCache cache, IOptions<DiscoveryOptions> opt) : IHostnameResolver
{
    public async Task<string?> ResolveAsync(string ip, CancellationToken ct)
    {
        if (cache.TryGetValue("dns:" + ip, out string? cached)) return cached;
        string? name = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(opt.Value.DnsTimeoutMs);
            var entry = await Dns.GetHostEntryAsync(ip, cts.Token);
            // Reject answers that are just the IP echoed back.
            name = string.IsNullOrWhiteSpace(entry.HostName) || entry.HostName == ip ? null : entry.HostName.TrimEnd('.');
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { name = null; }
        cache.Set("dns:" + ip, name, name is null ? TimeSpan.FromMinutes(10) : TimeSpan.FromHours(1));
        return name;
    }
}
