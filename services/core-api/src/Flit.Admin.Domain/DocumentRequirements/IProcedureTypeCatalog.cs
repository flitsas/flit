namespace Flit.Admin.Domain.DocumentRequirements;

/// <summary>
/// Consulta de solo lectura sobre el catálogo de tipos de trámite
/// (<c>tramites.procedure_types</c>, Feature #10116). HU #10195 únicamente necesita
/// comprobar la existencia de un trámite al asociar documentos; el CRUD de tipos de
/// trámite pertenece a otra HU.
/// </summary>
public interface IProcedureTypeCatalog
{
    /// <summary>True si existe un tipo de trámite con el id indicado.</summary>
    Task<bool> ExistsAsync(
        Guid procedureTypeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tipos de trámite asignables a un representante/gestor: solo los <b>activos y publicados</b>
    /// (los "habilitados" en el módulo de trámites). Devuelve id + código + nombre para que el
    /// selector del admin trabaje con los IDs reales del catálogo. Los seeds generan los ids con
    /// <c>uuidv7()</c> (no deterministas por BD/entorno), por lo que el frontend NO puede fijar ids
    /// hardcodeados: debe consumir esta lista (ADR-0033, corrección del error <c>tipo_tramite_inexistente</c>).
    /// </summary>
    Task<IReadOnlyList<ProcedureTypeCatalogItem>> ListActivePublishedAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lectura puntual por id (incluye inactivos). Usada por el banco de pruebas de notificaciones
    /// para interpolar <c>name</c> sin tocar instancias de trámite.
    /// </summary>
    Task<ProcedureTypeNotificationPreviewItem?> GetByIdForNotificationPreviewAsync(
        Guid procedureTypeId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ProcedureTypeNotificationPreviewItem?>(null);
}

/// <summary>Tipo de trámite para overlay de muestra de correo (banco de pruebas).</summary>
public sealed record ProcedureTypeNotificationPreviewItem(
    Guid Id,
    string Name,
    string Family,
    bool IsActive);

/// <summary>Ítem del catálogo de tipos de trámite para selección en el admin (HU #10901/#10904).</summary>
public sealed record ProcedureTypeCatalogItem(Guid Id, string Code, string Name);
