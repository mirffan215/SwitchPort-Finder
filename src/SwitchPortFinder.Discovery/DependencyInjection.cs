using Microsoft.Extensions.DependencyInjection;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Discovery.Snmp;

namespace SwitchPortFinder.Discovery;

public static class DependencyInjection
{
    /// <summary>Registers the real SNMP client. The mock discovery provider is only ever registered by test projects.</summary>
    public static IServiceCollection AddDiscovery(this IServiceCollection services)
    {
        services.AddSingleton<ISnmpClient, SharpSnmpClient>();
        services.AddScoped<ISwitchScanner, SnmpSwitchScanner>();
        services.AddSingleton<IHealthProbe, HealthProbe>();
        return services;
    }
}
