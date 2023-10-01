using Arc4u.Diagnostics.Monitoring;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class SystemResourcesExtension
    {
        public static IServiceCollection AddSystemMonitoring(this IServiceCollection services)
        {
            services.AddHostedService<SystemResources>();

            return services;
        }
    }
}
