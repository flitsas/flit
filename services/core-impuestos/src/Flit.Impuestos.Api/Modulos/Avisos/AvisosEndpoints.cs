using Flit.Impuestos.Api.Modulos.Conciliacion;
using Flit.Impuestos.Api.Modulos.Resultados;
using Flit.Impuestos.Api.Plataforma;

namespace Flit.Impuestos.Api.Modulos.Avisos;

/// <summary>
/// <c>POST /api/v1/impuestos/avisos/{token}</c> (<c>recibirAvisoProveedor</c>, impuestos.v1.yaml): el webhook del proveedor.
/// Anónimo (lo autentica la firma), fuera del grupo autenticado y del guard de suscripción, con el límite
/// <see cref="LimitesSolicitudes.Avisos"/> (60 por minuto por IP real y un global aparte) y el cuerpo acotado a 256 KB. Ninguna
/// respuesta lleva cuerpo: 200, 401, 410 o 413. Un token sin forma responde 410 aquí mismo, sin consumir cupo. La lógica está en
/// <see cref="ProcesadorAviso"/>.
/// </summary>
/// <example>
/// <code>
/// AvisosEndpoints.Map(app, Program.Prefijo);
/// </code>
/// </example>
internal static class AvisosEndpoints
{
    /// <summary>Segmento bajo el prefijo; el guard de suscripción no lo mira.</summary>
    public const string Segmento = "/avisos";

    /// <summary>Largo del token del <c>callbackUrl</c>: 32 bytes en base64url sin relleno (<c>GeneradorTokenAviso</c>).</summary>
    public const int LargoToken = 43;

    /// <summary>
    /// El token tiene la forma de uno emitido (43 caracteres base64url). Uno sin forma no puede ser de nadie: 410 sin leer el cuerpo
    /// ni la base y sin consumir cupo del límite (IMP-3). Misma regla que aplica <see cref="ProcesadorAviso"/>.
    /// </summary>
    public static bool TokenConForma(string? token) =>
        token is { Length: LargoToken } && token.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');

    public static void Map(IEndpointRouteBuilder app, string prefijo)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapPost(prefijo + Segmento + "/{token}", RecibirAsync)
            .AllowAnonymous()
            .RequireRateLimiting(LimitesSolicitudes.Avisos)
            .CuerpoAcotado(CuerpoAcotadoFiltro.MaximoAviso)
            .WithName("RecibirAvisoProveedorImpuestos");
    }

    private static async Task<IResult> RecibirAsync(string token, HttpRequest request, ProcesadorAviso procesador, CancellationToken ct)
    {
        if (!TokenConForma(token))
            return Results.StatusCode(StatusCodes.Status410Gone);

        MemoryStream cuerpo;
        try
        {
            cuerpo = await CuerpoAcotadoFiltro.LeerAsync(request, CuerpoAcotadoFiltro.MaximoAviso, ct).ConfigureAwait(false);
        }
        catch (ReglaException ex) when (ex.Status == StatusCodes.Status413PayloadTooLarge)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        await using (cuerpo.ConfigureAwait(false))
        {
            var cabeceras = new CabecerasAviso(
                Cabecera(request, VerificadorFirma.CabeceraTimestamp),
                Cabecera(request, VerificadorFirma.CabeceraFirma),
                Cabecera(request, VerificadorFirma.CabeceraDelivery),
                Cabecera(request, VerificadorFirma.CabeceraEvento));
            return Results.StatusCode(await procesador.ProcesarAsync(token, cabeceras, cuerpo.ToArray(), ct).ConfigureAwait(false));
        }
    }

    private static string? Cabecera(HttpRequest request, string nombre) =>
        request.Headers.TryGetValue(nombre, out var v) && v.Count > 0 ? v[0] : null;
}

/// <summary>Registro de M10: receptor, cola, aplicación del resultado, conciliación y purga de los avisos recibidos.</summary>
public static class AvisosYConciliacionExtensions
{
    public static IServiceCollection AddAvisosYConciliacion(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var opciones = configuration.GetSection(OpcionesConciliacion.Seccion).Get<OpcionesConciliacion>() ?? new OpcionesConciliacion();
        opciones.Validar();
        services.AddSingleton(opciones);
        services.AddScoped<ProcesadorAviso>();
        services.AddScoped<AplicadorResultado>();
        services.AddScoped<LectorResultado>();
        services.AddScoped<CorrelacionIncierto>();
        services.AddSingleton<ColaAvisos>();
        services.AddHostedService(sp => sp.GetRequiredService<ColaAvisos>());
        services.AddSingleton<TrabajadorConciliacion>();
        if (opciones.TrabajadorActivo)
            services.AddHostedService(sp => sp.GetRequiredService<TrabajadorConciliacion>());
        // Security B-3: los avisos recibidos se purgan a los 30 días.
        services.AddSingleton<PurgaAvisosRecibidos>();
        if (configuration.GetValue(PurgaAvisosRecibidos.ClaveActiva, true))
            services.AddHostedService(sp => sp.GetRequiredService<PurgaAvisosRecibidos>());
        return services;
    }
}
