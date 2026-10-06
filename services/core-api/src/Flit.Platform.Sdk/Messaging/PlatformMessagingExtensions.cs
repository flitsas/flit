using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Platform.Sdk.Messaging;

public static class PlatformMessagingExtensions
{
    /// <summary>
    /// Outbox de <typeparamref name="TContext"/> con su publicador a RabbitMQ (HU #13338). El <c>DbContext</c> debe
    /// mapear la tabla con <see cref="OutboxModelBuilderExtensions.AddFlitOutbox"/>; quien guarda un cambio pide
    /// <see cref="IPlatformOutbox"/> y encola el evento antes de su <c>SaveChanges</c>.
    /// </summary>
    public static IServiceCollection AddFlitOutbox<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options = new PlatformMessagingOptions();
        configuration.GetSection(PlatformMessagingOptions.SectionName).Bind(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        services.AddScoped<IPlatformOutbox, PlatformOutbox<TContext>>();
        services.AddHostedService<OutboxPublisherService<TContext>>();
        return services;
    }
}
