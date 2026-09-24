namespace Flit.Api.Endpoints;

/// <summary>
/// HU #12849 (Feature #12846 "Eliminar módulo de Preasignación de rango", Épica #12751) — la consola
/// OT de gestión de rangos de placa se retira por completo: listar/crear/editar rangos, listar placas,
/// compañías elegibles y bloquear/desbloquear/revocar placa de rango responden <c>410 Gone</c> (nunca
/// <c>404</c>: el recurso existió y se retiró a propósito) con el mismo sobre estándar de errores de la
/// casa que ya usan otras rutas retiradas (<c>errors: [{ field, code, message }]</c>, ver
/// <see cref="DeprecatedAdminIdentityEndpoints"/> / ADR-0050). No aplica a
/// <c>procedures/{instanceId}/assign-plate</c>, <c>release-plate</c>, <c>revoke</c> (alias) ni
/// <c>update-plate</c>: esas rutas pertenecen al ciclo de vida del trámite, no a la consola, y se
/// conservan sin cambios de contrato.
/// </summary>
internal static class DeprecatedAdminPlateRangesEndpoints
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
                        message = "Esta ruta se retiró: el módulo de Preasignación de rango dejó de "
                            + "operar (Épica #12751). Asignar una placa a un trámite siempre reserva "
                            + "fuera de rango, sin importar los rangos configurados antes.",
                    },
                },
            },
            statusCode: StatusCodes.Status410Gone);
}
