using Flit.Platform.Sdk.Messaging;

namespace Flit.Consultas.Api.Bus;

/// <summary>Mensajes que una cola atiende a la vez y los que tiene en vuelo (ADR-0075, D1).</summary>
internal sealed record ColaConsumidor(ushort Concurrencia, ushort Prefetch)
{
    /// <summary>Aplica la concurrencia, el prefetch y los productores permitidos a la suscripción del SDK.</summary>
    public void Aplicar(PlatformConsumerOptions consumidor, IEnumerable<string> productoresPermitidos)
    {
        ArgumentNullException.ThrowIfNull(consumidor);
        ArgumentNullException.ThrowIfNull(productoresPermitidos);
        consumidor.Concurrencia = Concurrencia;
        consumidor.Prefetch = Prefetch;
        foreach (var productor in productoresPermitidos)
            consumidor.ProductoresPermitidos.Add(productor);
    }
}

/// <summary>
/// Configuración de los consumidores de trabajos del diagnóstico (ADR-0075), sección <see cref="Seccion"/>:
/// <list type="bullet">
/// <item><see cref="Lote"/> (cola <c>consultas.diagnostico</c>): Concurrencia 8 / Prefetch 16. <see cref="Express"/> (cola
/// <c>consultas.diagnostico-express</c>): 4 / 8. Concurrencia al menos 1 y no mayor que el prefetch (lo exige el SDK).</item>
/// <item><see cref="ProductoresPermitidos"/>: por defecto y si la lista llega vacía, <c>["diagnostico"]</c> (Q3 = 3a). La
/// configuración puede cambiar quién (las pruebas usan su propio usuario del broker) pero nunca dejarla abierta a cualquiera.</item>
/// <item><see cref="TiempoMaximo"/>: el tope de UNA ejecución (el deadline que hoy pone Diagnóstico a su llamada gRPC, 90 s por
/// defecto); vencido, el resultado es ERROR <c>DeadlineExceeded</c>.</item>
/// </list>
/// Un valor inválido impide arrancar y dice qué variable de entorno corregir.
/// </summary>
/// <example>
/// <code>
/// var opciones = OpcionesConsumidoresDiagnostico.Desde(builder.Configuration);
/// services.AddFlitConsumer&lt;...&gt;(..., o => opciones.Lote.Aplicar(o, opciones.ProductoresPermitidos));
/// </code>
/// </example>
internal sealed class OpcionesConsumidoresDiagnostico
{
    public const string Seccion = "Consultas:Diagnostico";

    public const int TiempoMaximoPorDefectoSegundos = 90;

    /// <summary>El único productor de estos trabajos (usuario del broker de core-diagnostico).</summary>
    public const string ProductorDiagnostico = "diagnostico";

    public TimeSpan TiempoMaximo { get; init; } = TimeSpan.FromSeconds(TiempoMaximoPorDefectoSegundos);

    public ColaConsumidor Lote { get; init; } = new(8, 16);

    public ColaConsumidor Express { get; init; } = new(4, 8);

    public IReadOnlyList<string> ProductoresPermitidos { get; init; } = [ProductorDiagnostico];

    /// <summary>Lee y valida la sección; lanza <see cref="InvalidOperationException"/> si el servicio no debe arrancar.</summary>
    public static OpcionesConsumidoresDiagnostico Desde(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var segundos = configuration.GetValue($"{Seccion}:TiempoMaximoSegundos", TiempoMaximoPorDefectoSegundos);
        if (segundos <= 0)
            throw Invalida("TiempoMaximoSegundos", "debe ser mayor que 0", segundos);

        var productores = configuration.GetSection($"{Seccion}:ProductoresPermitidos").GetChildren()
            .Select(c => c.Value?.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new OpcionesConsumidoresDiagnostico
        {
            TiempoMaximo = TimeSpan.FromSeconds(segundos),
            Lote = Cola(configuration, "Lote", new ColaConsumidor(8, 16)),
            Express = Cola(configuration, "Express", new ColaConsumidor(4, 8)),
            ProductoresPermitidos = productores.Count > 0 ? productores : [ProductorDiagnostico],
        };
    }

    private static ColaConsumidor Cola(IConfiguration configuration, string cola, ColaConsumidor porDefecto)
    {
        var concurrencia = configuration.GetValue($"{Seccion}:{cola}:Concurrencia", (int)porDefecto.Concurrencia);
        var prefetch = configuration.GetValue($"{Seccion}:{cola}:Prefetch", (int)porDefecto.Prefetch);
        if (concurrencia is < 1 or > ushort.MaxValue)
            throw Invalida($"{cola}:Concurrencia", "debe estar entre 1 y 65535", concurrencia);
        if (prefetch < concurrencia || prefetch > ushort.MaxValue)
            throw Invalida($"{cola}:Prefetch", $"debe estar entre la concurrencia ({concurrencia}) y 65535", prefetch);
        return new ColaConsumidor((ushort)concurrencia, (ushort)prefetch);
    }

    private static InvalidOperationException Invalida(string clave, string regla, int valor) => new(
        $"core-{ServicioSettings.Codigo} no puede arrancar: {$"{Seccion}:{clave}".Replace(":", "__", StringComparison.Ordinal)} {regla} (valor actual: {valor}).");
}
