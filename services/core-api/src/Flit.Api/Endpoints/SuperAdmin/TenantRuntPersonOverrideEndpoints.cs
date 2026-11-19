using System.Security.Claims;
using Flit.Admin.Application.Companies.RuntPersonOverrides;

namespace Flit.Api.Endpoints.SuperAdmin;

/// <summary>
/// CRUD de overrides manuales de persona RUNT por tenant (HU #14642).
/// Solo SuperAdmin. Los overrides cortocircuitan la cadena remota Kyverum/Verifik
/// cuando el RUNT no tiene el documento o lo tiene mal.
/// </summary>
internal static class TenantRuntPersonOverrideEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        // ── GET /superadmin/companies/{tenantId}/runt-person-overrides ──────
        group.MapGet("/companies/{tenantId:guid}/runt-person-overrides", async (
            Guid tenantId,
            string? documentType,
            ListTenantRuntPersonOverridesHandler handler,
            CancellationToken ct) =>
        {
            var list = await handler.HandleAsync(tenantId, documentType, ct).ConfigureAwait(false);
            return Results.Ok(list);
        }).WithName("ListTenantRuntPersonOverrides");

        // ── POST /superadmin/companies/{tenantId}/runt-person-overrides ─────
        group.MapPost("/companies/{tenantId:guid}/runt-person-overrides", async (
            Guid tenantId,
            CreateTenantRuntPersonOverrideHttpRequest body,
            CreateTenantRuntPersonOverrideHandler handler,
            ClaimsPrincipal caller,
            CancellationToken ct) =>
        {
            Guid.TryParse(
                caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub"),
                out var callerId);

            var request = new CreateTenantRuntPersonOverrideRequest(
                TenantId: tenantId,
                DocumentType: body.DocumentType,
                DocumentNumber: body.DocumentNumber,
                FirstName: body.FirstName,
                SecondName: body.SecondName,
                FirstLastName: body.FirstLastName,
                SecondLastName: body.SecondLastName,
                CitizenStatus: body.CitizenStatus ?? "ACTIVA",
                DriverStatus: body.DriverStatus ?? "ACTIVO",
                HasActiveLicense: body.HasActiveLicense ?? true,
                LicenseCategories: body.LicenseCategories,
                HasPendingFines: body.HasPendingFines ?? false,
                NroPazYSalvo: body.NroPazYSalvo,
                Reason: body.Reason,
                ExpiresAt: body.ExpiresAt,
                RequestedBy: callerId == Guid.Empty ? null : callerId);

            var result = await handler.HandleAsync(request, ct).ConfigureAwait(false);

            return result.Success
                ? Results.Created($"/api/v1/superadmin/companies/{tenantId}/runt-person-overrides/{result.Override!.Id}", result.Override)
                : Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_ERROR", result.Errors);
        }).WithName("CreateTenantRuntPersonOverride");

        // ── PATCH /superadmin/companies/{tenantId}/runt-person-overrides/{id} ─
        group.MapPatch("/companies/{tenantId:guid}/runt-person-overrides/{id:guid}", async (
            Guid tenantId,
            Guid id,
            UpdateTenantRuntPersonOverrideHttpRequest body,
            UpdateTenantRuntPersonOverrideHandler handler,
            ClaimsPrincipal caller,
            CancellationToken ct) =>
        {
            Guid.TryParse(
                caller.FindFirstValue(ClaimTypes.NameIdentifier) ?? caller.FindFirstValue("sub"),
                out var callerId);

            // tenantId en la ruta es informativo para el routing; la validación de pertenencia es
            // responsabilidad del repositorio (el id es global único, el tenantId en la ruta se
            // documenta para consistencia de la API).
            var request = new UpdateTenantRuntPersonOverrideRequest(
                FirstName: body.FirstName,
                SecondName: body.SecondName,
                FirstLastName: body.FirstLastName,
                SecondLastName: body.SecondLastName,
                CitizenStatus: body.CitizenStatus,
                DriverStatus: body.DriverStatus,
                HasActiveLicense: body.HasActiveLicense,
                LicenseCategories: body.LicenseCategories,
                HasPendingFines: body.HasPendingFines,
                NroPazYSalvo: body.NroPazYSalvo,
                Reason: body.Reason,
                IsActive: body.IsActive,
                ExpiresAt: body.ExpiresAt,
                RequestedBy: callerId == Guid.Empty ? null : callerId);

            var result = await handler.HandleAsync(id, request, ct).ConfigureAwait(false);

            if (!result.Success && result.Errors.Any(e => e.Field == "id"))
                return Results.NotFound();

            return result.Success
                ? Results.Ok(result.Override)
                : Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_ERROR", result.Errors);
        }).WithName("UpdateTenantRuntPersonOverride");
    }

    private static IResult Problem(
        int status,
        string code,
        IReadOnlyList<TenantRuntPersonOverrideValidationError> errors) =>
        Results.Problem(
            statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["errors"] = errors,
            });
}

/// <summary>Cuerpo del POST de creación de override de persona RUNT.</summary>
internal sealed record CreateTenantRuntPersonOverrideHttpRequest(
    string DocumentType,
    string DocumentNumber,
    string FirstName,
    string? SecondName,
    string FirstLastName,
    string? SecondLastName,
    string? CitizenStatus,
    string? DriverStatus,
    bool? HasActiveLicense,
    string? LicenseCategories,
    bool? HasPendingFines,
    string? NroPazYSalvo,
    string Reason,
    DateTimeOffset? ExpiresAt);

/// <summary>Cuerpo del PATCH de actualización de override de persona RUNT.</summary>
internal sealed record UpdateTenantRuntPersonOverrideHttpRequest(
    string? FirstName,
    string? SecondName,
    string? FirstLastName,
    string? SecondLastName,
    string? CitizenStatus,
    string? DriverStatus,
    bool? HasActiveLicense,
    string? LicenseCategories,
    bool? HasPendingFines,
    string? NroPazYSalvo,
    string? Reason,
    bool? IsActive,
    DateTimeOffset? ExpiresAt);
