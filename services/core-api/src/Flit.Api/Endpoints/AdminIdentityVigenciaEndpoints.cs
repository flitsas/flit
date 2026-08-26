using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.Persons;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// HU #11751 (ADR-0050) — consulta admin de VIGENCIA de identidad por documento, leyendo
/// EXCLUSIVAMENTE del almacén de Identidad (<c>tramites.procedure_instance_biometric_validations</c>).
/// Devuelve UN único registro (no una grilla: no reutiliza el payload de
/// <c>/api/v1/biometric-validations/by-person</c>). El tenant se DERIVA de la ruta admin
/// (<c>{tenantId}</c> + <see cref="CompanyOwnTenantFilter"/>): este endpoint no exige ni lee
/// <c>X-Tenant-Id</c>.
///
/// <para><b>Sin RLS especial.</b> A diferencia de <c>MandateSignerDirectory</c> al leer
/// <c>admin.admin_identity_validations</c> (que sí necesita <c>SET LOCAL row_security = off</c> porque
/// esa tabla tiene RLS anclada al tenant del OT y el directorio corre en el tenant GESTORA), esta
/// consulta reutiliza <see cref="IdentityVigenciaPorDocumentoResolver"/> tal cual lo usa el resto del
/// módulo Identidad: el aislamiento real de <c>ProcedureInstanceRepository</c> es el <c>WHERE
/// tenant_id</c> explícito de cada consulta (la política <c>tenant_isolation</c> de la tabla no lleva
/// <c>FORCE ROW LEVEL SECURITY</c> y la conexión de la aplicación es la propietaria de la tabla, así que
/// Postgres la exime de RLS igual que a cualquier otro lector del módulo — ver memoria "RLS decorativo").
/// El <paramref name="tenantId"/> de la ruta ya viaja explícito en cada llamada al repositorio; no hace
/// falta fijar ninguna variable de sesión.</para>
/// </summary>
public static class AdminIdentityVigenciaEndpoints
{
    public static IEndpointRouteBuilder MapAdminIdentityVigenciaEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{tenantId:guid}/identity-validations")
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .AddEndpointFilter<CompanyOwnTenantFilter>()
            .WithTags("Admin · Identidad");

        // GET /api/v1/admin/companies/{tenantId}/identity-validations/by-document?documentType=&documentNumber=
        group.MapGet("/by-document", async (
            Guid tenantId,
            [FromQuery] string? documentType,
            [FromQuery] string? documentNumber,
            GetIdentityVigenciaPorDocumentoHandler handler,
            CancellationToken ct) =>
        {
            var (result, error) = await handler.HandleAsync(tenantId, documentType, documentNumber, ct)
                .ConfigureAwait(false);

            return error switch
            {
                "documento_requerido" => Results.Json(
                    new
                    {
                        errors = new[]
                        {
                            new
                            {
                                field = "documentNumber",
                                code = "documento_requerido",
                                message = "documentType y documentNumber son obligatorios.",
                            },
                        },
                    },
                    statusCode: StatusCodes.Status400BadRequest),
                _ => Results.Ok(result),
            };
        })
        .WithName("AdminIdentityVigenciaByDocument")
        .WithSummary("Vigencia de identidad de una persona por documento (fuente única, ADR-0050)")
        .WithDescription("Resuelve el estado de identidad (sin_validacion|en_curso|aprobada_vigente|"
            + "vencida) de una persona por tipo+número de documento, leyendo del módulo Identidad. "
            + "Un único registro, no un listado. Requiere AdminCompany (acotado a su tenant) o SuperAdmin.")
        .Produces<IdentityVigenciaPorDocumentoResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        return app;
    }
}
