namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// HU #12853 (Feature #12846 "Eliminar módulo de Preasignación de rango", Épica #12751) — la ruta de
/// placa preasignada de la compañía (<c>/plate-preassign/available</c>, Feature #10587 P-10, y
/// <c>/plate-preassign/status</c>, HU #10806 AC3) se apaga: el wizard de matrícula inicial ya no puede
/// ofrecer un selector de placas de rango, porque la asignación de placa de cualquier trámite siempre
/// reserva fuera de rango desde HU #12849. Responde <c>410 Gone</c> (nunca <c>404</c>: el recurso
/// existió y se retiró a propósito) con el mismo sobre estándar de errores de la casa que otras rutas
/// retiradas (<c>errors: [{ field, code, message }]</c>, ver
/// <see cref="Flit.Api.Endpoints.DeprecatedAdminIdentityEndpoints"/> / ADR-0050).
/// </summary>
internal static class DeprecatedPlatePreassignEndpoints
{
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
                        message = "Esta ruta se retiró: la preasignación de placa dejó de operar "
                            + "(Épica #12751). La asignación de placa de un trámite siempre reserva "
                            + "fuera de rango.",
                    },
                },
            },
            statusCode: StatusCodes.Status410Gone);
}
