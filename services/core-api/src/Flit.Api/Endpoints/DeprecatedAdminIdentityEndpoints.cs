namespace Flit.Api.Endpoints;

/// <summary>
/// HU #11758 (ADR-0050) — respuesta común de las diez rutas de disparo/vinculación de identidad
/// administrativa que se retiran: representante legal (send/resend/link), mandatario por OT
/// (send/resend/link/mock) y mandatario por compañía (send/resend/link). Todas responden
/// <c>410 Gone</c> (nunca <c>404</c>: el recurso existió y se retiró a propósito, decisión DA-1) con
/// el sobre estándar de errores de la casa (<c>errors: [{ field, code, message }]</c>).
/// </summary>
internal static class DeprecatedAdminIdentityEndpoints
{
    /// <summary>
    /// Para rutas bajo <c>/companies/{tenantId}/...</c>: RECIBE <c>tenantId</c> (en vez de
    /// ignorarlo) para que <see cref="CompanyOwnTenantFilter"/> siga viendo el argumento y
    /// aplicando su chequeo de tenant propio antes de responder 410 — un AdminCompany de OTRO
    /// tenant sigue recibiendo 403, no 410, aunque la ruta ya no haga nada con el tenant.
    /// </summary>
    public static IResult GoneForTenant(Guid tenantId) => Gone();

    public static IResult Gone() =>
        Results.Json(
            new
            {
                errors = new[]
                {
                    new
                    {
                        field = (string?)null,
                        code = "endpoint_deprecado",
                        message = "Esta ruta se retiró: el módulo Identidad es la única fuente que "
                            + "puede originar una validación de identidad (ADR-0050).",
                    },
                },
            },
            statusCode: StatusCodes.Status410Gone);
}
