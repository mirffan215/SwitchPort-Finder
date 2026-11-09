using System.Text.RegularExpressions;
using FluentAssertions;
using SwitchPortFinder.Application.Common;

namespace SwitchPortFinder.UnitTests;

/// <summary>The spec's hard rules, enforced against the source tree so they cannot silently regress.</summary>
public class GuardrailTests
{
    private static readonly string Root = FindRoot();
    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "SwitchPortFinder.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static IEnumerable<string> Files(string sub, params string[] ext) =>
        Directory.EnumerateFiles(Path.Combine(Root, sub), "*.*", SearchOption.AllDirectories)
            .Where(f => ext.Contains(Path.GetExtension(f)) && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains("node_modules") && !f.Contains($"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"));

    private static IEnumerable<(string File, int Line, string Text)> Lines(IEnumerable<string> files) =>
        files.SelectMany(f => File.ReadLines(f).Select((t, i) => (f, i + 1, t)));

    [Fact]
    public void The_application_is_strictly_read_only_no_snmp_set_trap_or_inform_anywhere_in_production_code()
    {
        var forbidden = new Regex(@"\b(SetRequestMessage|SetAsync|SendTrap\w*|SendInform\w*|Messenger\.Set\b|TrapV\d|InformRequest\w*|SetRequestPdu)\b");
        var hits = Lines(Files("src", ".cs")).Where(l => forbidden.IsMatch(l.Text) && !l.Text.TrimStart().StartsWith("//") && !l.Text.TrimStart().StartsWith("///") && !l.Text.TrimStart().StartsWith("*")).ToList();
        hits.Should().BeEmpty("only SNMP read PDUs may exist: " + string.Join("; ", hits.Select(h => $"{Path.GetFileName(h.File)}:{h.Line}")));
    }

    [Fact]
    public void There_are_no_hard_coded_private_networks_in_production_code_or_the_frontend()
    {
        var rfc1918 = new Regex(@"(?<![\d.])(10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})(?![\d.])");
        var files = Files("src", ".cs", ".ts", ".tsx", ".json", ".html", ".css").Where(f => !f.EndsWith("package-lock.json"));
        var hits = Lines(files).Where(l => rfc1918.IsMatch(l.Text)).ToList();
        hits.Should().BeEmpty("production code must be network-agnostic: " + string.Join("; ", hits.Select(h => $"{Path.GetFileName(h.File)}:{h.Line} {h.Text.Trim()}")));
    }

    [Fact]
    public void The_frontend_source_has_no_external_urls_so_nothing_depends_on_the_internet_or_a_cdn()
    {
        var url = new Regex(@"https?://(?!localhost|127\.0\.0\.1|www\.w3\.org)[\w.-]+", RegexOptions.IgnoreCase);
        var files = Files("src/SwitchPortFinder.Web", ".ts", ".tsx", ".css", ".html").Where(f => !f.Contains("package-lock"));
        Lines(files).Where(l => url.IsMatch(l.Text)).Should().BeEmpty();
    }

    [Fact]
    public void The_built_frontend_bundle_makes_no_external_requests()
    {
        var dist = Path.Combine(Root, "src", "SwitchPortFinder.Api", "wwwroot");
        if (!Directory.Exists(dist)) return;   // frontend not built in this environment
        var url = new Regex(@"(src|href)=[""']https?://", RegexOptions.IgnoreCase);
        Directory.EnumerateFiles(dist, "*.html", SearchOption.AllDirectories).Where(f => url.IsMatch(File.ReadAllText(f))).Should().BeEmpty();
        Directory.EnumerateFiles(dist, "*.css", SearchOption.AllDirectories).Where(f => Regex.IsMatch(File.ReadAllText(f), @"@import\s+url\(\s*[""']?https?://|url\(\s*[""']?https?://")).Should().BeEmpty("no remote fonts/stylesheets");
    }

    [Fact]
    public void Credentials_are_never_interpolated_into_log_messages_in_production_code()
    {
        // Cheap tripwire: a log call that mentions a secret-bearing member by name.
        var secretNames = new Regex(@"\b(Community|AuthPassword|PrivPassword|Password|EncryptedSecrets|\.Username)\b");
        var logCall = new Regex(@"\blog(ger)?\.Log(Trace|Debug|Information|Warning|Error|Critical)\(|_log\.");
        var hits = Lines(Files("src", ".cs")).Where(l => logCall.IsMatch(l.Text) && secretNames.IsMatch(l.Text)).ToList();
        hits.Should().BeEmpty(string.Join("; ", hits.Select(h => $"{Path.GetFileName(h.File)}:{h.Line}")));
    }
}

public class TargetPolicyTests
{
    [Theory]
    [InlineData("192.0.2.1")] [InlineData("2001:db8::1")] [InlineData("169.254.1.1")] [InlineData("100.64.0.1")] [InlineData("10.0.0.1")]
    public void Ordinary_unicast_addresses_are_allowed(string ip) => TargetPolicy.Check(ip, allowLoopback: false).Should().BeNull();

    [Theory]
    [InlineData("0.0.0.0")] [InlineData("::")] [InlineData("255.255.255.255")] [InlineData("224.0.0.1")] [InlineData("239.255.255.250")] [InlineData("ff02::1")]
    public void Never_valid_targets_are_rejected_even_in_development(string ip) => TargetPolicy.Check(ip, allowLoopback: true).Should().NotBeNull();

    [Theory] [InlineData("127.0.0.1")] [InlineData("127.1.2.3")] [InlineData("::1")] [InlineData("::ffff:127.0.0.1")]
    public void Loopback_needs_an_explicit_development_opt_in(string ip)
    {
        TargetPolicy.Check(ip, allowLoopback: false).Should().NotBeNull(); TargetPolicy.Check(ip, allowLoopback: true).Should().BeNull();
    }

    [Fact] public void Garbage_is_rejected() => TargetPolicy.Check("not-an-ip", true).Should().NotBeNull();
}
