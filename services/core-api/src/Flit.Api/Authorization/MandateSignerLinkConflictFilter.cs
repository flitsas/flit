using Flit.Admin.Domain.Companies.MandateSigners;
using Microsoft.AspNetCore.Http;

namespace Flit.Api.Authorization;

/// <summary>
/// HU #13195 (ADR-0066 D1, AC3) — traduce a <b>409 Conflict</b> el rechazo del índice único
/// <c>uq_mandate_signer_companies_one_per_origin</c> (un segundo vínculo activo para la misma compañía,
/// organismo y grupo de origen). Se aplica a los grupos de rutas que crean, editan o reactivan mandatarios,
/// para que ninguna ruta de escritura quede con un 500. El cuerpo no lleva datos personales.
/// </summary>
public sealed class MandateSignerLinkConflictFilter : IEndpointFilter
{
    public const string ErrorCode = "mandatario_activo_existente";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            return await next(context).ConfigureAwait(false);
        }
        catch (MandateSignerActiveLinkConflictException ex)
        {
            return Results.Json(
                new { code = ErrorCode, error = ex.Message },
                statusCode: StatusCodes.Status409Conflict);
        }
    }
}
