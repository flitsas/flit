using Flit.Impuestos.Api.Plataforma;
using Microsoft.Extensions.Primitives;

namespace Flit.Impuestos.Api.Modulos.Comun;

/// <summary>
/// Empresa explícita de las rutas que el Super Admin usa sobre otra compañía (plan §5.1: <c>?empresa=&lt;tenantId&gt;</c> o el
/// segmento <c>{tenantId}</c> de <c>/backoffice/empresas/{tenantId}/…</c>). La lectura entre compañías nunca toma la empresa del
/// token. Sin ella, con un valor que no es un uuid o con dos valores distintos, la ruta responde 400
/// <c>{ code: EMPRESA_REQUERIDA }</c> antes de leer nada. Solo un Super Admin puede indicarla: otro usuario recibe 403. Con ella,
/// el filtro registra la lectura (<see cref="AuditoriaLecturaBackoffice"/>) antes de que el endpoint lea.
/// </summary>
/// <example>
/// <code>
/// backoffice.MapGet("/empresas/{tenantId}/aprovisionamiento", Leer).ConEmpresaExplicita("aprovisionamiento");
/// var empresa = EmpresaExplicita.De(http);
/// </code>
/// </example>
public static class EmpresaExplicita
{
    public const string Parametro = "empresa";
    public const string ParametroRuta = "tenantId";
    public const string CodigoRequerida = "EMPRESA_REQUERIDA";
    public const string CodigoSoloSuperAdmin = "EMPRESA_SOLO_SUPER_ADMIN";

    private const string ClaveItems = "impuestos.backoffice.empresa";

    /// <summary>
    /// La empresa indicada: el valor de la ruta y el de la consulta (los que no estén vacíos) deben ser el mismo uuid distinto de
    /// cero. Si no hay ninguno, si alguno no es un uuid o si difieren, no hay empresa (null).
    /// </summary>
    public static Guid? Resolver(string? deLaRuta, string? deLaConsulta)
    {
        Guid? empresa = null;
        foreach (var valor in new[] { deLaRuta, deLaConsulta }.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            if (!Guid.TryParse(valor, out var id) || id == Guid.Empty)
                return null;
            if (empresa is { } previa && previa != id)
                return null;
            empresa = id;
        }

        return empresa;
    }

    /// <summary>Empresa que validó el filtro para esta petición.</summary>
    public static Guid De(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Items.TryGetValue(ClaveItems, out var valor) && valor is Guid empresa
            ? empresa
            : throw new InvalidOperationException("La ruta no pasó por el filtro de empresa explícita (ConEmpresaExplicita).");
    }

    /// <summary>Exige la empresa explícita y audita cada lectura (GET/HEAD) con el <paramref name="recurso"/> dado.</summary>
    public static TBuilder ConEmpresaExplicita<TBuilder>(this TBuilder builder, string recurso)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recurso);
        return builder.AddEndpointFilter(new Filtro(recurso));
    }

    private sealed class Filtro(string recurso) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            StringValues consulta = http.Request.Query[Parametro];
            var empresa = consulta.Count > 1 ? null : Resolver(http.Request.RouteValues[ParametroRuta]?.ToString(), consulta.ToString());
            if (empresa is not { } id)
            {
                return Results.Json(
                    new { code = CodigoRequerida, error = "Indica la empresa a consultar (un único id de empresa)." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!http.RequestServices.GetRequiredService<UsuarioActual>().EsSuperAdmin)
                throw ReglaException.Prohibido(CodigoSoloSuperAdmin, "Solo el Super Admin consulta otras compañías.");

            http.Items[ClaveItems] = id;
            if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
            {
                await http.RequestServices.GetRequiredService<AuditoriaLecturaBackoffice>()
                    .RegistrarAsync(id, recurso, http.RequestAborted).ConfigureAwait(false);
            }

            return await next(context).ConfigureAwait(false);
        }
    }
}
