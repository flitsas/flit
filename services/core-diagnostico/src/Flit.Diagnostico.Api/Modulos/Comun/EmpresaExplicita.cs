using Microsoft.Extensions.Primitives;

namespace Flit.Diagnostico.Api.Modulos.Comun;

/// <summary>
/// Empresa explícita de las rutas <c>/backoffice/*</c> (HU #13728, D-16, ADR-0073). La lectura entre compañías nunca toma
/// la empresa del token: la indica quien consulta, en <c>?empresa=</c> o en el segmento <c>{empresa}</c> de la ruta. Sin
/// ella, con un valor que no es una empresa, o con dos valores distintos, la ruta responde 400
/// <c>{ code: EMPRESA_REQUERIDA }</c> antes de leer nada y sin auditar. Con ella, el filtro registra la lectura en
/// <c>diagnostico.lecturas_backoffice</c> (<see cref="AuditoriaLecturaBackoffice"/>) antes de que el endpoint lea, así
/// que ninguna ruta del grupo puede olvidarse de auditar.
/// </summary>
public static class EmpresaExplicita
{
    public const string Parametro = "empresa";
    public const string CodigoRequerida = "EMPRESA_REQUERIDA";

    private const string ClaveItems = "diagnostico.backoffice.empresa";

    /// <summary>
    /// La empresa indicada: el valor de la ruta y el de la consulta (los que no estén vacíos) deben ser el mismo uuid
    /// distinto de cero. Si no hay ninguno, si alguno no es un uuid o si difieren, no hay empresa (null).
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

    /// <summary>Empresa que validó el filtro para esta petición. Solo existe dentro de un grupo con <see cref="ConEmpresaExplicita{TBuilder}"/>.</summary>
    public static Guid De(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Items.TryGetValue(ClaveItems, out var valor) && valor is Guid empresa
            ? empresa
            : throw new InvalidOperationException("La ruta no pasó por el filtro de empresa explícita (ConEmpresaExplicita).");
    }

    /// <summary>Exige la empresa explícita en cada endpoint del grupo y audita cada lectura (GET/HEAD).</summary>
    public static TBuilder ConEmpresaExplicita<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(new Filtro());

    private sealed class Filtro : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var http = context.HttpContext;
            StringValues consulta = http.Request.Query[Parametro];
            var empresa = consulta.Count > 1 ? null : Resolver(http.Request.RouteValues[Parametro]?.ToString(), consulta.ToString());
            if (empresa is not { } id)
            {
                return Results.Json(
                    new { code = CodigoRequerida, error = "Indica la empresa a consultar en el parámetro empresa (un único id de empresa)." },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            http.Items[ClaveItems] = id;
            if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
            {
                await http.RequestServices.GetRequiredService<AuditoriaLecturaBackoffice>()
                    .RegistrarAsync(id, http.Request, http.RequestAborted).ConfigureAwait(false);
            }

            return await next(context).ConfigureAwait(false);
        }
    }
}
