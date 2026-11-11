using System.Text;
using FluentAssertions;
using SwitchPortFinder.Infrastructure.Security;

namespace SwitchPortFinder.UnitTests;

public class TotpTests
{
    // RFC 6238 appendix B test vectors (SHA-1, secret "12345678901234567890"); the RFC lists 8 digits, the last 6 are what a 6-digit authenticator shows.
    private static readonly byte[] Rfc = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")] [InlineData(1111111109L, "081804")] [InlineData(1111111111L, "050471")] [InlineData(1234567890L, "005924")] [InlineData(2000000000L, "279037")] [InlineData(20000000000L, "353130")]
    public void Matches_the_RFC_6238_vectors(long unixSeconds, string expected) => Totp.Code(Rfc, Totp.StepOf(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))).Should().Be(expected);

    [Fact] public void Base32_round_trips_and_matches_the_RFC_4648_vector()
    {
        Totp.Base32(Encoding.ASCII.GetBytes("foobar")).Should().Be("MZXW6YTBOI");
        Totp.FromBase32("mzxw 6ytb-oi=")!.Should().Equal(Encoding.ASCII.GetBytes("foobar"));
        var s = Totp.NewSecret(); Totp.FromBase32(Totp.Base32(s))!.Should().Equal(s);
        Totp.FromBase32("not*base32").Should().BeNull();
    }

    [Fact] public void Accepts_the_adjacent_steps_for_clock_drift_but_not_further()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000); var step = Totp.StepOf(now);
        Totp.Verify(Rfc, Totp.Code(Rfc, step), now).Should().Be(step);
        Totp.Verify(Rfc, Totp.Code(Rfc, step - 1), now).Should().Be(step - 1);
        Totp.Verify(Rfc, Totp.Code(Rfc, step + 1), now).Should().Be(step + 1);
        Totp.Verify(Rfc, Totp.Code(Rfc, step + 2), now).Should().BeNull();
        Totp.Verify(Rfc, Totp.Code(Rfc, step - 2), now).Should().BeNull();
    }

    [Fact] public void A_code_cannot_be_replayed_and_input_is_forgiving_about_spaces()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000); var step = Totp.StepOf(now); var code = Totp.Code(Rfc, step);
        Totp.Verify(Rfc, code, now, lastUsedStep: step).Should().BeNull("the same step cannot be used twice");
        Totp.Verify(Rfc, code[..3] + " " + code[3..], now).Should().Be(step);
        Totp.Verify(Rfc, "12345", now).Should().BeNull(); Totp.Verify(Rfc, null, now).Should().BeNull(); Totp.Verify(Rfc, "abcdef", now).Should().BeNull();
    }

    [Fact] public void Recovery_codes_are_unique_normalised_and_hashed()
    {
        var codes = Totp.NewRecoveryCodes(); codes.Should().HaveCount(10).And.OnlyHaveUniqueItems();
        codes.Should().OnlyContain(c => c.Length == 14 && c[4] == '-');
        Totp.HashRecovery(codes[0]).Should().Be(Totp.HashRecovery(codes[0].ToLowerInvariant().Replace("-", " ")));
        Totp.HashRecovery(codes[0]).Should().NotBe(Totp.HashRecovery(codes[1])).And.NotContain(codes[0][..4]);
    }

    [Fact] public void The_provisioning_uri_is_what_authenticator_apps_expect()
    {
        var u = Totp.Uri("SwitchPort Finder", "admin@lab", Rfc);
        u.Should().StartWith("otpauth://totp/SwitchPort%20Finder:admin%40lab?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ").And.Contain("issuer=SwitchPort%20Finder").And.Contain("digits=6").And.Contain("period=30");
    }
}
