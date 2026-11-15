using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Plataforma;
using Grpc.Core;
using Microsoft.Extensions.Primitives;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// <c>?empresa=</c> del selector del backoffice: solo compañías con Diagnóstico activo, validado en el servidor (HU #14579, BE-0, SP-3,
/// ADR-0088). <see cref="EmpresaExplicita.Resolver"/> acepta cualquier uuid distinto de cero y la suscripción solo mira la compañía
/// del token; este filtro corre <b>antes</b> de <c>ConEmpresaExplicita</c> en las rutas de estadísticas y reportes del backoffice
/// (E3, B4–B6), así que ni lee ni audita una compañía apagada:
/// <list type="bullet">
/// <item>Con Identidad configurada, la empresa debe estar en la lista de <see cref="IEmpresasPlataforma"/> (la misma del selector,
/// <c>GET /backoffice/empresas</c>). Si Identidad no responde: 503 <c>IDENTIDAD_NO_DISPONIBLE</c> con <c>Retry-After: 30</c>, nunca
/// «leer igual».</item>
/// <item>Sin Identidad configurada (local), decide <see cref="IProductosHabilitados"/> (fail-closed).</item>
/// <item>No habilitada o inexistente: 404 <see cref="CodigoNoEncontrada"/> con el mismo cuerpo en los dos casos.</item>
/// <item>Sin empresa resoluble (ausente, no uuid o repetida) no responde: deja que <c>ConEmpresaExplicita</c> dé su 400
/// <c>EMPRESA_REQUERIDA</c>, sin preguntar a Identidad.</item>
/// </list>
/// Uso: <c>api.MapGroup(BackofficeEndpoints.Grupo).RequireAuthorization(PoliticaBackoffice.Nombre).SoloEmpresaHabilitada().ConEmpresaExplicita()</c>
/// (en el mismo grupo y en ese orden: los filtros corren en el orden en que se agregan).
/// </summary>
public static partial class EmpresaHabilitada
{
    public const string CodigoNoEncontrada = "EMPRESA_NO_ENCONTRADA";

    private const string ClaveItems = "diagnostico.backoffice.empresa_habilitada";
    private const string Mensaje = "La compañía indicada no existe o no tiene Diagnóstico activo.";

    /// <summary>Agrega el filtro al grupo o a la ruta; debe ir antes de <c>ConEmpresaExplicita</c>.</summary>
    public static TBuilder SoloEmpresaHabilitada<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new Filtro());

    /// <summary>Nombre de la empresa validada según Identidad (para <c>ambito.empresa_nombre</c> de E3); null sin Identidad configurada.</summary>
    public static string? Nombre(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Items.TryGetValue(ClaveItems, out var valor) && valor is Validada validada
            ? validada.Nombre
            : throw new InvalidOperationException("La ruta no pasó por el filtro de empresa habilitada (SoloEmpresaHabilitada).");
    }

    private sealed record Validada(Guid Id, string? Nombre);

    private sealed class Filtro : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            StringValues consulta = http.Request.Query[EmpresaExplicita.Parametro];
            var empresa = consulta.Count > 1 ? null : EmpresaExplicita.Resolver(http.Request.RouteValues[EmpresaExplicita.Parametro]?.ToString(), consulta.ToString());
            if (empresa is not { } id)
                return await next(context).ConfigureAwait(false);

            var validada = await ValidarAsync(http, id, http.RequestAborted).ConfigureAwait(false);
            if (validada is null)
                return Results.Json(new { code = CodigoNoEncontrada, error = Mensaje }, statusCode: StatusCodes.Status404NotFound);

            http.Items[ClaveItems] = validada;
            return await next(context).ConfigureAwait(false);
        }
    }

    private static async Task<Validada?> ValidarAsync(HttpContext http, Guid empresa, CancellationToken ct)
    {
        var servicios = http.RequestServices;
        IReadOnlyList<EmpresaPlataforma>? lista;
        try
        {
            lista = await servicios.GetRequiredService<IEmpresasPlataforma>().ListarAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (EmpresasBackofficeEndpoints.EsFallaDeIdentidad(ex) && !ct.IsCancellationRequested)
        {
            // Solo el motivo técnico al log (SP-9): ni la empresa pedida ni nombres o NIT de la lista.
            LogNoDisponible(servicios.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(EmpresaHabilitada)),
                ex is RpcException rpc ? rpc.StatusCode.ToString() : ex.GetType().Name);
            throw new ReglaException(EmpresasBackofficeEndpoints.CodigoNoDisponible,
                "No se pudo verificar la compañía con Identidad. Intenta de nuevo en unos segundos.", StatusCodes.Status503ServiceUnavailable)
            { RetryAfterSegundos = 30 };
        }

        if (lista is not null)
            return lista.FirstOrDefault(e => e.Id == empresa) is { } encontrada ? new Validada(encontrada.Id, encontrada.Nombre) : null;

        // Sin Identidad configurada no hay lista: decide la suscripción, que falla cerrada.
        return await servicios.GetRequiredService<IProductosHabilitados>().DiagnosticoHabilitadoAsync(empresa, ct).ConfigureAwait(false)
            ? new Validada(empresa, null)
            : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo verificar con Identidad la compañía pedida al backoffice ({Motivo}): la ruta responde 503.")]
    private static partial void LogNoDisponible(ILogger logger, string motivo);
}
