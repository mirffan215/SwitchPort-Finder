using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Discovery;
using SwitchPortFinder.Discovery.Providers;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.TestSupport;
using static SwitchPortFinder.TestSupport.ScriptedSnmp;

namespace SwitchPortFinder.UnitTests;

public class ProviderTests
{
    [Fact] public void Every_selectable_manufacturer_has_a_provider_plus_generic()
    {
        foreach (var m in Enum.GetValues<Manufacturer>().Where(m => m != Manufacturer.AutoDetect))
            ProviderRegistry.For(m).Manufacturer.Should().Be(m, because: m.ToString());
        ProviderRegistry.All.Should().HaveCount(Enum.GetValues<Manufacturer>().Length - 1);   // all but AutoDetect
    }

    [Fact] public void AutoDetect_or_unknown_values_fall_back_to_generic() =>
        ProviderRegistry.For(Manufacturer.AutoDetect).Should().BeOfType<GenericProvider>();

    [Fact] public void Only_cisco_uses_per_vlan_fdb_and_cdp()
    {
        ProviderRegistry.All.Where(p => p.UsesPerVlanFdb || p.SupportsCdp || p.HasCiscoVlanMibs).Select(p => p.Manufacturer).Should().Equal(Manufacturer.Cisco);
    }

    [Fact] public void Vendor_oids_are_the_ones_resolved_from_the_vendor_mibs()
    {
        ProviderRegistry.For(Manufacturer.Juniper).SerialOids.Single().Oid.Should().Be("1.3.6.1.4.1.2636.3.1.3.0");      // jnxBoxSerialNo.0
        ProviderRegistry.For(Manufacturer.Juniper).ModelOids.Single().Oid.Should().Be("1.3.6.1.4.1.2636.3.1.2.0");       // jnxBoxDescr.0
        ProviderRegistry.For(Manufacturer.MikroTik).SerialOids.Single().Oid.Should().Be("1.3.6.1.4.1.14988.1.1.7.3.0");  // mtxrSerialNumber.0
        ProviderRegistry.For(Manufacturer.MikroTik).ModelOids.Single().Oid.Should().Be("1.3.6.1.4.1.14988.1.1.7.9.0");   // mtxrBoardName.0
        ProviderRegistry.For(Manufacturer.Fortinet).SerialOids.Single().Oid.Should().Be("1.3.6.1.4.1.12356.100.1.1.1.0"); // fnSysSerial.0
        ProviderRegistry.For(Manufacturer.Cisco).SerialOids.Single().Oid.Should().Be("1.3.6.1.4.1.9.5.1.2.19.0");       // chassisSerialNumberString.0
    }

    [Fact] public void Vendors_without_a_verified_oid_have_none_rather_than_a_guess() =>
        new[] { Manufacturer.HpeAruba, Manufacturer.ExtremeNetworks, Manufacturer.Huawei, Manufacturer.Ubiquiti, Manufacturer.Netgear, Manufacturer.TpLink, Manufacturer.AlliedTelesis, Manufacturer.OtherGeneric }
            .Select(ProviderRegistry.For).Should().OnlyContain(p => p.ModelOids.Count == 0 && p.SerialOids.Count == 0);

    private static SnmpSwitchScanner Scanner(ScriptedSnmp s) => new(s, Options.Create(new SnmpOptions()), Options.Create(new DiscoveryOptions()), NullLogger<SnmpSwitchScanner>.Instance);
    private static ScanRequest Req(Manufacturer m = Manufacturer.AutoDetect) => new("192.0.2.10", new SnmpCredentials { Version = SnmpVersion.V2c, Community = "pub" }, m, false);
    private static ScriptedSnmp Net(string objectId) { var s = new ScriptedSnmp().WithSystem("192.0.2.10", "SW", objectId); s.Walk("192.0.2.10", "pub", Oids.IfTable, IfTable((1, "ge-0/0/1", 6, true, null))); return s; }

