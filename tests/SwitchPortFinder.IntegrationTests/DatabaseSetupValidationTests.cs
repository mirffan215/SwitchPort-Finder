using FluentAssertions;
using Xunit;

namespace SwitchPortFinder.IntegrationTests;

public class DatabaseSetupValidationTests
{
    [Theory] [InlineData("SwitchPortFinder", true)] [InlineData("spf_prod-2", true)] [InlineData("", false)] [InlineData(null, false)] [InlineData("bad name", false)]
    [InlineData("x]; DROP DATABASE master;--", false)] [InlineData("a'b", false)] [InlineData("db.name", false)] [InlineData("é", false)]
    public void Database_names_are_strictly_validated(string? name, bool ok) => SwitchPortFinder.Api.Infrastructure.DatabaseSetup.IsValidDatabaseName(name).Should().Be(ok);

    [Theory]
    [InlineData(@"NT SERVICE\SwitchPortFinder", true)] [InlineData(@"CORP\svc-spf", true)] [InlineData(@"CORP\gmsa-spf$", true)] [InlineData("spf_svc", true)]
    [InlineData(@"a\b\c", false)] [InlineData("x]; DROP LOGIN sa;--", false)] [InlineData("a'b", false)] [InlineData("", false)] [InlineData(null, false)]
    public void Account_names_are_strictly_validated(string? name, bool ok) => SwitchPortFinder.Api.Infrastructure.DatabaseSetup.IsValidPrincipal(name).Should().Be(ok);

    [Fact] public void Only_names_with_a_backslash_are_treated_as_windows_accounts()
    {
        SwitchPortFinder.Api.Infrastructure.DatabaseSetup.IsWindowsPrincipal(@"NT SERVICE\SwitchPortFinder").Should().BeTrue();
        SwitchPortFinder.Api.Infrastructure.DatabaseSetup.IsWindowsPrincipal("spf_svc").Should().BeFalse();
    }
}
