using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Guarda compartida de las ramas «generar-o-limpiar» de <c>GenerarFurHandler</c> (DT-3, HU #11316,
/// Feature #11309): solo se retira lo que <b>GENERÓ EL SISTEMA</b> (<c>Source == "system"</c>). Hoy
/// esas ramas borran por <c>Tipo</c> sin mirar el origen, así que un documento cargado por el usuario,
/// el organismo, o <b>personalizado por la compañía</b> (<c>Source == "company"</c>, HU #11313) queda
/// destruido junto con su archivo — sin poder recuperarse.
///
/// <para>Esta HU la aplica solo a la rama de <c>mandato</c> (el único tipo cuya limpieza puede hoy
/// destruir un documento personalizado). El <b>Bug #11310</b> (preexistente, fuera de esta HU) extiende
/// la misma guarda a las otras cinco ramas (<c>certificado_identidad*</c>, <c>certificado_rues*</c>,
/// <c>certificado_rnmc</c>, <c>certificado_soat_rtm</c> y escrituras), con sus propias pruebas de
/// regresión por tipo.</para>
/// </summary>
public static class AttachmentCleanup
{
    /// <summary>¿Este adjunto lo generó el sistema? Único origen candidato a retirarse por esta guarda.</summary>
    public static bool EsGeneradoPorElSistema(ProcedureInstanceAttachment attachment) =>
        string.Equals(attachment.Source, "system", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Retira (storage + colección en memoria + repo) los adjuntos de <paramref name="instance"/> que
    /// cumplen <paramref name="predicadoDeTipo"/> Y fueron generados por el sistema. Cualquier adjunto de
    /// otro origen (<c>user</c>/<c>ot</c>/<c>ict</c>/<c>company</c>/<c>consultation</c>/<c>portal</c>/
    /// <c>ocr</c>) sobrevive intacto, con su archivo en storage.
    /// </summary>
    public static void RetirarGenerados(
        ProcedureInstance instance,
        IProcedureInstanceRepository repo,
        IAttachmentStorage storage,
        Func<ProcedureInstanceAttachment, bool> predicadoDeTipo)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(predicadoDeTipo);

        foreach (var prev in instance.Attachments
                     .Where(a => predicadoDeTipo(a) && EsGeneradoPorElSistema(a))
                     .ToList())
        {
            storage.Delete(prev.StoragePath);
            instance.Attachments.Remove(prev);
            repo.RemoveAttachment(prev);
        }
    }
}
