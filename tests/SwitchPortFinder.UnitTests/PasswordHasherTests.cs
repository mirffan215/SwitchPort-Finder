using FluentAssertions;
using SwitchPortFinder.Infrastructure.Security;

namespace SwitchPortFinder.UnitTests;

public class PasswordHasherTests
{
    [Fact] public void Hash_round_trips_and_is_salted()
    {
        var a = PasswordHasher.Hash("correct horse battery staple"); var b = PasswordHasher.Hash("correct horse battery staple");
        a.Should().NotBe(b, "every hash has its own random salt");
        PasswordHasher.Verify("correct horse battery staple", a).Should().BeTrue();
        PasswordHasher.Verify("correct horse battery stapl3", a).Should().BeFalse();
        PasswordHasher.Verify("", a).Should().BeFalse();
    }

    [Fact] public void Hash_format_is_versioned_with_210k_iterations_and_never_contains_the_password()
    {
        var h = PasswordHasher.Hash("a-very-secret-passphrase");
        h.Should().StartWith("v1.210000.").And.NotContain("secret");
    }

    [Theory] [InlineData("")] [InlineData("v1")] [InlineData("v1.x.y.z")] [InlineData("v2.1.AA==.AA==")] [InlineData("v1.1000.not-base64!.also-bad!")]
    public void Garbage_stored_hashes_never_verify_and_never_throw(string stored) => PasswordHasher.Verify("anything", stored).Should().BeFalse();

    [Fact] public void Tampered_hash_fails()
    {
        var h = PasswordHasher.Hash("a-very-secret-passphrase"); var parts = h.Split('.');
        parts[3] = Convert.ToBase64String(new byte[32]);
        PasswordHasher.Verify("a-very-secret-passphrase", string.Join('.', parts)).Should().BeFalse();
    }

    [Theory]
    [InlineData("short", false)] [InlineData("aaaaaaaaaaaa", false)] [InlineData("alice-is-here-1", false)] [InlineData("a-long-enough-passphrase", true)]
    public void Password_policy(string pw, bool ok) => (PasswordHasher.Policy(pw, "alice") is null).Should().Be(ok);

    [Fact] public void Policy_rejects_null_and_absurdly_long() { PasswordHasher.Policy(null).Should().NotBeNull(); PasswordHasher.Policy(new string('x', 300) + "abcd").Should().NotBeNull(); }
}
