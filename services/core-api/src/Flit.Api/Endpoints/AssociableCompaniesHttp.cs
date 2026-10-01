using Flit.Admin.Domain.Companies.MandateSigners;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #13178 — forma única de la respuesta del directorio de compañías asociables (OT, compañía y cliente hijo).
/// Solo id, nombre y NIT por elemento (Ley 1581); una búsqueda de menos de 2 caracteres es 422.
/// </summary>
internal static class AssociableCompaniesHttp
{
    public static IResult ToResult(AssociableCompaniesResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.IsValid || result.Page is null)
        {
            return Results.Json(
                new { errors = new[] { new { field = "search", message = result.SearchError, value = (string?)null } } },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var page = result.Page;
        return Results.Ok(new
        {
            items = page.Items.Select(c => new { id = c.Id, name = c.Name, nit = c.Nit }),
            total = page.Total,
            page = page.Page,
            pageSize = page.PageSize,
            aplicaSoloASuCompania = page.AplicaSoloASuCompania,
        });
    }
}
