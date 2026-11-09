using FluentAssertions;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.UnitTests;

public class VendorDetectorTests
{
    [Theory]
    [InlineData("1.3.6.1.4.1.9.1.2134", Manufacturer.Cisco)]
    [InlineData(".1.3.6.1.4.1.9.12.3.1.3.1", Manufacturer.Cisco)]
    [InlineData("1.3.6.1.4.1.11.2.3.7.11.1", Manufacturer.HpeAruba)]
    [InlineData("1.3.6.1.4.1.25506.11.1.1", Manufacturer.HpeAruba)]
    [InlineData("1.3.6.1.4.1.47196.4.1.1", Manufacturer.HpeAruba)]
    [InlineData("1.3.6.1.4.1.674.10895.3000", Manufacturer.Dell)]
    [InlineData("1.3.6.1.4.1.2636.1.1.1.2.30", Manufacturer.Juniper)]
    [InlineData("1.3.6.1.4.1.1916.2.1", Manufacturer.ExtremeNetworks)]
    [InlineData("1.3.6.1.4.1.4413.1", Manufacturer.ExtremeNetworks)]
    [InlineData("1.3.6.1.4.1.2011.2.23.1", Manufacturer.Huawei)]
    [InlineData("1.3.6.1.4.1.14988.1", Manufacturer.MikroTik)]
    [InlineData("1.3.6.1.4.1.41112.1.6", Manufacturer.Ubiquiti)]
    [InlineData("1.3.6.1.4.1.12356.101.1", Manufacturer.Fortinet)]
    [InlineData("1.3.6.1.4.1.4526.100", Manufacturer.Netgear)]
    [InlineData("1.3.6.1.4.1.11863.1.1", Manufacturer.TpLink)]
    [InlineData("1.3.6.1.4.1.207.1.4.126", Manufacturer.AlliedTelesis)]
    public void Detects_vendor_from_enterprise_number(string oid, Manufacturer expected) =>
        VendorDetector.Detect(oid).Should().Be(expected);

    [Theory]
    [InlineData("1.3.6.1.4.1.8072.3.2.10")]      // net-snmp: not in the table
    [InlineData("1.3.6.1.4.1.99999")]
    [InlineData("1.3.6.1.2.1.1")]                // not an enterprise OID
    [InlineData("1.3.6.1.4.1.90")]               // prefix of 9 must not match Cisco
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_or_malformed_falls_back_to_generic(string? oid) =>
        VendorDetector.Detect(oid).Should().Be(Manufacturer.OtherGeneric);

    [Fact]
    public void Manual_selection_overrides_detection()
    {
        VendorDetector.Effective(Manufacturer.Dell, Manufacturer.Cisco).Should().Be(Manufacturer.Dell);
        VendorDetector.Effective(Manufacturer.AutoDetect, Manufacturer.Cisco).Should().Be(Manufacturer.Cisco);
        VendorDetector.Effective(Manufacturer.AutoDetect, null).Should().Be(Manufacturer.OtherGeneric);
    }

    [Fact]
    public void Mismatch_is_reported_only_for_a_real_disagreement()
    {
        VendorDetector.Mismatch(Manufacturer.Dell, Manufacturer.Cisco).Should().BeTrue();
        VendorDetector.Mismatch(Manufacturer.Cisco, Manufacturer.Cisco).Should().BeFalse();
        VendorDetector.Mismatch(Manufacturer.AutoDetect, Manufacturer.Cisco).Should().BeFalse();
        VendorDetector.Mismatch(Manufacturer.Cisco, Manufacturer.OtherGeneric).Should().BeFalse("unknown detection is not evidence of a mismatch");
    }

    [Theory]
    [InlineData("Cisco", Manufacturer.Cisco)]
    [InlineData("HPE / Aruba", Manufacturer.HpeAruba)]
    [InlineData("aruba", Manufacturer.HpeAruba)]
    [InlineData("TP-Link", Manufacturer.TpLink)]
    [InlineData("Fortinet (FortiSwitch)", Manufacturer.Fortinet)]
    [InlineData("Other / Generic SNMP", Manufacturer.OtherGeneric)]
    [InlineData("", Manufacturer.AutoDetect)]
    [InlineData("Auto-detect", Manufacturer.AutoDetect)]
    public void Parses_manufacturer_names(string text, Manufacturer expected) => VendorDetector.Parse(text).Should().Be(expected);

    [Fact] public void Unrecognised_manufacturer_text_returns_null() => VendorDetector.Parse("Foobar").Should().BeNull();

    [Fact]
    public void Every_manufacturer_has_a_display_name_that_parses_back()
    {
        foreach (var m in Enum.GetValues<Manufacturer>())
            VendorDetector.Parse(VendorDetector.DisplayName(m)).Should().Be(m, because: m.ToString());
    }
}
