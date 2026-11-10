namespace SwitchPortFinder.Application.Discovery;

/// <summary>Counter snapshot of one port at one scan.</summary>
public record CounterSample(long InOctets, long OutOctets, long InDiscards, long OutDiscards, long InErrors, long OutErrors, bool Wide, DateTime At, long UpTicks);

/// <summary>Average rates between two scans. Octet rates are null when they cannot be trusted (32-bit counters that may have wrapped repeatedly).</summary>
public record PortRates(double? InBps, double? OutBps, double DiscardsPerSec, double ErrorsPerSec, int Seconds);

/// <summary>
/// Turns two counter snapshots into average rates. Pure and deterministic so it can be unit-tested.
/// Rejects: too short / too long an interval, a switch reboot (sysUpTime went backwards), 64-bit counter resets,
/// and octet rates above the link speed (a sign a 32-bit counter wrapped more than once between scans).
/// </summary>
public static class PortRateCalculator
{
    public const int MinSeconds = 10;
    public const int MaxSeconds = 7 * 24 * 3600;

    public static PortRates? Compute(CounterSample prev, CounterSample cur, long speedMbps)
    {
        var secs = (cur.At - prev.At).TotalSeconds;
        if (secs < MinSeconds || secs > MaxSeconds) return null;
        if (cur.UpTicks > 0 && prev.UpTicks > 0 && cur.UpTicks < prev.UpTicks) return null;   // rebooted: counters restarted
        var wide = cur.Wide && prev.Wide;
        if (cur.Wide != prev.Wide) return null;                                              // counter width changed between scans
        var dIn = Delta(cur.InOctets, prev.InOctets, wide); var dOut = Delta(cur.OutOctets, prev.OutOctets, wide);
        if (dIn is null || dOut is null) return null;                                          // 64-bit counter reset: nothing trustworthy
        long Narrow(long c, long p) => Delta(c, p, false) ?? 0;   // 32-bit error/discard counters: a decrease is one wrap
        var dDisc = Narrow(cur.InDiscards, prev.InDiscards) + Narrow(cur.OutDiscards, prev.OutDiscards);
        var dErr = Narrow(cur.InErrors, prev.InErrors) + Narrow(cur.OutErrors, prev.OutErrors);
        double? inBps = dIn * 8 / secs, outBps = dOut * 8 / secs;
        if (speedMbps > 0)
        {
            var limit = speedMbps * 1_000_000d * 1.05;
            if (inBps > limit || outBps > limit) { inBps = null; outBps = null; }
        }
        return new PortRates(inBps, outBps, dDisc / secs, dErr / secs, (int)secs);
    }

    /// <summary>Counter difference. A 32-bit counter that went down has wrapped once; a 64-bit one has been reset (returns null).</summary>
    private static long? Delta(long cur, long prev, bool wide)
    {
        if (cur >= prev) return cur - prev;
        return wide ? null : cur + (1L << 32) - prev;
    }
}
