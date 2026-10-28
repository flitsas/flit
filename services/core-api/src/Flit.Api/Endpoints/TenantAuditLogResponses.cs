using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;

namespace Flit.Api.Endpoints;

/// <summary>
/// Ejecución común del historial de auditoría de un tenant (HU #14004) para las rutas de
/// compañía y de hijas de cabeza de red: 200 con la página o 422 si los filtros son inválidos.
/// </summary>
internal static class TenantAuditLogResponses
{
    internal sealed record ValidationErrorResponse(IReadOnlyList<CompanyValidationError> Errors);

    internal static async Task<IResult> ExecuteAsync(
        GetTenantAuditLogHandler handler,
        GetTenantAuditLogQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (InvalidAuditLogFilterException ex)
        {
            return Results.Json(
                new ValidationErrorResponse(ex.Errors),
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }
}
