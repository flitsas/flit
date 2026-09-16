using Flit.Admin.Domain.OtMetrics;
using Flit.Tramites.Domain.Tramites.Estados;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Estado del trámite LEÍDO DESDE EL ORGANISMO.
///
/// <para>Vive suelto porque lo usan el informe del periodo y el motor de consultas, y dos copias de
/// esta traducción harían que los dos dijeran cosas distintas del mismo trámite. Un reporte que se
/// contradice con otro no es un reporte a medias: es un reporte que ya no se puede usar para
/// discutir con nadie.</para>
///
/// <para>Los valores son excluyentes y exhaustivos: cada trámite cae en exactamente uno, y por eso
/// el desglose del informe suma el total.</para>
/// </summary>
internal static class OtEstadoResolver
{
    public static string Resolve(
        string status,
        bool subsanacionActiva,
        bool isPaused) => status switch
    {
        TramiteEstado.Aprobado => OtReportEstado.Aprobado,
        TramiteEstado.Anulado => OtReportEstado.Anulado,
        // Un rechazo con subsanación abierta vuelve; uno sin ella se quedó ahí. Para el organismo
        // son dos cosas distintas y contarlas juntas escondería cuánto trabajo tiene de vuelta.
        TramiteEstado.Rechazado => subsanacionActiva
            ? OtReportEstado.EnSubsanacion
            : OtReportEstado.Rechazado,
        // ADR-0059 — la ruta de placa son estados reales: preasignacion = el organismo debe poner
        // placa; asignado = la pelota está en el cliente (SOAT, impuestos, enviar al OT); entregado =
        // en revisión del organismo. Un trámite pausado espera al cliente, esté donde esté.
        TramiteEstado.Preasignacion or TramiteEstado.Asignado or TramiteEstado.Entregado when isPaused
            => OtReportEstado.EsperandoCliente,
        TramiteEstado.Preasignacion => OtReportEstado.EsperandoPlaca,
        TramiteEstado.Asignado => OtReportEstado.EsperandoCliente,
        TramiteEstado.Entregado => OtReportEstado.EnRevision,
        _ => OtReportEstado.Otro,
    };
}
