using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Alpha.Utils;

public static class HostExtensions {
    public static void AddSingletonHostedService<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        this IServiceCollection services
    )
        where T : class, IHostedService {
        services.AddSingleton<T>();
        services.AddHostedService<T>(provider => provider.GetRequiredService<T>());
    }

    public static void AddSingletonHostedService<T>(this IServiceCollection services, T instance)
        where T : class, IHostedService {
        services.AddSingleton(instance);
        services.AddHostedService<T>(provider => provider.GetRequiredService<T>());
    }
}
