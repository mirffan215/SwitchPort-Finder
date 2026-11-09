using FluentAssertions;
using SwitchPortFinder.Application.Common;

namespace SwitchPortFinder.UnitTests;

public class RuntimeSettingsTests
{
    [Fact] public void Defaults_are_valid_and_match_the_documented_defaults()
    {
        var s = new RuntimeSettings(); s.Validate().Should().BeEmpty();
        (s.SnmpTimeoutMs, s.SnmpRetries, s.MaxParallelScans, s.OfflineAfterMinutes, s.StaleAfterDays, s.ScanIntervalMinutes).Should().Be((5000, 1, 5, 60, 7, 0));
    }

    [Theory]
    [InlineData(nameof(RuntimeSettings.SnmpTimeoutMs), 0, "snmpTimeoutMs")] [InlineData(nameof(RuntimeSettings.SnmpTimeoutMs), 600_000, "snmpTimeoutMs")]
    [InlineData(nameof(RuntimeSettings.SnmpRetries), -1, "snmpRetries")] [InlineData(nameof(RuntimeSettings.SnmpRetries), 99, "snmpRetries")]
    [InlineData(nameof(RuntimeSettings.MaxParallelScans), 0, "maxParallelScans")] [InlineData(nameof(RuntimeSettings.MaxParallelScans), 500, "maxParallelScans")]
    [InlineData(nameof(RuntimeSettings.ScanIntervalMinutes), 1, "scanIntervalMinutes")] [InlineData(nameof(RuntimeSettings.ScanIntervalMinutes), -5, "scanIntervalMinutes")]
    [InlineData(nameof(RuntimeSettings.PerVlanParallelism), 0, "perVlanParallelism")] [InlineData(nameof(RuntimeSettings.UplinkHeuristicMinMacs), 1, "uplinkHeuristicMinMacs")]
    public void Out_of_range_values_are_rejected_with_the_field_name(string prop, int value, string field)
    {
        var s = new RuntimeSettings(); typeof(RuntimeSettings).GetProperty(prop)!.SetValue(s, value);
        s.Validate().Should().ContainKey(field);
    }

    [Fact] public void Schedule_zero_means_off_and_five_minutes_is_the_minimum() { new RuntimeSettings { ScanIntervalMinutes = 0 }.Validate().Should().BeEmpty(); new RuntimeSettings { ScanIntervalMinutes = 5 }.Validate().Should().BeEmpty(); }

    [Fact] public void Stale_must_be_later_than_offline() =>
        new RuntimeSettings { OfflineAfterMinutes = 3 * 24 * 60, StaleAfterDays = 2 }.Validate().Should().ContainKey("staleAfterDays");

    [Fact] public void NaN_ratio_is_rejected() => new RuntimeSettings { UplinkHeuristicRatio = double.NaN }.Validate().Should().ContainKey("uplinkHeuristicRatio");

    [Fact] public void Settings_round_trip_through_the_option_classes()
    {
        var s = new RuntimeSettings { SnmpTimeoutMs = 3000, SnmpRetries = 2, MaxParallelScans = 9, StaleAfterDays = 30, ScanIntervalMinutes = 15, UplinkHeuristicRatio = 0.75 };
        SnmpOptions so = new(); DiscoveryOptions d = new(); s.ApplyTo(so, d);
        (so.TimeoutMs, so.Retries, d.MaxParallelScans, d.StaleAfterDays, d.ScanIntervalMinutes, d.UplinkHeuristicRatio).Should().Be((3000, 2, 9, 30, 15, 0.75));
        RuntimeSettings.From(so, d).Should().BeEquivalentTo(s);
    }
}
