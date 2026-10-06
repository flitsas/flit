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

    /// <summary>
    /// Consumidor de eventos de <paramref name="producer"/> en la cola <paramref name="queue"/> con su efecto
    /// <typeparamref name="THandler"/> (HU #13339). El <c>DbContext</c> debe mapear la bandeja con
    /// <see cref="InboxModelBuilderExtensions.AddFlitInbox"/>. Usa la conexión de <c>Platform:Messaging</c>.
    /// </summary>
    public static IServiceCollection AddFlitConsumer<TContext, THandler, TData>(
        this IServiceCollection services,
        IConfiguration configuration,
        string queue,
        string producer,
        IEnumerable<string> eventTypes,
        Action<PlatformConsumerOptions>? configure = null)
        where TContext : DbContext
        where THandler : class, IEventConsumer<TData>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        ArgumentNullException.ThrowIfNull(eventTypes);

        var messaging = new PlatformMessagingOptions();
        configuration.GetSection(PlatformMessagingOptions.SectionName).Bind(messaging);
        messaging.Validate();

        var consumer = new PlatformConsumerOptions { Queue = queue, Producer = producer };
        foreach (var type in eventTypes)
            consumer.EventTypes.Add(type);
        configure?.Invoke(consumer);
        if (consumer.EventTypes.Count == 0)
            throw new InvalidOperationException($"El consumidor {queue} no se suscribe a ningún evento.");

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<THandler>();
        services.AddHostedService(sp => new PlatformConsumer<TContext, THandler, TData>(
            sp.GetRequiredService<IServiceScopeFactory>(),
            consumer,
            messaging,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<PlatformConsumer<TContext, THandler, TData>>>()));
        return services;
    }
}
