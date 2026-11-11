using System.Text;
using FluentAssertions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Import;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.UnitTests;

public class SecureExportTests
{
    private const string Pass = "correct horse battery staple";

    [Fact] public void Round_trips_and_the_clear_text_is_not_visible_in_the_file()
    {
        var plain = Encoding.UTF8.GetBytes("Name,Community\nSW1,SUPER-SECRET-community\n");
        var file = SecureExport.Encrypt(plain, Pass);
        Encoding.UTF8.GetString(file).Should().NotContain("SUPER-SECRET").And.NotContain("Community");
        SecureExport.IsContainer(file).Should().BeTrue();
        SecureExport.Decrypt(file, Pass).Should().Equal(plain);
    }

    [Fact] public void Two_exports_of_the_same_data_differ_because_salt_and_nonce_are_random()
    {
        var p = Encoding.UTF8.GetBytes("same"); SecureExport.Encrypt(p, Pass).Should().NotEqual(SecureExport.Encrypt(p, Pass));
    }

    [Fact] public void A_wrong_passphrase_a_flipped_bit_and_a_truncated_file_are_all_refused_with_the_same_message()
    {
        var file = SecureExport.Encrypt(Encoding.UTF8.GetBytes("secret data"), Pass);
        Action wrong = () => SecureExport.Decrypt(file, Pass + "x");
        var flipped = (byte[])file.Clone(); flipped[^3] ^= 0x01; Action tamper = () => SecureExport.Decrypt(flipped, Pass);
        var header = (byte[])file.Clone(); header[20] ^= 0x01; Action salt = () => SecureExport.Decrypt(header, Pass);
        wrong.Should().Throw<ValidationFailedException>(); tamper.Should().Throw<ValidationFailedException>(); salt.Should().Throw<ValidationFailedException>();
        Action trunc = () => SecureExport.Decrypt(file[..30], Pass); trunc.Should().Throw<ValidationFailedException>();
        Action notOurs = () => SecureExport.Decrypt(Encoding.UTF8.GetBytes("Name,IP\r\nsw,10.0.0.1\r\n and some more bytes to be long enough for the header check........"), Pass); notOurs.Should().Throw<ValidationFailedException>();
    }

    [Theory] [InlineData("")] [InlineData(null)] [InlineData("short")] [InlineData("elevenchars")]
    public void Weak_passphrases_are_refused(string? p) { Action a = () => SecureExport.Encrypt([1], p!); a.Should().Throw<ValidationFailedException>(); }

    [Fact] public void The_credential_export_has_the_import_columns_and_re_imports_cleanly_including_snmpv3()
    {
        var v2 = (new Switch { Name = "SW-V2", ManagementIp = "203.0.113.5", Manufacturer = Manufacturer.Cisco, Model = "9300", Location = "Rack 1", UseAsArpSource = true }, (SnmpCredentials?)new SnmpCredentials { Version = SnmpVersion.V2c, Community = "comm,with\"quote" });
        var v3 = (new Switch { Name = "SW-V3", ManagementIp = "203.0.113.6" }, (SnmpCredentials?)new SnmpCredentials { Version = SnmpVersion.V3, Username = "monitor", AuthProtocol = SnmpAuthProtocol.Sha256, AuthPassword = "auth-pass-123", PrivProtocol = SnmpPrivProtocol.Aes128, PrivPassword = "priv-pass-123", ContextName = "ctx" });
        var csv = Encoding.UTF8.GetString(ImportTemplateBuilder.ExportSwitchesWithCredentials([v2, v3])).TrimStart('﻿');
        var rows = Csv.Parse(csv, ',');
        rows[0].Should().Equal(ImportColumns.All);
        rows[1][0].Should().Be("SW-V2"); rows[1][4].Should().Be("v2c"); rows[1][5].Should().Be("comm,with\"quote", "CSV quoting survives commas and quotes");
        rows[2][4].Should().Be("v3"); rows[2][6].Should().Be("monitor"); rows[2][8].Should().Be("auth-pass-123"); rows[2][10].Should().Be("priv-pass-123"); rows[2][11].Should().Be("ctx");
    }
}
