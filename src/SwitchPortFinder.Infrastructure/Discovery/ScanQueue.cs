using System.Threading.Channels;
using SwitchPortFinder.Application.Abstractions;

namespace SwitchPortFinder.Infrastructure.Discovery;

public sealed class ScanQueue : IScanQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });
    public ValueTask EnqueueAsync(int jobId, CancellationToken ct = default) => _channel.Writer.WriteAsync(jobId, ct);
    public ChannelReader<int> Reader => _channel.Reader;
}
