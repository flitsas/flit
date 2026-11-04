using Microsoft.Extensions.Hosting;

namespace Flit.PersonaNatural.Api.Pruebas;

/// <summary>
/// Interruptor de PRUEBAS locales (sin HU). Apagado por defecto. <c>PersonaNatural:Pruebas:OmitirIdentidad=true</c> solo
/// se respeta con <c>ASPNETCORE_ENVIRONMENT=Development</c>: en cualquier otro ambiente se ignora (y se avisa al arrancar).
/// </summary>
public interface IPruebasPortal
{
    /// <summary>Verdadero solo si el flag está activo Y el ambiente es Development.</summary>
    bool OmitirIdentidad { get; }
}

public sealed class PruebasPortal(bool omitirIdentidad) : IPruebasPortal
{
    public const string ClaveOmitirIdentidad = "PersonaNatural:Pruebas:OmitirIdentidad";

    public bool OmitirIdentidad { get; } = omitirIdentidad;

    /// <summary>Lo que el operador pidió, sin filtrar por ambiente (para el aviso de arranque).</summary>
    public static bool Solicitado(IConfiguration configuration) => configuration.GetValue(ClaveOmitirIdentidad, false);

    public static IServiceCollection AddPruebasPortal(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        var solicitado = Solicitado(configuration);
        var efectivo = solicitado && environment.IsDevelopment();
        services.AddSingleton<IPruebasPortal>(new PruebasPortal(efectivo));
        if (solicitado)
            services.AddHostedService(sp => new AvisoPruebasPortal(sp.GetRequiredService<ILogger<AvisoPruebasPortal>>(), efectivo));
        return services;
    }
}

internal sealed partial class AvisoPruebasPortal(ILogger<AvisoPruebasPortal> logger, bool efectivo) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (efectivo)
            AvisoActivo(logger);
        else
            AvisoIgnorado(logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 9001, Level = LogLevel.Warning, Message = "Validación de identidad OMITIDA (solo pruebas)")]
    private static partial void AvisoActivo(ILogger logger);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Warning, Message = "PersonaNatural:Pruebas:OmitirIdentidad=true IGNORADO: solo se respeta con ASPNETCORE_ENVIRONMENT=Development")]
    private static partial void AvisoIgnorado(ILogger logger);
}
