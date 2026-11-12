using FluentAssertions;
using SwitchPortFinder.Infrastructure.Integrations;

namespace SwitchPortFinder.UnitTests;

public class IntegrationParseTests
{
    [Theory]
    [InlineData("aa-bb-cc-dd-ee-ff", "AABBCCDDEEFF")]
    [InlineData("AA:BB:CC:DD:EE:FF", "AABBCCDDEEFF")]
    [InlineData("0011.2233.4455", "001122334455")]
    [InlineData("00-00-00-00-00-00", null)]
    [InlineData("zz", null)]
    [InlineData(null, null)]
    public void Macs_are_normalised(string? input, string? expected) => Parse.Mac(input).Should().Be(expected);

    [Theory]
    [InlineData("pc-12.corp.local", "pc-12")]
    [InlineData("PC-12", "PC-12")]
    [InlineData("pc-12.", "pc-12")]
    [InlineData("", null)]
    public void Names_are_shortened_to_the_first_label(string input, string? expected) => Parse.ShortName(input).Should().Be(expected);

    [Fact]
    public void A_powershell_lease_csv_is_parsed_and_bad_rows_are_skipped()
    {
        var csv = "\"IPAddress\",\"ClientId\",\"HostName\"\r\n\"10.0.0.5\",\"aa-bb-cc-dd-ee-01\",\"pc-1.corp.local\"\r\n\"10.0.0.6\",\"aa-bb-cc-dd-ee-02\",\"\"\r\n\"nope\",\"aa-bb-cc-dd-ee-03\",\"x\"\r\n\"10.0.0.8\",\"bad\",\"y\"\r\n";
        var l = Parse.LeaseCsv(csv);
        l.Should().HaveCount(2);
        (l[0].Mac, l[0].Ip, l[0].HostName).Should().Be(("AABBCCDDEE01", "10.0.0.5", "pc-1.corp.local")); l[1].HostName.Should().BeNull();
    }

    [Fact] public void The_ou_path_is_read_from_the_distinguished_name() { Parse.OuPath("CN=PC-1,OU=Sales,OU=Staff,DC=corp,DC=local").Should().Be("Sales / Staff"); Parse.OuPath("CN=PC-1,CN=Computers,DC=corp,DC=local").Should().BeNull(); }

    [Theory]
    [InlineData("dhcp01.corp.local", true)]
    [InlineData("10.1.1.5", true)]
    [InlineData("x'; Remove-Item *", false)]
    [InlineData("a b", false)]
    [InlineData("", false)]
    public void Only_plain_host_names_reach_powershell(string host, bool ok) => Parse.IsHost(host).Should().Be(ok);
}
