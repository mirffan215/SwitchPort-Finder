using FluentAssertions;
using MimeKit;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Infrastructure.Notifications;

namespace SwitchPortFinder.UnitTests;

public class AlertLogicTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
    private static List<(DateTime At, double V)> S(params (int MinutesAgo, double V)[] p) => p.OrderByDescending(x => x.MinutesAgo).Select(x => (Now.AddMinutes(-x.MinutesAgo), x.V)).ToList();

    [Fact] public void A_reading_above_the_limit_for_the_whole_duration_is_a_breach()
        => AlertService.Sustained(S((10, 85), (7, 90), (4, 88), (1, 92)), 80, 10, Now).Should().Be((true, false, 92));

    [Fact] public void One_dip_below_the_limit_inside_the_window_prevents_the_alert()
        => AlertService.Sustained(S((10, 85), (7, 70), (4, 88), (1, 92)), 80, 10, Now).Breach.Should().BeFalse();

    [Fact] public void A_short_spike_is_not_enough_when_a_duration_is_required()
        => AlertService.Sustained(S((2, 95), (1, 96)), 80, 10, Now).Breach.Should().BeFalse("only 1 minute of data above the limit, 10 required");

    [Fact] public void Duration_zero_means_the_latest_reading_decides()
    {
        AlertService.Sustained(S((5, 10), (1, 95)), 80, 0, Now).Breach.Should().BeTrue();
        AlertService.Sustained(S((5, 95), (1, 10)), 80, 0, Now).Breach.Should().BeFalse();
    }

    [Fact] public void An_alert_clears_only_below_90_percent_of_the_limit_so_it_does_not_flap()
    {
        AlertService.Sustained(S((10, 85), (1, 78)), 80, 10, Now).Should().Be((false, false, 78), "below the limit but not below 72: stays open");
        AlertService.Sustained(S((10, 85), (1, 70)), 80, 10, Now).Clear.Should().BeTrue();
    }

    [Fact] public void Old_readings_neither_open_nor_close_anything()
    {
        var r = AlertService.Sustained(S((200, 95), (190, 96)), 80, 10, Now); (r.Breach, r.Clear).Should().Be((false, false));
        AlertService.Sustained([], 80, 10, Now).Should().Be((false, false, 0));
    }

    [Fact] public void Recipients_are_parsed_deduplicated_and_junk_is_dropped()
    {
        var r = MailSender.ParseRecipients("a@x.com; B@x.com, a@x.com\nnot-an-address;  c@y.org ");
        r.Select(x => x.Address).Should().Equal("a@x.com", "B@x.com", "c@y.org");
        MailSender.ParseRecipients(null).Should().BeEmpty();
    }

    [Fact] public void Config_problems_are_reported_in_plain_words()
    {
        MailSender.ConfigProblem(new NotificationConfig { Host = "", FromAddress = "a@b.com" }).Should().Contain("host");
        MailSender.ConfigProblem(new NotificationConfig { Host = "smtp", Port = 0, FromAddress = "a@b.com" }).Should().Contain("port");
        MailSender.ConfigProblem(new NotificationConfig { Host = "smtp", Port = 587, FromAddress = "nope" }).Should().Contain("From");
        MailSender.ConfigProblem(new NotificationConfig { Host = "smtp", Port = 587, FromAddress = "a@b.com" }).Should().BeNull();
    }

    [Fact] public void The_e_mail_has_a_clear_subject_html_is_encoded_and_the_link_is_included()
    {
        var cfg = new NotificationConfig { PublicBaseUrl = "https://spf.example.com:8450" };
        AlertEvent E(string title, string detail = "d") => new() { Title = title, Detail = detail, FirstAt = Now };
        var (s1, t1, _) = AlertService.Compose(cfg, [E("Uplink utilization: SW1 Gi1/0/24 at 92 %")], [], [], Now); s1.Should().Be("[ALERT] Uplink utilization: SW1 Gi1/0/24 at 92 %"); t1.Should().Contain("https://spf.example.com:8450");
        var (s2, _, h2) = AlertService.Compose(cfg, [E("<script>x</script>", "a & b")], [E("two")], [], Now); s2.Should().Contain("1 new, 1 ongoing, 0 resolved");
        h2.Should().NotContain("<script>").And.Contain("&lt;script&gt;").And.Contain("a &amp; b");
        AlertService.Compose(cfg, [], [], [E("SW1 back")], Now).Subject.Should().StartWith("[RESOLVED]");
    }
}
