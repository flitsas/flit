namespace Flit.Tramites.Domain.Repositories;

/// <summary>
/// Documento de la matriz documental resuelta del gestor (HU #10522, RF17/RF22), en la forma
/// mínima que consume el checklist: código (= <c>document_types.code</c>, que empareja con el
/// <c>DocTipo</c> del ítem), nombre visible, obligatoriedad y orden ya resuelto por precedencia
/// OT &gt; Default.
/// </summary>
public sealed record ResolvedChecklistDoc(
    string Codigo,
    string Nombre,
    bool Obligatorio,
    short Orden,
    /// <summary>Buzón dummy (CFD-06): se lista en el checklist pero no bloquea el avance.</summary>
    bool EsDummy = false);

/// <summary>
/// Puerto que provee la matriz documental resuelta del gestor para un trámite (HU #10522,
/// RF17/RF22 — decisión LT: <b>matriz viva</b>, no snapshot). Desacopla el motor de trámites del
/// submódulo Admin: la implementación (en Infraestructura) puentea a
/// <c>IResolvedDocumentMatrixResolver</c> y mapea sus filas a este VO de dominio.
/// <para>
/// Devuelve <b>lista vacía</b> cuando el gestor no tiene documentos configurados para el trámite
/// (sin matriz) ⇒ el checklist cae al catálogo plano actual (sin regresión).
/// </para>
/// </summary>
public interface IResolvedChecklistMatrixProvider
{
    /// <summary>
    /// Matriz resuelta del trámite (posiblemente vacía). <paramref name="transitOfficeId"/> es
    /// opcional: sin OT se usa el orden/obligatoriedad base del trámite.
    /// </summary>
    Task<IReadOnlyList<ResolvedChecklistDoc>> GetForAsync(
        Guid procedureTypeId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);
}
