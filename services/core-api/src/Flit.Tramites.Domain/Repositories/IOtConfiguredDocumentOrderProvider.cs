namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Puerto que entrega el orden documental que el Organismo de Tránsito configuró para un tipo de
/// trámite (HU #11184). Devuelve los <b>códigos del catálogo</b> en el orden guardado en
/// <c>admin.ot_document_precedence</c>, o <b>lista vacía</b> si ese OT no ha configurado nada.
/// <para>
/// Es deliberadamente distinto de <see cref="IResolvedChecklistMatrixProvider"/>: aquel resuelve la
/// matriz del <b>checklist</b> (qué documentos se piden y con qué obligatoriedad) y siempre
/// devuelve la matriz base del trámite; este solo responde cuando el OT tomó una decisión explícita
/// sobre el <b>orden del expediente</b>. La distinción es la que garantiza el respaldo: sin
/// configuración, el consolidado conserva el orden por modalidad de siempre.
/// </para>
/// <para>
/// La implementación (en Infraestructura) resuelve el tenant del OT desde su perfil, igual que el
/// resolutor de la matriz documental: el trámite vive en el tenant del cliente, pero la prelación
/// es del organismo.
/// </para>
/// </summary>
public interface IOtConfiguredDocumentOrderProvider
{
    /// <summary>
    /// Códigos de documento en el orden configurado por el OT, o vacío si no configuró ninguno
    /// (o si el trámite no tiene organismo asignado todavía).
    /// </summary>
    Task<IReadOnlyList<string>> GetConfiguredOrderAsync(
        Guid procedureTypeId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);
}
