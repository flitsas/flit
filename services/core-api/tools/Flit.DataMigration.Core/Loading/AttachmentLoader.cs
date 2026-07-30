using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Source;
using Flit.DataMigration.V1.Storage;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.DataMigration.V1.Loading;

/// <summary>Cómo se lleva el binario del origen al destino.</summary>
public enum CopyMode
{
    /// <summary>Descargar del origen y subir al destino (stores distintos: p. ej. AWS → MinIO).</summary>
    Copy,

    /// <summary>Mismo store: no se mueve el binario, <c>storage_path</c> = id de V1 (cero copia).</summary>
    Reference,
}

/// <summary>Qué pasó con los adjuntos de un trámite.</summary>
public enum AttachmentLoadStatus
{
    Migrated,
    Simulated,
    Skipped,
    NotMigrated,
    NoAttachments,
}

public sealed class AttachmentLoadResult
{
    public required long V1Id { get; init; }
    public required AttachmentLoadStatus Status { get; init; }
    public Guid? V2Id { get; init; }
    public string? Reason { get; init; }
    public int Copied { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public int Excluded { get; init; }

    /// <summary>Imágenes de identidad no migradas porque la carta selfie ya las contiene.</summary>
    public int Redundant { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Instancia 2: lleva los adjuntos de un trámite de V1 a <c>procedure_instance_attachments</c> de V2.
/// Exige que la data plana ya exista (el trámite tiene que estar en <c>migration_map</c>).
/// <para>
/// Por cada columna <c>id_attach*</c> con valor: lee del file-manager ORIGEN, calcula el sha256
/// real, y —según el modo— sube al file-manager DESTINO (copia) o referencia el mismo id (mismo
/// store). Cada adjunto se aísla: si uno falla (p. ej. el origen está apagado y da 500), se reporta
/// y se sigue con los demás; no tumba el trámite.
/// </para>
/// </summary>
public sealed class AttachmentLoader(
    V1ProcedureKind kind,
    FlitDbContext db,
    MigrationMapStore migrationMap,
    AttachmentMapStore attachmentMap,
    FileManagerClient source,
    FileManagerClient? target,
    CopyMode mode,
    Guid systemUserId,
    string batchId,
    bool keepIdentityImages = false)
{
    public async Task<AttachmentLoadResult> LoadAsync(
        V1SourceRecord record,
        bool dryRun,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var targetRef = await migrationMap.FindTargetAsync(record.SourceTable, record.Id, cancellationToken);
        if (targetRef is null)
        {
            return new AttachmentLoadResult
            {
                V1Id = record.Id,
                Status = AttachmentLoadStatus.NotMigrated,
                Reason = "El trámite no está en migration_map: primero migra la data plana "
                    + $"(--tipo {kind.CliName}).",
            };
        }

        // Columnas de adjunto con valor. Resolvemos el 'tipo' de V2 y separamos excluidas/desconocidas.
        var warnings = new List<string>();
        var toProcess = new List<(string Column, string Tipo, string SourceId)>();
        var excluded = 0;
        var redundant = 0;

        // Imágenes sueltas de la validación de identidad que la carta selfie ya contiene. La carta
        // la materializa la instancia 3 (--tipo <trámite>-documents): si no se corre, estas imágenes
        // no llegan a V2 por ninguna vía. Siguen en V1 y se recuperan con --conservar-jpg-identidad.
        var redundantColumns = keepIdentityImages
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : (IReadOnlyDictionary<string, string>)kind.IdentityPolicy.RedundantColumns(record);

        foreach (var (column, value) in record.Columns)
        {
            if (!V1ProcedureKind.IsAttachmentColumn(column) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (redundantColumns.TryGetValue(column, out var motivo))
            {
                warnings.Add($"{column}: no se migra — {motivo} (id de V1 {value}).");
                redundant++;
                continue;
            }

            switch (kind.AttachmentMap.Resolve(column, out var tipo))
            {
                case V1AttachmentMap.Resolution.Mapped:
                    toProcess.Add((column, tipo, value));
                    break;
                case V1AttachmentMap.Resolution.Excluded:
                    excluded++;
                    break;
                default:
                    warnings.Add(
                        $"{column}: columna de adjunto sin 'tipo' mapeado en {kind.AttachmentMap.GetType().Name} "
                        + $"— NO se migra (id de V1 {value}).");
                    break;
            }
        }

        if (toProcess.Count == 0)
        {
            return new AttachmentLoadResult
            {
                V1Id = record.Id,
                Status = AttachmentLoadStatus.NoAttachments,
                V2Id = targetRef.V2Id,
                Excluded = excluded,
                Redundant = redundant,
                Warnings = warnings,
            };
        }

        // Idempotencia: sin --force, saltamos las columnas ya migradas.
        var already = force
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (HashSet<string>)await attachmentMap.MigratedColumnsAsync(record.SourceTable, record.Id, cancellationToken);

        var copied = 0;
        var skipped = 0;
        var failed = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.current_tenant_id', {0}, true)",
                [targetRef.TenantId.ToString()],
                cancellationToken);

            if (force)
            {
                await attachmentMap.DeleteForTramiteAsync(
                    record.SourceTable, record.Id, targetRef.V2Id, cancellationToken);
            }

            foreach (var (column, tipo, sourceId) in toProcess)
            {
                if (already.Contains(column))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var one = await CopyOneAsync(record, targetRef, column, tipo, sourceId, dryRun, cancellationToken);
                    if (one is null)
                    {
                        warnings.Add($"{column}: el file-manager origen no conoce el id {sourceId} (¿ambiente apagado o binario purgado?).");
                        failed++;
                    }
                    else
                    {
                        if (one.Value.Warning is not null)
                        {
                            warnings.Add(one.Value.Warning);
                        }

                        copied++;
                    }
                }
#pragma warning disable CA1031 // Un adjunto que falla no debe tumbar el resto del trámite.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    warnings.Add($"{column}: {ex.Message}");
                    failed++;
                }
            }

            if (dryRun)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            else
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        return new AttachmentLoadResult
        {
            V1Id = record.Id,
            Status = dryRun ? AttachmentLoadStatus.Simulated : AttachmentLoadStatus.Migrated,
            V2Id = targetRef.V2Id,
            Copied = copied,
            Skipped = skipped,
            Failed = failed,
            Excluded = excluded,
            Redundant = redundant,
            Warnings = warnings,
        };
    }

    /// <summary>Un adjunto copiado con éxito; <c>Warning</c> no nulo si hubo un aviso no fatal.</summary>
    private readonly record struct CopyOutcome(string? Warning);

    /// <summary>Copia (o referencia) un adjunto. Devuelve <c>null</c> si el origen no conoce el id.</summary>
    private async Task<CopyOutcome?> CopyOneAsync(
        V1SourceRecord record,
        TramiteTarget targetRef,
        string column,
        string tipo,
        string sourceId,
        bool dryRun,
        CancellationToken cancellationToken)
    {
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
