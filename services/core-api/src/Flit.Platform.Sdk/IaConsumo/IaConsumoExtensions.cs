using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Flit.Platform.Sdk.IaConsumo;

public static class IaConsumoServiceCollectionExtensions
{
    /// <summary>
    /// Registro de consumo de IA (contrato ia-consumo v1) sobre la base de <typeparamref name="TContext"/>, que debe mapear
    /// las tablas con <see cref="IaConsumoModelBuilderExtensions.AddFlitIaConsumo"/>. Registra el contexto ambiental, el
    /// registrador que usan los clientes HTTP, el escritor por lotes y la purga diaria del detalle.
    /// </summary>
    /// <param name="aplicativo">flit | flito | ...</param>
    /// <param name="servicio">core-api | core-comparendos | core-diagnostico | ...</param>
    public static IServiceCollection AddFlitIaConsumo<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string aplicativo,
        string servicio)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(aplicativo);
        ArgumentException.ThrowIfNullOrWhiteSpace(servicio);

        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(new IaConsumoOpciones(configuration, aplicativo, servicio));
        services.AddSingleton<IaConsumoCola>();
        services.AddSingleton<IaCalculadoraCosto>();
        services.AddSingleton<IIaCallContext>(IaCallContext.Instancia);
        services.AddSingleton<IIaConsumoRegistrador, IaConsumoRegistrador>();
        services.AddScoped<IIaConsumoAlmacen, EfIaConsumoAlmacen<TContext>>();
        services.AddHostedService<IaConsumoEscritor>();
        services.AddHostedService<IaConsumoRetencion>();
        return services;
    }
}
