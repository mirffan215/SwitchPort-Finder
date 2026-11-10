using FluentAssertions;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Discovery.Parsing;
using static SwitchPortFinder.TestSupport.ScriptedSnmp;

namespace SwitchPortFinder.UnitTests;

public class PortRateCalculatorTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static CounterSample S(long inO, long outO, DateTime at, long inD = 0, long outD = 0, bool wide = true, long up = 1000) => new(inO, outO, inD, outD, 0, 0, wide, at, up);

    [Fact]
    public void Computes_bits_per_second_and_drops_per_second()
    {
        var r = PortRateCalculator.Compute(S(0, 0, T0), S(75_000_000, 25_000_000, T0.AddSeconds(10), inD: 20, outD: 10, up: 2000), 1000)!;
        r.InBps.Should().BeApproximately(60_000_000, 1);          // 75 MB in 10 s = 60 Mbit/s
        r.OutBps.Should().BeApproximately(20_000_000, 1);
        r.DiscardsPerSec.Should().BeApproximately(3, 0.001);       // 30 discards in 10 s
        r.Seconds.Should().Be(10);
    }

    [Fact] public void Too_short_an_interval_gives_no_rate() => PortRateCalculator.Compute(S(0, 0, T0), S(1, 1, T0.AddSeconds(3)), 1000).Should().BeNull();
    [Fact] public void A_week_old_baseline_is_too_stale_to_average() => PortRateCalculator.Compute(S(0, 0, T0), S(1, 1, T0.AddDays(8)), 1000).Should().BeNull();
    [Fact] public void A_reboot_is_detected_from_sysUpTime_going_backwards() => PortRateCalculator.Compute(S(500, 500, T0, up: 9000), S(100, 100, T0.AddMinutes(5), up: 200), 1000).Should().BeNull();
    [Fact] public void A_64bit_counter_that_went_down_is_a_reset_not_a_wrap() => PortRateCalculator.Compute(S(500, 500, T0), S(100, 100, T0.AddMinutes(5)), 1000).Should().BeNull();

    [Fact]
    public void A_32bit_counter_that_went_down_is_one_wrap()
    {
        var prev = S(uint.MaxValue - 99, 0, T0, wide: false); var cur = S(100, 0, T0.AddSeconds(100), wide: false);   // +200 octets
        PortRateCalculator.Compute(prev, cur, 1000)!.InBps.Should().BeApproximately(16, 0.01);                         // 200*8/100
    }

    [Fact]
    public void Octet_rates_above_link_speed_are_rejected_as_aliased_wraps()
    {
        var r = PortRateCalculator.Compute(S(0, 0, T0, wide: false), S(4_000_000_000, 0, T0.AddSeconds(10), wide: false), 100)!;   // 3.2 Gbit/s on a 100M link
        r.InBps.Should().BeNull(); r.OutBps.Should().BeNull();
    }

    [Fact] public void Changing_counter_width_between_scans_gives_no_rate() => PortRateCalculator.Compute(S(0, 0, T0, wide: false), S(10, 10, T0.AddSeconds(60)), 1000).Should().BeNull();
}

public class PortCounterParsingTests
{
    private static SnmpValue C(long n) => new(SnmpValueKind.Counter, n);

    [Fact]
    public void Prefers_64bit_ifHC_octets_and_reads_discards_and_errors()
    {
        var ifT = IfTable((1, "Gi1/0/1", 6, true, null)).Concat([
            Vb($"{Oids.IfTable}.10.1", C(111)), Vb($"{Oids.IfTable}.16.1", C(222)), Vb($"{Oids.IfTable}.13.1", C(3)), Vb($"{Oids.IfTable}.19.1", C(4)), Vb($"{Oids.IfTable}.14.1", C(5)), Vb($"{Oids.IfTable}.20.1", C(6))]);
        var ifX = IfXTable((1, "Gi1/0/1", 1000, "")).Concat([Vb($"{Oids.IfXTable}.6.1", C(9_000_000_000)), Vb($"{Oids.IfXTable}.10.1", C(8_000_000_000))]);
        var c = InterfaceParser.Parse(ifT, ifX).Single().Counters!;
        c.Wide.Should().BeTrue(); c.InOctets.Should().Be(9_000_000_000); c.OutOctets.Should().Be(8_000_000_000);
        (c.InDiscards, c.OutDiscards, c.InErrors, c.OutErrors).Should().Be((3, 4, 5, 6));
    }

