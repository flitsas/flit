using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// HU #12410 (Feature #12257, épica #12235) — política de los documentos de la red
/// (<c>/api/v1/tramites/network/instances/{id}/attachments*</c>): además de la policy de cabeza
/// (<see cref="NetworkScopePolicy"/>), la CLASE de la cabeza decide. MARCA_BLANCA siempre puede;
/// CONCESION solo con el interruptor <see cref="IHierarchySwitches.NetworkDocumentsConcesionKey"/>
/// encendido (apagado por defecto: pendiente 13 del PO, bloqueo D1 Ley 1581). Regla pura sobre el
/// alcance + el estado del interruptor, leído por petición.
/// </summary>
public static class NetworkDocumentsPolicy
{
    /// <summary>403 — cabeza CONCESION con el interruptor de documentos de red apagado.</summary>
    public const string DocumentsDisabled = "network_documents_disabled";

    /// <summary>404 escueto — trámite/documento inexistente, ajeno o fuera del alcance (no se distingue).</summary>
    public const string NotFound = "not_found";

    /// <summary>
    /// <c>null</c> si la clase de la cabeza puede leer documentos de la red; si no, el código de error.
    /// <paramref name="scope"/> debe ser ya un grupo válido (<see cref="NetworkScopePolicy.Validate"/>).
    /// </summary>
    public static string? ValidateKind(TenantScope scope, bool concesionDocumentsEnabled)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.GroupKind switch
        {
            GroupKind.MarcaBlanca => null,
            GroupKind.Concesion when concesionDocumentsEnabled => null,
            _ => DocumentsDisabled,
        };
    }
}

/// <summary>
/// Desenlace de una lectura de documentos de la red. <see cref="ProcedureTenantId"/> es el dueño del
/// trámite cuando se pudo resolver (también en rechazos y en 404 sobre un trámite ajeno): la API lo
/// usa SOLO para auditar (HU #12361), nunca lo devuelve al cliente.
/// </summary>
public sealed record NetworkAttachmentsOutcome<T>(T? Result, string? Error, Guid? ProcedureTenantId)
    where T : class;

/// <summary>
/// HU #12410 — listado de metadatos y descarga proxeada de los documentos de un trámite de la red.
/// Reutiliza <see cref="ListAttachmentsHandler"/> y <see cref="DownloadAttachmentHandler"/> con el
/// tenant DUEÑO del trámite (resuelto con <see cref="IProcedureInstanceOwnerLookup"/>, jamás con un
/// header) tras exigir <see cref="TenantScope.CanRead"/>: ninguna consulta de datos se ejecuta fuera
/// del conjunto de lectura. Orden de decisión:
/// <list type="number">
///   <item>Sin alcance de grupo ⇒ <see cref="NetworkScopePolicy.ScopeRequired"/> (403; la policy del grupo ya lo evita).</item>
///   <item>Trámite inexistente o dueño fuera del conjunto de lectura ⇒ <see cref="NetworkDocumentsPolicy.NotFound"/>
///   (404 escueto, idéntico en ambos casos: no revela existencia, AC6).</item>
///   <item>Clase CONCESION con el interruptor apagado ⇒ <see cref="NetworkDocumentsPolicy.DocumentsDisabled"/> (403, AC5).</item>
///   <item>Documento inexistente o binario perdido ⇒ el MISMO <see cref="NetworkDocumentsPolicy.NotFound"/>.</item>
/// </list>
/// No expone direcciones prefirmadas ni <c>storagePath</c>: los metadatos son el <see cref="AttachmentDto"/>
/// de siempre y el contenido va por transmisión (AC1/AC2). El modelo no tiene «versión» de documento.
/// </summary>
public sealed class NetworkAttachmentsHandler(
    IProcedureInstanceOwnerLookup ownerLookup,
    IHierarchySwitches switches,
    ListAttachmentsHandler list,
    DownloadAttachmentHandler download)
{
    public async Task<NetworkAttachmentsOutcome<AttachmentsResponse>> ListAsync(
        Guid id,
        TenantScope? scope,
        CancellationToken ct = default)
    {
        var (owner, error) = await AuthorizeAsync(id, scope, ct).ConfigureAwait(false);
        if (error is not null)
            return new(null, error, owner);

        var (result, listError) = await list.HandleAsync(id, owner!.Value, ct).ConfigureAwait(false);
        return listError is not null || result is null
            ? new(null, NetworkDocumentsPolicy.NotFound, owner)
            : new(result, null, owner);
    }

    public async Task<NetworkAttachmentsOutcome<AttachmentDownload>> DownloadAsync(
        Guid id,
        Guid attachmentId,
        TenantScope? scope,
        CancellationToken ct = default)
    {
        var (owner, error) = await AuthorizeAsync(id, scope, ct).ConfigureAwait(false);
        if (error is not null)
            return new(null, error, owner);

        var (result, downloadError) = await download.HandleAsync(id, owner!.Value, attachmentId, ct).ConfigureAwait(false);
        // not_found y file_missing colapsan en el mismo 404 escueto: distinguirlos permitiría enumerar.
        return downloadError is not null || result is null
            ? new(null, NetworkDocumentsPolicy.NotFound, owner)
            : new(result, null, owner);
    }

    /// <summary>Dueño del trámite y, si procede, el error. Dueño con valor aunque haya error (para auditar).</summary>
    private async Task<(Guid? Owner, string? Error)> AuthorizeAsync(Guid id, TenantScope? scope, CancellationToken ct)
    {
        if (NetworkScopePolicy.Validate(scope) is { } scopeError)
            return (null, scopeError);

        var owner = await ownerLookup.GetOwnerTenantIdAsync(id, ct).ConfigureAwait(false);
        if (owner is not { } tenantId || tenantId == Guid.Empty)
            return (null, NetworkDocumentsPolicy.NotFound);

        if (!scope!.CanRead(tenantId))
            return (tenantId, NetworkDocumentsPolicy.NotFound);

        var concesionEnabled = scope.GroupKind == GroupKind.Concesion
            && await switches.IsNetworkDocumentsConcesionEnabledAsync(ct).ConfigureAwait(false);
        if (NetworkDocumentsPolicy.ValidateKind(scope, concesionEnabled) is { } kindError)
            return (tenantId, kindError);

        return (tenantId, null);
    }
}
