using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Reports;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.IntegrationTests;

[Collection("api")]
public class ReportsApiTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static readonly DateTime T0 = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Two switches with a scripted history: A is up, up, DOWN, DOWN, up (30 min polls); B is always up with higher CPU.</summary>
    private async Task<(int A, int B, string NameA)> Seed()
    {
        using var scope = fx.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SwitchMetrics.RemoveRange(db.SwitchMetrics); db.PortMetrics.RemoveRange(db.PortMetrics); db.Switches.RemoveRange(db.Switches.Where(x => x.Name.Contains("RPT-"))); await db.SaveChangesAsync();
        var tag = Guid.NewGuid().ToString("N")[..5];
        var a = new Switch { Name = $"=RPT-A-{tag}", ManagementIp = "198.18.50.1", Status = SwitchStatus.Reachable, CpuPct = 30, MemPct = 40, Location = "Lab" };   // starts with '=' on purpose: exports must neutralise it
        var b = new Switch { Name = $"RPT-B-{tag}", ManagementIp = "198.18.50.2", Status = SwitchStatus.Reachable, CpuPct = 80, MemPct = 90 };
        db.Switches.AddRange(a, b); await db.SaveChangesAsync();
        bool[] up = [true, true, false, false, true];
        for (var i = 0; i < 5; i++)
        {
            db.SwitchMetrics.Add(new SwitchMetricSample { SwitchId = a.Id, At = T0.AddMinutes(30 * i), Reachable = up[i], CpuPct = up[i] ? 10 + i * 10 : null, MemPct = up[i] ? 50 : null, LatencyMs = up[i] ? 5 : null, UptimeSeconds = up[i] ? (i < 4 ? 1000 + i * 1800 : 100) : null });
            db.SwitchMetrics.Add(new SwitchMetricSample { SwitchId = b.Id, At = T0.AddMinutes(30 * i), Reachable = true, CpuPct = 70 + i, MemPct = 60, LatencyMs = 8 });
        }
        var p = new SwitchPort { SwitchId = a.Id, IfIndex = 1, Name = "Gi1/0/1", OperUp = true, PortType = PortType.SwitchUplink, SpeedMbps = 1000 }; db.SwitchPorts.Add(p); await db.SaveChangesAsync();
        db.PortMetrics.Add(new PortMetricSample { SwitchId = a.Id, PortId = p.Id, At = T0, InBps = 100e6, OutBps = 50e6, UtilPct = 10, DiscardsPerSec = 2 });
        db.PortMetrics.Add(new PortMetricSample { SwitchId = a.Id, PortId = p.Id, At = T0.AddDays(1), InBps = 300e6, OutBps = 50e6, UtilPct = 30, DiscardsPerSec = 4 });
        await db.SaveChangesAsync(); return (a.Id, b.Id, a.Name);
    }

    private async Task<ReportResult> Run(ReportQuery q) { var r = await _http.PostAsJsonAsync("/api/reports/run", q, Json); r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<ReportResult>(Json))!; }
    private static double Num(object? v) => ((JsonElement)v!).GetDouble();
    private static string Str(object? v) => ((JsonElement)v!).ToString();
    private static ReportQuery Hist(string ds) => new() { Dataset = ds, From = T0.AddHours(-1), To = T0.AddDays(2) };

    [Fact]
    public async Task Availability_downtime_outages_and_restarts_are_computed_from_the_poll_history()
    {
        var (_, _, nameA) = await Seed();
        var q = Hist("availability"); q.Columns = ["switch", "availabilityPct", "downtimeMinutes", "outages", "restarts", "avgCpuPct", "maxCpuPct"];
        var r = await Run(q); var row = r.Rows.Single(x => Str(x[0]) == nameA);
        Num(row[1]).Should().BeApproximately(60, 0.01, "3 of 5 samples were up");
        Num(row[2]).Should().BeApproximately(60, 0.01, "two failed samples x 30 minutes until the next sample");
        Num(row[3]).Should().Be(1, "one run of consecutive failures is one outage"); Num(row[4]).Should().Be(1, "uptime dropped from 4600 s to 100 s: one restart");
        Num(row[5]).Should().BeApproximately((10 + 20 + 50) / 3.0, 0.01, "CPU of unreachable samples is not part of the average"); Num(row[6]).Should().Be(50);
    }

    [Fact]
    public async Task Summaries_group_aggregate_bucket_by_time_filter_sort_and_limit()
    {
        var (_, _, nameA) = await Seed();
        var q = Hist("switch_health"); q.GroupBy = ["switch"]; q.Measures = [new("cpuPct", "avg"), new("cpuPct", "max"), new("*", "count")]; q.SortBy = "avg_cpuPct"; q.Desc = true;
        var r = await Run(q);
        r.Columns.Select(c => c.Key).Should().Equal("switch", "avg_cpuPct", "max_cpuPct", "count");
        Str(r.Rows[0][0]).Should().StartWith("RPT-B-"); Num(r.Rows[0][1]).Should().BeApproximately(72, 0.01); Num(r.Rows[0][3]).Should().Be(5);
        r.Columns[1].Unit.Should().Be("%");

        var byDay = Hist("port_traffic"); byDay.GroupBy = ["at"]; byDay.TimeBucket = "day"; byDay.Measures = [new("utilPct", "avg"), new("discardsPerSec", "sum")];
        var d = await Run(byDay); d.Rows.Should().HaveCount(2); Str(d.Rows[0][0]).Should().Be("2026-03-01"); Num(d.Rows[0][1]).Should().Be(10); Num(d.Rows[1][2]).Should().Be(4);

        var f = Hist("switch_health"); f.Columns = ["switch", "cpuPct"]; f.Filters = [new("switch", "contains", "RPT-B"), new("cpuPct", "gte", "73")]; var fr = await Run(f); fr.Rows.Should().HaveCount(2);
        var lim = Hist("switch_health"); lim.Columns = ["switch"]; lim.Limit = 3; var lr = await Run(lim); (lr.Rows.Count, lr.TotalRows, lr.Truncated).Should().Be((3, 10, true));
    }

    [Fact]
    public async Task Current_state_datasets_work_and_dates_are_formatted()
    {
        await Seed();
        var sw = await Run(new ReportQuery { Dataset = "switches", Columns = ["name", "status", "cpuPct", "memPct"], Filters = [new("name", "contains", "RPT-")] });
        sw.Rows.Should().HaveCount(2); sw.Columns.Select(c => c.Label).Should().Equal("Switch", "Status", "CPU", "Memory");
        (await Run(new ReportQuery { Dataset = "ports", GroupBy = ["type"], Measures = [new("*", "count")] })).Rows.Should().NotBeEmpty();
        (await Run(new ReportQuery { Dataset = "devices", Columns = ["hostname", "lastSeen"], Limit = 1 })).Should().NotBeNull();
    }

    [Theory]
    [InlineData("nope", null, null, null)] [InlineData("switches", "no_such_field", null, null)] [InlineData("switches", "name", "median", null)] [InlineData("switches", "name", "sum", null)]
    [InlineData("switches", "cpuPct", "avg", "'; DROP TABLE Switches;--")]
    public async Task Hostile_or_invalid_queries_are_rejected_cleanly_never_executed(string ds, string? field, string? agg, string? filterValue)
    {
        var q = new ReportQuery { Dataset = ds };
        if (field is not null && agg is not null) q.Measures = [new(field, agg)]; else if (field is not null) q.Columns = [field];
        if (filterValue is not null) q.Filters = [new("name", "contains", filterValue)];
        var r = await _http.PostAsJsonAsync("/api/reports/run", q, Json);
        if (filterValue is null) r.StatusCode.Should().Be(HttpStatusCode.BadRequest); else r.StatusCode.Should().Be(HttpStatusCode.OK, "a hostile filter VALUE is just text compared in memory");
        (await r.Content.ReadAsStringAsync()).Should().NotContain("SqlException").And.NotContain("   at ");
        foreach (var bad in new object[] { new { dataset = "switches", filters = new[] { new { field = "name", op = "; DROP", value = "x" } } }, new { dataset = "switches", timeBucket = "century", groupBy = new[] { "name" } }, new { dataset = "switch_health", from = "2000-01-01", to = "2026-01-01", columns = new[] { "at" } } })
            ((int)(await _http.PostAsJsonAsync("/api/reports/run", bad, Json)).StatusCode).Should().BeLessThan(500);
    }

    [Fact]
    public async Task Excel_and_csv_exports_contain_the_data_and_neutralise_formulas()
    {
        var (_, _, nameA) = await Seed();
        var q = Hist("switch_health"); q.GroupBy = ["switch"]; q.Measures = [new("cpuPct", "avg")];
        var x = await _http.PostAsJsonAsync("/api/reports/export?format=xlsx", new { query = q, title = "CPU =report" }, Json);
        x.StatusCode.Should().Be(HttpStatusCode.OK); x.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".xlsx").And.NotContain("=");
        using var wb = new XLWorkbook(new MemoryStream(await x.Content.ReadAsByteArrayAsync())); var ws = wb.Worksheet("Report");
        ws.Cell(4, 1).GetString().Should().Be("Switch"); ws.Cell(4, 2).GetString().Should().Contain("Average CPU");
        var rowA = Enumerable.Range(5, 6).First(r => ws.Cell(r, 1).GetString() == nameA);
        ws.Cell(rowA, 1).GetString().Should().StartWith("=", "stored as text, not evaluated"); ws.Cell(rowA, 1).DataType.Should().Be(XLDataType.Text);
        ws.Cell(rowA, 2).DataType.Should().Be(XLDataType.Number); wb.Worksheet("Definition").Cell(2, 1).GetString().Should().Contain("Switch health history");
        var c = await _http.PostAsJsonAsync("/api/reports/export?format=csv", new { query = q, title = "x" }, Json);
        var csv = Encoding.UTF8.GetString(await c.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        csv.Should().Contain("Switch,Average CPU (%)").And.NotContain("\n=RPT", "a leading = is neutralised in CSV");
    }

    [Fact]
    public async Task Saved_reports_can_be_created_renamed_loaded_and_deleted_and_bad_definitions_are_refused()
    {
        var name = "Saved " + Guid.NewGuid().ToString("N")[..5];
        var json = JsonSerializer.Serialize(new { query = new { dataset = "switches" }, chart = "bar" });
        var c = await _http.PostAsJsonAsync("/api/reports", new SaveReportRequest(name, json), Json); c.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await c.Content.ReadFromJsonAsync<SavedReportDto>(Json))!.Id;
        (await _http.GetFromJsonAsync<SavedReportDto>($"/api/reports/{id}", Json))!.Json.Should().Be(json);
        (await _http.PutAsJsonAsync($"/api/reports/{id}", new SaveReportRequest(name + " v2", json), Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _http.GetFromJsonAsync<List<SavedReportDto>>("/api/reports", Json))!.Should().Contain(r => r.Name == name + " v2");
        foreach (var bad in new[] { new SaveReportRequest("", json), new SaveReportRequest("x", "not json"), new SaveReportRequest("x", "[1,2]"), new SaveReportRequest("x", new string('a', 30_000)) })
            (await _http.PostAsJsonAsync("/api/reports", bad, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _http.DeleteAsync($"/api/reports/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _http.GetAsync($"/api/reports/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_report_can_be_limited_to_one_or_several_switches_for_current_and_history_datasets()
    {
        var (a, b, nameA) = await Seed();
        var one = Hist("switch_health"); one.SwitchIds = [a]; one.GroupBy = ["switch"]; one.Measures = [new("*", "count")];
        var r1 = await Run(one); r1.Rows.Should().ContainSingle(); Str(r1.Rows[0][0]).Should().Be(nameA);
        var both = Hist("switch_health"); both.SwitchIds = [a, b]; both.GroupBy = ["switch"]; both.Measures = [new("*", "count")]; (await Run(both)).Rows.Should().HaveCount(2);
        var avail = Hist("availability"); avail.SwitchIds = [b]; avail.Columns = ["switch"]; (await Run(avail)).Rows.Should().ContainSingle();
        var traffic = Hist("port_traffic"); traffic.SwitchIds = [b]; traffic.Columns = ["switch"]; (await Run(traffic)).Rows.Should().BeEmpty("only switch A has traffic samples");
        var current = new ReportQuery { Dataset = "switches", Columns = ["name"], SwitchIds = [b] }; var rc = await Run(current); rc.Rows.Should().ContainSingle(); Str(rc.Rows[0][0]).Should().StartWith("RPT-B-");
        var ports = new ReportQuery { Dataset = "ports", Columns = ["switch", "port"], SwitchIds = [a] }; (await Run(ports)).Rows.Should().OnlyContain(x => Str(x[0]) == nameA);
        var none = new ReportQuery { Dataset = "switches", Columns = ["name"], SwitchIds = [999999] }; (await Run(none)).Rows.Should().BeEmpty();
        var x = await _http.PostAsJsonAsync("/api/reports/export?format=xlsx", new { query = one, title = "one switch" }, Json);
        using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(await x.Content.ReadAsByteArrayAsync()));
        string.Join("|", wb.Worksheet("Definition").CellsUsed().Select(c => c.GetString())).Should().Contain("Switches: " + nameA);
    }

    [Fact] public async Task The_dataset_catalogue_is_served() => (await _http.GetFromJsonAsync<List<DatasetDef>>("/api/reports/datasets", Json))!.Select(d => d.Key).Should().Contain(["switches", "ports", "devices", "switch_health", "port_traffic", "availability"]);
}

/// <summary>Viewers may run and export reports (read-only) but not save them.</summary>
public class ReportsRbacTests(AuthApiFixture fx) : IClassFixture<AuthApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task A_viewer_can_run_and_export_but_not_save_or_delete()
    {
        var admin = fx.CreateClient(); (await admin.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", AuthApiFixture.AdminPassword), Json)).EnsureSuccessStatusCode();
        var u = "rpt-viewer-" + Guid.NewGuid().ToString("N")[..5];
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(u, "Viewer-Passphrase-2026", UserRole.Viewer), Json)).EnsureSuccessStatusCode();
        var v = fx.CreateClient(); (await v.PostAsJsonAsync("/api/auth/login", new LoginRequest(u, "Viewer-Passphrase-2026"), Json)).EnsureSuccessStatusCode();
        var q = new ReportQuery { Dataset = "switches", Columns = ["name"] };
        (await v.PostAsJsonAsync("/api/reports/run", q, Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await v.PostAsJsonAsync("/api/reports/export?format=csv", new { query = q, title = "x" }, Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await v.GetAsync("/api/reports")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await v.PostAsJsonAsync("/api/reports", new SaveReportRequest("x", "{}"), Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.DeleteAsync("/api/reports/1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fx.CreateClient().PostAsJsonAsync("/api/reports/run", q, Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
