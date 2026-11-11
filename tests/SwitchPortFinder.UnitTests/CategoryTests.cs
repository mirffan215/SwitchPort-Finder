using FluentAssertions;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Discovery;

namespace SwitchPortFinder.UnitTests;

public class CategoryTests
{
    private static readonly List<CategoryRule> Rules = DeviceCategoryService.DefaultRules();
    private static CategoryContext C(string? vendor = null, string host = "", int? vlan = null, string? port = null, string neighbor = "", NeighborCaps caps = NeighborCaps.None) => new(vendor, host, vlan, port, neighbor, caps);
    private static DeviceCategory Cat(CategoryContext c) => DeviceCategoryService.Evaluate(Rules, c).Category;

    [Fact] public void A_Cisco_access_point_is_recognised_by_its_CDP_model_name_even_though_its_MAC_says_only_Cisco()
        => DeviceCategoryService.Evaluate(Rules, C("Cisco Systems, Inc", neighbor: "AP-LOBBY-1 AIR-AP2802I-E-K9 Cisco AP")).Should().Be((DeviceCategory.AccessPoint, "Cisco access point model name in CDP/LLDP (AIR-, Aironet)"));

    [Fact] public void LLDP_capabilities_decide_first() { Cat(C(caps: NeighborCaps.WlanAp | NeighborCaps.Bridge)).Should().Be(DeviceCategory.AccessPoint); Cat(C(caps: NeighborCaps.Telephone)).Should().Be(DeviceCategory.IpPhone); }

    [Fact] public void A_Cisco_phone_is_a_phone_by_host_name_even_inside_the_access_point_VLAN()
    {
        Cat(C("Cisco Systems, Inc", host: "SEP001122AABBCC")).Should().Be(DeviceCategory.IpPhone);
        Cat(C("Cisco Systems, Inc", host: "SEP001122AABBCC", vlan: 50)).Should().Be(DeviceCategory.IpPhone, "the specific rule beats the weak VLAN rule");
        Cat(C("Cisco Systems, Inc", neighbor: "Cisco IP Phone 7841", vlan: 50)).Should().Be(DeviceCategory.IpPhone);
    }

    [Fact] public void Anything_in_VLAN_50_is_probably_an_access_point_but_only_as_the_last_resort()
    {
        Cat(C("Cisco Systems, Inc", vlan: 50)).Should().Be(DeviceCategory.AccessPoint);
        Cat(C("Cisco Systems, Inc", vlan: 10)).Should().Be(DeviceCategory.Unclassified);
        DeviceCategoryService.Evaluate(Rules, C(vlan: 50)).Reason.Should().Contain("VLAN 50");
    }

    [Fact] public void Grandstream_is_an_access_point_anywhere_and_Ruijie_only_in_the_access_point_VLAN()
    {
        Cat(C("Grandstream Networks, Inc.", vlan: 10)).Should().Be(DeviceCategory.AccessPoint);
        Cat(C("Ruijie Networks Co.,LTD", vlan: 50)).Should().Be(DeviceCategory.AccessPoint);
        Cat(C("Ruijie Networks Co.,LTD", vlan: 10)).Should().Be(DeviceCategory.Unclassified);
    }

    [Fact] public void Host_name_prefixes_work_and_a_name_that_merely_contains_AP_does_not()
    {
        Cat(C(host: "AP-3rd-floor")).Should().Be(DeviceCategory.AccessPoint); Cat(C(host: "WAP-01")).Should().Be(DeviceCategory.AccessPoint);
        Cat(C(host: "LAPTOP-123")).Should().Be(DeviceCategory.Unclassified); Cat(C(host: "RAPID-PC")).Should().Be(DeviceCategory.Unclassified);
    }

    [Fact] public void Disabled_rules_are_skipped_priority_orders_them_and_a_rule_without_conditions_never_matches()
    {
        var rules = new List<CategoryRule> {
            new() { Id = 1, Name = "late", Category = DeviceCategory.Printer, Priority = 50, VendorContains = "hp" },
            new() { Id = 2, Name = "early", Category = DeviceCategory.Camera, Priority = 10, VendorContains = "hp" },
            new() { Id = 3, Name = "off", Category = DeviceCategory.Server, Priority = 1, VendorContains = "hp", Enabled = false },
            new() { Id = 4, Name = "empty", Category = DeviceCategory.Other, Priority = 2 } };
        DeviceCategoryService.Evaluate(rules, C("HP Inc.")).Category.Should().Be(DeviceCategory.Camera);
        DeviceCategoryService.Evaluate(rules, C("Dell")).Category.Should().Be(DeviceCategory.Unclassified);
    }

    [Fact] public void Rule_validation_catches_empty_and_nonsense_rules()
    {
        SwitchPortFinder.Application.Discovery.CategoryRuleRequest R(string cat = "AccessPoint", string? vendor = "x", int prio = 5, int? vlan = null, string? cap = null) => new("n", cat, true, prio, vendor, null, null, null, null, cap, vlan);
        DeviceCategoryService.RuleProblem(R()).Should().BeNull();
        DeviceCategoryService.RuleProblem(R(vendor: null)).Should().Contain("at least one condition");
        DeviceCategoryService.RuleProblem(R(cat: "Nope")).Should().Contain("category"); DeviceCategoryService.RuleProblem(R(cat: "Unclassified")).Should().Contain("category");
        DeviceCategoryService.RuleProblem(R(prio: 0)).Should().Contain("Priority"); DeviceCategoryService.RuleProblem(R(vlan: 5000)).Should().Contain("VLAN");
        DeviceCategoryService.RuleProblem(R(vendor: null, cap: "Station")).Should().Contain("capability");
        DeviceCategoryService.RuleProblem(R(vendor: new string('a', 101))).Should().Contain("100");
    }
}
