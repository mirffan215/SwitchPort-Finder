using FluentAssertions;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Discovery.Parsing;

namespace SwitchPortFinder.UnitTests;

public class HealthParserTests
{
    private static SnmpVarBind G(string oid, long n) => new(oid, new SnmpValue(SnmpValueKind.Gauge, n));
    private static SnmpVarBind Id(string oid, string text) => new(oid, new SnmpValue(SnmpValueKind.ObjectId, 0, null, text));

    [Fact] public void Cisco_cpu_is_the_busiest_processor_five_minute_average()
    {
        var walk = new[] { G($"{Oids.CiscoCpuTotalEntry}.8.1", 12), G($"{Oids.CiscoCpuTotalEntry}.8.2", 45), G($"{Oids.CiscoCpuTotalEntry}.5.1", 99) /* deprecated 5min column must be ignored */ };
        HealthParser.MaxColumn(walk, Oids.CiscoCpuTotalEntry, 8).Should().Be(45);
    }

    [Fact] public void Cisco_memory_uses_the_largest_pool_used_over_total()
    {
        var walk = new[]
        {
            G($"{Oids.CiscoMemoryPoolEntry}.5.1", 600), G($"{Oids.CiscoMemoryPoolEntry}.6.1", 400),      // processor pool: 1000 bytes, 60 % used
            G($"{Oids.CiscoMemoryPoolEntry}.5.2", 90), G($"{Oids.CiscoMemoryPoolEntry}.6.2", 10),        // small I/O pool, ignored
        };
        HealthParser.CiscoMemory(walk).Should().BeApproximately(60, 0.001);
    }

    [Fact] public void Juniper_cpu_and_memory_are_read_from_the_operating_table_maximum()
    {
        var walk = new[] { G($"{Oids.JuniperOperatingEntry}.8.9.1.0.0", 5), G($"{Oids.JuniperOperatingEntry}.8.9.2.0.0", 77), G($"{Oids.JuniperOperatingEntry}.11.9.1.0.0", 30), G($"{Oids.JuniperOperatingEntry}.11.9.2.0.0", 64) };
        HealthParser.MaxColumn(walk, Oids.JuniperOperatingEntry, 8).Should().Be(77); HealthParser.MaxColumn(walk, Oids.JuniperOperatingEntry, 11).Should().Be(64);
    }

    [Fact] public void Host_resources_cpu_is_the_average_over_processors()
    {
        var walk = new[] { G($"{Oids.HrProcessorEntry}.2.196608", 10), G($"{Oids.HrProcessorEntry}.2.196609", 30) };
        HealthParser.HrCpu(walk).Should().Be(20);
    }

    [Fact] public void Host_resources_memory_picks_the_RAM_row_not_disks_or_virtual_memory()
    {
        var walk = new[]
        {
            Id($"{Oids.HrStorageEntry}.2.1", Oids.HrStorageRam), G($"{Oids.HrStorageEntry}.5.1", 1000), G($"{Oids.HrStorageEntry}.6.1", 250),
            Id($"{Oids.HrStorageEntry}.2.31", "1.3.6.1.2.1.25.2.1.4"), G($"{Oids.HrStorageEntry}.5.31", 5000), G($"{Oids.HrStorageEntry}.6.31", 4999),   // fixed disk, must not be used
        };
        HealthParser.HrMemory(walk).Should().Be(25);
    }

    [Fact] public void Empty_walks_and_out_of_range_values_never_invent_numbers()
    {
        HealthParser.MaxColumn([], Oids.CiscoCpuTotalEntry, 8).Should().BeNull(); HealthParser.HrCpu([]).Should().BeNull(); HealthParser.HrMemory([]).Should().BeNull(); HealthParser.CiscoMemory([]).Should().BeNull();
        HealthParser.MaxColumn([G($"{Oids.CiscoCpuTotalEntry}.8.1", 250)], Oids.CiscoCpuTotalEntry, 8).Should().Be(100, "clamped to a valid percentage");
        HealthParser.HrMemory([Id($"{Oids.HrStorageEntry}.2.1", Oids.HrStorageRam), G($"{Oids.HrStorageEntry}.5.1", 0), G($"{Oids.HrStorageEntry}.6.1", 5)]).Should().BeNull("size 0 is not a percentage");
    }
}
