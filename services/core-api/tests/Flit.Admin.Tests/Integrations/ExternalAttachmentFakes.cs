using System.Security.Cryptography;
using Flit.Tramites.Application.Storage;
using Flit.Tramites.Domain.ExternalSync;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Admin.Tests.Integrations;

/// <summary>
/// HU #13263 — dobles en memoria del envío de adjuntos externo, compartidos por las pruebas del endpoint y del contrato.
/// El bloqueo de fila y el SQL reales se prueban contra Postgres en <c>Flit.Integration.Tests</c>.
/// </summary>
public sealed class EnvioEnMemoria : IExternalAttachmentRepository, IExternalAttachmentWriter
{
    /// <summary>Trámite que «existe»; <c>null</c> = inexistente o fuera de alcance.</summary>
    public ExternalAttachmentTarget? Target { get; set; }

    public List<NewExternalAttachment> Escritos { get; } = [];

    public List<Guid> Retirados { get; } = [];

    /// <summary>Veces que se pidió marcar <c>impuesto_departamental_pagado</c> (HU #13264).</summary>
    public int Marcas { get; private set; }

    public void Reiniciar(ExternalAttachmentTarget? target)
    {
        Target = target;
        Marcas = 0;
        Escritos.Clear();
        Retirados.Clear();
    }

    public Task<T> RunLockedAsync<T>(
        Guid procedureId, string tipo,
        Func<ExternalAttachmentTarget?, IExternalAttachmentWriter, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default) =>
        work(Target is { } t && t.ProcedureId == procedureId ? t : null, this, cancellationToken);

    public Task<Guid> ReplaceAsync(NewExternalAttachment attachment, IReadOnlyCollection<Guid> retire, CancellationToken cancellationToken)
    {
        Escritos.Add(attachment);
        Retirados.AddRange(retire);
        return Task.FromResult(Guid.CreateVersion7());
    }

    public Task MarkTaxPaidAsync(CancellationToken cancellationToken)
    {
        Marcas++;
        return Task.CompletedTask;
    }
}

public sealed class AlmacenEnMemoria : IAttachmentStorage
{
    public List<string> Guardados { get; } = [];

    public List<string> Borrados { get; } = [];

    public Exception? Falla { get; set; }

    public async Task<StoredFile> SaveAsync(Guid procedureInstanceId, string tipo, string originalFilename, Stream content, CancellationToken ct = default)
    {
        if (Falla is not null)
        {
            throw Falla;
        }

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var path = $"fm-envio-{Guardados.Count + 1}";
        Guardados.Add(path);
        return new StoredFile(path, Convert.ToHexStringLower(SHA256.HashData(ms.ToArray())), ms.Length);
    }

    public void Delete(string storagePath) => Borrados.Add(storagePath);

    public Task<PresignedUpload> CreatePresignedUploadAsync(Guid procedureInstanceId, string tipo, string originalFilename, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<Stream?> OpenReadAsync(string storagePath, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<(string Url, DateTimeOffset ExpiresAt)?> GetPresignedViewUrlAsync(string storagePath, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

public sealed class MatrizFija : IResolvedChecklistMatrixProvider
{
    public bool IncluyeLiquidacionImpuesto { get; set; } = true;

    public Task<IReadOnlyList<ResolvedChecklistDoc>> GetForAsync(Guid procedureTypeId, Guid? transitOfficeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ResolvedChecklistDoc>>(
            IncluyeLiquidacionImpuesto ? [new ResolvedChecklistDoc("liquidacion_impuesto", "Liquidación de impuesto", false, 1)] : []);
}
