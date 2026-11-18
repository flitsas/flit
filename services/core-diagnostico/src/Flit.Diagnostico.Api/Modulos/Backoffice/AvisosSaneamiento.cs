using Flit.Diagnostico.Api.Modulos.Notificaciones;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Avisos de la campana al solicitante por las acciones de FLIT (plan §4.5, ADR-0087 D-8; HU #14576 AC48–AC50; SP-22, SP-23). Por
/// cada acción, un aviso por par (empresa, solicitante, diagnóstico, tipo y resultado) con <c>{"cantidad": n}</c> y, al cerrar,
/// <c>"resultado"</c>: nada de placa, motivo ni texto. <c>referencia_id</c> = el evento de menor id del grupo (único por acción, así
/// que reabrir o tomar otra vez el mismo lote no choca con <c>ux_notificaciones_idempotencia</c>). La comprobación de idempotencia
/// mira las notificaciones de la empresa del aviso (<see cref="LecturaEntreEmpresas.DeLaEmpresa{T}"/>), no las del token de FLIT.
/// No guarda: va en la unidad de trabajo de la transición. Sin correo ni avisos a FLIT (S19, S19-bis).
/// </summary>
internal static class AvisosSaneamiento
{
    public static async Task AgregarAsync(DiagnosticoDb db, IReadOnlyList<TransicionAplicada> aplicadas, DateTimeOffset ahora, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(aplicadas);
        var grupos = aplicadas
            .Where(a => a.TipoAviso is not null)
            .GroupBy(a => (a.Saneamiento.TenantId, a.Saneamiento.SolicitadoPor, a.Saneamiento.DiagnosticoId, Tipo: a.TipoAviso!, Resultado: Resultado(a.Evento.Evento)));
        foreach (var grupo in grupos)
        {
            var (tenant, solicitante, diagnostico, tipo, resultado) = grupo.Key;
            var referencia = grupo.Min(a => a.Evento.Id);
            await EscritorNotificaciones.AgregarAsync(
                db, tenant, solicitante, tipo, diagnostico, referencia, ahora, ct, Datos(grupo.Count(), resultado), db.DeLaEmpresa<Notificacion>(tenant))
                .ConfigureAwait(false);
        }
    }

    /// <summary>JSON plano que cumple <c>ck_notificaciones_datos</c>.</summary>
    internal static string Datos(int cantidad, string? resultado) =>
        resultado is null ? $$"""{"cantidad":{{cantidad}}}""" : $$"""{"cantidad":{{cantidad}},"resultado":"{{resultado}}"}""";

    private static string? Resultado(TipoEventoSaneamiento evento) => evento switch
    {
        TipoEventoSaneamiento.CerradoSaneado => "SANEADO",
        TipoEventoSaneamiento.CerradoNoProcede => "NO_PROCEDE",
        _ => null,
    };
}
