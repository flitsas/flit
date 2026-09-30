using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Source;
using Flit.DataMigration.V1.Storage;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;

namespace Flit.DataMigration.V1.Loading;

/// <summary>Un adjunto copiado con éxito; <c>Warning</c> no nulo si hubo un aviso no fatal.</summary>
public readonly record struct CopyOutcome(string? Warning);

/// <summary>
/// Lleva UN archivo de V1 a <c>procedure_instance_attachments</c> de V2 y lo anota en la libreta de
/// adjuntos. Lo usan las dos instancias que copian archivos de V1: la de adjuntos, columna por
/// columna, y la de documentos, para el consolidado que V1 tiene guardado cuando la copia de la base
/// no lo refleja (HU #13163).
/// <para>
/// Que sea la misma pieza de código no es estética: el id del adjunto es determinístico sobre la
/// columna (<c>attach:{columna}</c>). Si las dos instancias copian la misma columna por caminos
/// distintos, escriben la misma fila, y la segunda la encuentra ya anotada en vez de duplicarla.
/// </para>
/// <para>
/// No abre transacción ni fija el tenant de la sesión: eso es de quien llama, que decide si confirma
/// o revierte (<c>--dry-run</c>).
/// </para>
/// </summary>
public sealed class AttachmentCopier(
    FlitDbContext db,
    AttachmentMapStore attachmentMap,
    FileManagerClient source,
    FileManagerClient? target,
    CopyMode mode,
    Guid systemUserId,
    string batchId)
{
    /// <summary>Copia (o referencia) un adjunto. Devuelve <c>null</c> si el origen no conoce el id.</summary>
    public async Task<CopyOutcome?> CopyAsync(
        V1SourceRecord record,
        TramiteTarget targetRef,
        string column,
        string tipo,
        string sourceId,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(targetRef);

        var head = await source.HeadAsync(sourceId, cancellationToken);
        if (head is null)
        {
            return null;
        }

        var bytes = await source.DownloadAsync(head.DownloadUrl, cancellationToken);
        var sha256 = FileManagerClient.Sha256Hex(bytes);

        string? warning = null;
        if (!string.IsNullOrWhiteSpace(head.Sha256)
            && !string.Equals(head.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            warning = $"{column}: el sha256 de la metadata ({head.Sha256}) no coincide con el del binario ({sha256}); se usa el calculado.";
        }

        // storage_path: destino nuevo (copia) o el mismo id de V1 (referencia). En dry-run NO se sube
        // (una subida real no la revierte el rollback de la BD): se simula y se marca.
        string storagePath;
        if (mode == CopyMode.Reference)
        {
            storagePath = sourceId;
        }
        else if (dryRun)
        {
            storagePath = $"(dry-run: no subido; origen {sourceId})";
        }
        else
        {
            var uploaded = await target!.UploadAsync(
                targetRef.V2Id, tipo, head.Filename, bytes, sha256, cancellationToken);
            storagePath = uploaded.Id;
        }

        var attachmentId = DeterministicGuid.ForV1Child(record.SourceTable, record.Id, $"attach:{column}");
        var mimetype = FileManagerClient.GuessMimetype(head.Filename);

        db.Set<ProcedureInstanceAttachment>().Add(new ProcedureInstanceAttachment
        {
            Id = attachmentId,
            TenantId = targetRef.TenantId,
            ProcedureInstanceId = targetRef.V2Id,
            Tipo = tipo,
            Filename = head.Filename,
            Mimetype = mimetype,
            SizeBytes = bytes.LongLength,
            Sha256 = sha256,
            StoragePath = storagePath,
            Source = "migration",
            UploadedAt = DateTimeOffset.UtcNow,
            UploadedBy = systemUserId,
        });
        await db.SaveChangesAsync(cancellationToken);

        await attachmentMap.RecordAsync(
            new AttachmentMapEntry
            {
                V1Table = record.SourceTable,
                V1Id = record.Id,
                V1Column = column,
                SourceFileId = sourceId,
                V2AttachmentId = attachmentId,
                V2ProcedureInstanceId = targetRef.V2Id,
                TenantId = targetRef.TenantId,
                Tipo = tipo,
                Mode = mode.ToString(),
                StoragePath = storagePath,
                Sha256 = sha256,
                SizeBytes = bytes.LongLength,
                Filename = head.Filename,
                Mimetype = mimetype,
            },
            batchId,
            cancellationToken);

        return new CopyOutcome(warning);
    }
}