    [Theory]
    [InlineData("1.3.6.1.4.1.2636.1.1.1.2.30", "1.3.6.1.4.1.2636.3.1.3.0", "JN123456", "1.3.6.1.4.1.2636.3.1.2.0", "Juniper EX4300-48T")]
    [InlineData("1.3.6.1.4.1.14988.1", "1.3.6.1.4.1.14988.1.1.7.3.0", "HE1234ABCD", "1.3.6.1.4.1.14988.1.1.7.9.0", "CRS326-24G")]
    public async Task Vendor_mib_fills_model_and_serial_when_entity_mib_has_none(string objId, string serialOid, string serial, string modelOid, string model)
    {
        var s = Net(objId); s.Gets[$"192.0.2.10|{serialOid}"] = Txt(serial); s.Gets[$"192.0.2.10|{modelOid}"] = Txt(model);
        var r = await Scanner(s).ScanAsync(Req(), _ => Task.CompletedTask, default);
        (r.System!.Serial, r.System.Model).Should().Be((serial, model));
    }

    [Fact] public async Task Fortinet_serial_and_dell_column_serial()
    {
        var f = Net("1.3.6.1.4.1.12356.101.1"); f.Gets["192.0.2.10|1.3.6.1.4.1.12356.100.1.1.1.0"] = Txt("FS108E0000001");
        (await Scanner(f).ScanAsync(Req(), _ => Task.CompletedTask, default)).System!.Serial.Should().Be("FS108E0000001");

        var d = Net("1.3.6.1.4.1.6027.1.3.17"); d.Walk("192.0.2.10", "pub", "1.3.6.1.4.1.6027.3.1.1.1.2", [Vb("1.3.6.1.4.1.6027.3.1.1.1.2.1", Txt("DL-SN-0042"))]);
        (await Scanner(d).ScanAsync(Req(), _ => Task.CompletedTask, default)).System!.Serial.Should().Be("DL-SN-0042");
    }

    [Fact] public async Task Entity_mib_wins_over_vendor_fallbacks()
    {
        var s = Net("1.3.6.1.4.1.2636.1.1.1.2.30");
        s.Walk("192.0.2.10", "pub", Oids.EntPhysicalModel, [Vb($"{Oids.EntPhysicalModel}.1", Txt("EX4300-ENTITY"))]);
        s.Walk("192.0.2.10", "pub", Oids.EntPhysicalSerial, [Vb($"{Oids.EntPhysicalSerial}.1", Txt("ENTITY-SERIAL"))]);
        s.Gets[$"192.0.2.10|1.3.6.1.4.1.2636.3.1.3.0"] = Txt("VENDOR-SERIAL");
        var r = await Scanner(s).ScanAsync(Req(), _ => Task.CompletedTask, default);
        (r.System!.Model, r.System.Serial).Should().Be(("EX4300-ENTITY", "ENTITY-SERIAL"));
    }

    [Fact] public async Task A_missing_vendor_oid_is_ignored_and_never_fails_the_scan()
    {
        var s = Net("1.3.6.1.4.1.2636.1.1.1.2.30");   // Juniper, but the vendor OIDs are not answered
        var r = await Scanner(s).ScanAsync(Req(), _ => Task.CompletedTask, default);
        r.Reachable.Should().BeTrue(); r.System!.Serial.Should().BeNull(); r.Steps.Single(x => x.Name == Steps.System).State.Should().Be(StepState.Ok);
    }

    [Fact] public async Task Selected_manufacturer_overrides_detection_for_the_vendor_fallbacks()
    {
        var s = Net("1.3.6.1.4.1.99999"); s.Gets["192.0.2.10|1.3.6.1.4.1.2636.3.1.3.0"] = Txt("JN-FORCED");
        (await Scanner(s).ScanAsync(Req(Manufacturer.Juniper), _ => Task.CompletedTask, default)).System!.Serial.Should().Be("JN-FORCED");
    }
}