    [Fact]
    public void Falls_back_to_32bit_octets_when_ifXTable_has_no_HC_counters()
    {
        var ifT = IfTable((1, "Gi1/0/1", 6, true, null)).Concat([Vb($"{Oids.IfTable}.10.1", C(111)), Vb($"{Oids.IfTable}.16.1", C(222))]);
        var c = InterfaceParser.Parse(ifT, []).Single().Counters!;
        c.Wide.Should().BeFalse(); c.InOctets.Should().Be(111); c.OutOctets.Should().Be(222);
    }

    [Fact] public void No_counters_returned_means_none_not_zero() => InterfaceParser.Parse(IfTable((1, "Gi1/0/1", 6, true, null)), []).Single().Counters.Should().BeNull();
}

public class UpdateRatesTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static SwitchPortFinder.Domain.Entities.SwitchPort Port() => new() { SpeedMbps = 1000 };

    [Fact]
    public void First_scan_stores_a_baseline_only_then_second_scan_yields_rates()
    {
        var p = Port();
        SwitchPortFinder.Infrastructure.Discovery.ScanResultWriter.UpdateRates(p, new PortCounters(0, 0, 0, 0, 0, 0, true), 100, T0);
        p.RateAt.Should().BeNull("one sample is not a rate"); p.CtrAt.Should().Be(T0);
        SwitchPortFinder.Infrastructure.Discovery.ScanResultWriter.UpdateRates(p, new PortCounters(750_000_000, 0, 60, 0, 0, 0, true), 700, T0.AddSeconds(60));
        p.RateInBps.Should().BeApproximately(100_000_000, 1); p.RateDiscardsPerSec.Should().BeApproximately(1, 0.001); p.RateSeconds.Should().Be(60);
    }

    [Fact]
    public void Back_to_back_scans_keep_the_older_baseline()
    {
        var p = Port();
        var w = SwitchPortFinder.Infrastructure.Discovery.ScanResultWriter.UpdateRates;
        w(p, new PortCounters(100, 0, 0, 0, 0, 0, true), 100, T0);
        w(p, new PortCounters(200, 0, 0, 0, 0, 0, true), 101, T0.AddSeconds(2));
        p.CtrAt.Should().Be(T0); p.CtrInOctets.Should().Be(100);
    }

    [Fact]
    public void A_reboot_clears_the_old_rate_instead_of_showing_stale_numbers()
    {
        var p = Port();
        var w = SwitchPortFinder.Infrastructure.Discovery.ScanResultWriter.UpdateRates;
        w(p, new PortCounters(0, 0, 0, 0, 0, 0, true), 5000, T0);
        w(p, new PortCounters(1_000_000, 0, 0, 0, 0, 0, true), 5600, T0.AddSeconds(60)); p.RateAt.Should().NotBeNull();
        w(p, new PortCounters(10, 0, 0, 0, 0, 0, true), 50, T0.AddSeconds(120));
        p.RateAt.Should().BeNull(); p.RateInBps.Should().BeNull(); p.CtrInOctets.Should().Be(10, "the new counters become the baseline");
    }
}

public class OuiLookupTests
{
    [Theory]
    [InlineData("1866DA163EB5", "Dell Inc.")]
    [InlineData("0000C0031E29", "WESTERN DIGITAL CORPORATION")]
    [InlineData("08A189007CBB", "Hangzhou Hikvision Digital Technology Co.,Ltd.")]
    [InlineData("00d0b81636ed", "Iomega Corporation")]   // lower case input is fine
    public void Known_manufacturers_resolve(string mac, string vendor) => SwitchPortFinder.Domain.OuiLookup.Lookup(mac).Should().Be(vendor);

    [Theory] [InlineData("062995 73847A")] [InlineData("4E9718B4F9C7")] [InlineData("DA00112233AA")]
    public void Randomised_locally_administered_macs_are_labelled_private_not_guessed(string mac) =>
        SwitchPortFinder.Domain.OuiLookup.Lookup(mac.Replace(" ", "")).Should().Be(SwitchPortFinder.Domain.OuiLookup.PrivateMac);

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("1866DA")] [InlineData("ZZZZZZZZZZZZ")] [InlineData("01005E000001")]   // multicast is never a device
    public void Garbage_and_multicast_give_no_vendor(string? mac) => SwitchPortFinder.Domain.OuiLookup.Lookup(mac).Should().BeNull();

    [Fact] public void The_embedded_registry_is_loaded() => SwitchPortFinder.Domain.OuiLookup.Count.Should().BeGreaterThan(30_000);
}
