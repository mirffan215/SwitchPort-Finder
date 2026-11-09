using FluentAssertions;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Discovery.Snmp;
using SwitchPortFinder.Domain.Enums;
using Lextm.SharpSnmpLib.Security;

namespace SwitchPortFinder.UnitTests;

public class CredentialTests
{
    private sealed class ReversingProtector : ICredentialProtector
    {
        public string Protect(string p) => "ENC:" + new string(p.Reverse().ToArray());
        public string Unprotect(string p) => new string(p[4..].Reverse().ToArray());
    }

    [Fact]
    public void Blank_secrets_inherit_existing_values_when_version_unchanged()
    {
        var existing = new SnmpCredentials { Version = SnmpVersion.V2c, Community = "oldcomm" };
        CredentialCodec.Merge(new CredentialInput { Version = SnmpVersion.V2c, Community = "" }, existing).Community.Should().Be("oldcomm");
        CredentialCodec.Merge(new CredentialInput { Version = SnmpVersion.V2c, Community = "new" }, existing).Community.Should().Be("new");
    }

    [Fact]
    public void Changing_version_does_not_inherit_secrets_from_the_old_version()
    {
        var existing = new SnmpCredentials { Version = SnmpVersion.V2c, Community = "oldcomm" };
        var merged = CredentialCodec.Merge(new CredentialInput { Version = SnmpVersion.V3, Username = "u" }, existing);
        merged.Community.Should().BeNull();
    }

    [Fact]
    public void Encrypted_form_does_not_contain_secrets_and_round_trips()
    {
        var codec = new CredentialCodec(new ReversingProtector());
        var c = new SnmpCredentials { Version = SnmpVersion.V3, Username = "admin", AuthProtocol = SnmpAuthProtocol.Sha1, AuthPassword = "authpass123", PrivProtocol = SnmpPrivProtocol.Aes128, PrivPassword = "privpass123" };
        var enc = codec.Encrypt(c);
        var back = codec.Decrypt(c.Version, c.AuthProtocol, c.PrivProtocol, null, enc);
        back.AuthPassword.Should().Be("authpass123"); back.Username.Should().Be("admin");
        enc.Should().NotContain("authpass123");
    }

    [Fact]
    public void Validation_rules()
    {
        CredentialCodec.Validate(new SnmpCredentials { Version = SnmpVersion.V2c }).Should().ContainKey("credential.community");
        CredentialCodec.Validate(new SnmpCredentials { Version = SnmpVersion.V3, Username = "u", AuthProtocol = SnmpAuthProtocol.Sha1, AuthPassword = "short" }).Should().ContainKey("credential.authPassword");
        CredentialCodec.Validate(new SnmpCredentials { Version = SnmpVersion.V3, Username = "u" }).Should().BeEmpty("noAuthNoPriv is allowed");
    }

    [Fact]
    public void Accidental_string_formatting_never_reveals_secrets()
    {
        var c = new SnmpCredentials { Version = SnmpVersion.V2c, Community = "SECRET-COMM", AuthPassword = "SECRET-AUTH" };
        $"{c}".Should().NotContain("SECRET"); c.ToString().Should().NotContain("SECRET");
        new CredentialInput { Community = "SECRET-COMM" }.ToString().Should().NotContain("SECRET");
    }

    [Theory]
    [InlineData(SnmpAuthProtocol.None, SnmpPrivProtocol.None, typeof(DefaultPrivacyProvider))]
    [InlineData(SnmpAuthProtocol.Md5, SnmpPrivProtocol.None, typeof(DefaultPrivacyProvider))]
    [InlineData(SnmpAuthProtocol.Sha1, SnmpPrivProtocol.Des, typeof(DESPrivacyProvider))]
    [InlineData(SnmpAuthProtocol.Sha256, SnmpPrivProtocol.Aes128, typeof(AESPrivacyProvider))]
    [InlineData(SnmpAuthProtocol.Sha384, SnmpPrivProtocol.Aes192, typeof(AES192PrivacyProvider))]
    [InlineData(SnmpAuthProtocol.Sha512, SnmpPrivProtocol.Aes256, typeof(AES256PrivacyProvider))]
    public void Builds_the_matching_SNMPv3_privacy_provider(SnmpAuthProtocol a, SnmpPrivProtocol p, Type expected) =>
        SharpSnmpClient.BuildPrivacy(new SnmpCredentials { Version = SnmpVersion.V3, Username = "u", AuthProtocol = a, AuthPassword = "authpass123", PrivProtocol = p, PrivPassword = "privpass123" })
            .Should().BeOfType(expected);

    [Fact]
    public void ISnmpClient_exposes_only_read_operations() =>
        typeof(ISnmpClient).GetMethods().Select(m => m.Name).Should().BeEquivalentTo("GetAsync", "GetNextAsync", "WalkAsync");

    [Theory]
    [InlineData("192.0.2.1", "192.0.2.1")] [InlineData(" 2001:DB8::1 ", "2001:db8::1")] [InlineData("10", null)] [InlineData("1.2.3", null)] [InlineData("", null)]
    public void Ip_normalisation(string input, string? expected) => SwitchService.NormalizeIp(input).Should().Be(expected);
}
