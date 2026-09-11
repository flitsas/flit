using Flit.Tramites.Application.BulkTramites;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Endpoints de carga masiva de trámites (HU #12520, Feature #12519). Exigen usuario autenticado
/// (cualquier JWT válido), <b>no</b> SuperAdmin ni un rol específico: el PO confirmó que si el
/// usuario puede radicar trámites, ve también la carga masiva — sin distinción por tenant.
/// </summary>
public static class BulkTramitesEndpoints
{
    public static IEndpointRouteBuilder MapBulkTramitesEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/tramites/carga-masiva")
            .RequireAuthorization();

        // GET /plantilla?tipo=matricula|traspaso|otros — AC1/AC3 → 200 (xlsx) / 400.
        group.MapGet("/plantilla", DescargarPlantillaAsync).WithName("BulkTramitesDescargarPlantilla");

        return app;
    }

    private static async Task<IResult> DescargarPlantillaAsync(
        [FromQuery] string? tipo,
        [FromServices] IBulkTramitesXlsxTemplate template,
        CancellationToken cancellationToken)
    {
        var tipoResuelto = BulkTramitesTemplateTypeParser.Parse(tipo);
        if (tipoResuelto is null)
        {
            return Results.Json(
                new ErrorResponse(
                    $"El tipo de plantilla '{tipo}' no está disponible para carga masiva. "
                    + "Usa 'matricula', 'traspaso' u 'otros'."),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var archivo = await template.BuildAsync(tipoResuelto.Value, cancellationToken).ConfigureAwait(false);
        return Results.File(archivo.Content, archivo.Mimetype, archivo.Filename);
    }

    private sealed record ErrorResponse(string Error);
}
