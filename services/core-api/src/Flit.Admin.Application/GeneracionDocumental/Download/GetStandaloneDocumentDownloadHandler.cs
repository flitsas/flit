using Flit.Admin.Application.GeneracionDocumental.Ports;
using Flit.Admin.Domain.GeneracionDocumental;

namespace Flit.Admin.Application.GeneracionDocumental.Download;

/// <summary>
/// Redescarga de un documento ya generado (CF-03/CF-19/CF-20, HU #12204): resuelve una presigned
/// URL de vida corta y audita la descarga.
///
/// <para><b>No regenera nada.</b> No toca el proveedor externo ni el generador de PDF: el binario ya
/// existe en storage y su <c>storage_sha256</c> no cambia — de hecho el UPDATE de auditoría ni
/// siquiera nombra esa columna, que además está congelada por el trigger de inmutabilidad.</para>
///
/// <para><b>Aislamiento (R3):</b> la búsqueda es <c>GetByIdAsync(tenantId, id)</c>. Un documento de
/// otra compañía simplemente no aparece y se responde <see cref="StandaloneDocumentDownloadOutcome.NotFound"/>
/// — no <c>403</c> con detalle: un 403 confirmaría que el id existe. Vale también para SuperAdmin:
/// ver metadata global no es poder descargar contenido ajeno (CF-20).</para>
///
/// <para><b>Auditoría atómica:</b> el incremento lo hace <c>RegisterDownloadAsync</c> en un único
/// UPDATE con <c>download_count = download_count + 1</c> en SQL. Este handler jamás lee el contador
/// para sumarle uno.</para>
///
/// <para><b>La URL firmada no se loguea.</b> Por eso aquí no hay <c>ILogger</c>: no se puede
/// escribir por accidente lo que no se tiene.</para>
/// </summary>
public sealed class GetStandaloneDocumentDownloadHandler
{
    private readonly IStandaloneDocumentRepository _repository;
    private readonly IStandaloneDocumentStorage _storage;
    private readonly TimeProvider _timeProvider;

    public GetStandaloneDocumentDownloadHandler(
        IStandaloneDocumentRepository repository,
        IStandaloneDocumentStorage storage,
        TimeProvider? timeProvider = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<GetStandaloneDocumentDownloadResult> HandleAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty || id == Guid.Empty)
        {
            return GetStandaloneDocumentDownloadResult.NotFound();
        }

        var document = await _repository.GetByIdAsync(tenantId, id, cancellationToken).ConfigureAwait(false);

        // No existe, está borrada o es de otro tenant: la misma respuesta para los tres casos.
        if (document is null)
        {
            return GetStandaloneDocumentDownloadResult.NotFound();
        }

        // pending / processing / error: no hay binario que entregar (CF-19).
        if (document.Status != StandaloneDocumentStatus.Generated || string.IsNullOrWhiteSpace(document.StoragePath))
        {
            return GetStandaloneDocumentDownloadResult.NotGenerated(document.Status);
        }

        var link = await _storage
            .GetPresignedViewUrlAsync(document.StoragePath, cancellationToken)
            .ConfigureAwait(false);

        // Fila 'generated' sin binario recuperable: no se audita una descarga que no ocurrió.
        if (link is null)
        {
            return GetStandaloneDocumentDownloadResult.NotFound();
        }

        // CF-19 — un único UPDATE atómico, DESPUÉS de tener la URL: no se cuenta lo que no se entregó.
        await _repository
            .RegisterDownloadAsync(tenantId, id, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        return GetStandaloneDocumentDownloadResult.Ok(link);
    }
}

public enum StandaloneDocumentDownloadOutcome
{
    /// <summary>Enlace entregado y descarga auditada.</summary>
    Ok,

    /// <summary>No existe, es de otro tenant o perdió su binario. Siempre 404, nunca 403.</summary>
    NotFound,

    /// <summary>Existe en el tenant pero no está en <c>generated</c>. 409, sin presigned URL.</summary>
    NotGenerated,
}

/// <summary>Resultado de la descarga. Sin <c>storagePath</c>: la ruta interna no sale del backend.</summary>
public sealed record GetStandaloneDocumentDownloadResult(
    StandaloneDocumentDownloadOutcome Outcome,
    StandaloneDocumentDownloadLink? Link,
    string? Status)
{
    public static GetStandaloneDocumentDownloadResult Ok(StandaloneDocumentDownloadLink link) =>
        new(StandaloneDocumentDownloadOutcome.Ok, link, StandaloneDocumentStatus.Generated);

    public static GetStandaloneDocumentDownloadResult NotFound() =>
        new(StandaloneDocumentDownloadOutcome.NotFound, null, null);

    public static GetStandaloneDocumentDownloadResult NotGenerated(string status) =>
        new(StandaloneDocumentDownloadOutcome.NotGenerated, null, status);
}
