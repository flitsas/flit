using System.Globalization;
using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Source;
using Flit.DataMigration.V1.Storage;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.DataMigration.V1.Loading;

/// <summary>Qué pasó con los documentos materializados de un trámite.</summary>
public enum SnapshotLoadStatus
{
    Materialized,
    Simulated,
    NotMigrated,
    NotFoundInV1,
    Failed,
}

public sealed class SnapshotLoadResult
{
    public required long V1Id { get; init; }
    public required SnapshotLoadStatus Status { get; init; }
    public Guid? V2Id { get; init; }
    public string? Reason { get; init; }
    public int Materialized { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }

    /// <summary>Piezas no guardadas porque su binario ya lo copió la instancia 2.</summary>
    public int Duplicated { get; init; }

    /// <summary>Piezas que V1 no pudo construir o entregó degradadas, con su motivo.</summary>
    public IReadOnlyList<string> Issues { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Instancia 3: materializa en V2 los documentos que V1 genera en caliente y nunca guarda.
/// <para>
/// Los adjuntos que el cliente cargó ya los trae la instancia 2. Lo que falta —y lo que se pierde
/// para siempre el día que V1 se apague— es la portada, el FUR, la compraventa del sistema, las
/// cartas selfie, el mandato, el trámite virtual, la limitación de propiedad, la carta declaratoria
/// y la autorización al apoderado: V1 los arma cada vez que alguien los pide y los descarta, y V2
/// no tiene generadores para ellos.
/// </para>
/// <para>
/// Se le piden a V1 con <c>GET /vehicle-transfer-migration/:id/snapshot</c>, que los devuelve en la
/// respuesta sin escribir nada en V1, y se suben al file-manager de V2. Cada pieza se aísla: si una
/// falla, se reporta y el resto continúa.
/// </para>
/// </summary>
public sealed class SnapshotLoader(
    V1ProcedureKind kind,
    FlitDbContext db,
    MigrationMapStore migrationMap,
    AttachmentMapStore attachmentMap,
    V1SnapshotClient snapshotClient,
    FileManagerClient target,
    Guid systemUserId,
    string batchId)
{
    /// <summary>
    /// Origen que queda en <c>procedure_instance_attachments.source</c>. Se distingue de
    /// <c>migration</c> (adjuntos copiados tal cual) porque estas piezas no existían como archivo:
    /// las construyó V1 en el momento de migrar. La columna es varchar(20).
    /// </summary>
    private const string SourceTag = "migration_snapshot";

    /// <summary>Cómo llegó el binario, en el vocabulario del mapa de adjuntos (Copy / Reference).</summary>
    private const string ModeTag = "Snapshot";

    /// <summary>
    /// Prefijo de la clave con que se registra la pieza en el mapa de adjuntos. Distingue lo
    /// materializado de lo copiado por columna, de modo que ambas instancias son idempotentes por
    /// separado y no se pisan.
    /// </summary>
    private const string ColumnPrefix = "snapshot:";

    public async Task<SnapshotLoadResult> LoadAsync(
        V1SourceRecord record,
        bool dryRun,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        var targetRef = await migrationMap.FindTargetAsync(record.SourceTable, record.Id, cancellationToken);
        if (targetRef is null)
        {
            return new SnapshotLoadResult
            {
                V1Id = record.Id,
                Status = SnapshotLoadStatus.NotMigrated,
                Reason = "El trámite no está en migration_map: primero migra la data plana "
                    + $"(--tipo {kind.CliName}).",
            };
        }

        V1Snapshot? snapshot;
        try
        {
            snapshot = await snapshotClient.GetAsync(record.Id, cancellationToken);
        }
#pragma warning disable CA1031 // Un trámite que falla no debe tumbar el lote.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return new SnapshotLoadResult
            {
                V1Id = record.Id,
                Status = SnapshotLoadStatus.Failed,
                V2Id = targetRef.V2Id,
                Reason = ex.Message,
            };
        }

        if (snapshot is null)
        {
            return new SnapshotLoadResult
            {
                V1Id = record.Id,
                Status = SnapshotLoadStatus.NotFoundInV1,
                V2Id = targetRef.V2Id,
                Reason = "V1 no conoce el trámite (404).",
            };
        }

        // Lo que V1 reporta como ausente o degradado se arrastra al reporte tal cual: es la única
        // señal de que algo del expediente no se pudo materializar antes de apagar V1.
        var issues = snapshot.Issues
            .Select(i => $"[{i.Status}] {i.Name}: {i.Reason}")
            .ToList();

        var warnings = new List<string>();

        // Con --force hay que BORRAR lo materializado antes, no solo ignorar la libreta: el id del
        // adjunto es determinístico sobre la clave de la pieza, así que re-insertar sin borrar choca
        // contra la clave primaria y --force queda inservible.
        if (force && !dryRun)
        {
            await attachmentMap.DeleteSnapshotForTramiteAsync(
                record.SourceTable, record.Id, targetRef.V2Id, cancellationToken);
        }

        var already = force
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : (HashSet<string>)await attachmentMap.MigratedColumnsAsync(record.SourceTable, record.Id, cancellationToken);

        // Binarios que la instancia 2 ya trajo. Una pieza que declara venir de uno de ellos no es un
        // documento nuevo: es el mismo archivo con otro nombre. Esto SÍ se consulta con --force,
        // porque --force reprocesa el snapshot, no borra lo que copió la instancia 2.
        var yaCopiados = await attachmentMap.MigratedSourceFileIdsAsync(
            record.SourceTable, record.Id, cancellationToken);

        // Aviso de coherencia entre instancias: si la instancia 2 descartó las imágenes de identidad
        // confiando en que aquí llegaría la carta selfie y no llegó, esas imágenes no están en V2.
        foreach (var parte in kind.IdentityPolicy.PartiesSinCartaSelfie(
                     record, [.. snapshot.Pieces.Select(p => p.Key)]))
        {
            warnings.Add(
                $"V1 no entregó la carta selfie del {parte}, pero la instancia 2 descartó sus imágenes por "
                + "considerarlas cubiertas por esa carta: hoy ese trámite no tiene ni la una ni las otras. "
                + $"Recupéralas con --tipo {kind.CliName}-attachments --conservar-jpg-identidad para este id "
                + "(no hace falta --force: solo añade lo que falta).");
        }

        var materialized = 0;
        var skipped = 0;
        var failed = 0;
        var duplicated = 0;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.current_tenant_id', {0}, true)",
                [targetRef.TenantId.ToString()],
                cancellationToken);

            foreach (var piece in snapshot.Pieces)
            {
                var column = ColumnPrefix + piece.Key;

                if (already.Contains(column))
                {
                    skipped++;
                    continue;
                }

                if (piece.SourceFileId is not null && yaCopiados.Contains(piece.SourceFileId))
                {
                    warnings.Add(
                        $"{piece.Key}: no se guarda — V1 no generó este documento, lo tomó del adjunto "
                        + $"{piece.SourceFileId}, que la instancia 2 ya migró.");
                    duplicated++;
                    continue;
                }

                try
                {
                    var warning = await MaterializeOneAsync(
                        record, targetRef, snapshot, piece, column, dryRun, cancellationToken);

                    if (warning is not null)
                    {
                        warnings.Add(warning);
                    }

                    materialized++;
                }
#pragma warning disable CA1031 // Una pieza que falla no debe costar el resto del expediente.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    warnings.Add($"{piece.Key}: {Describe(ex)}");
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

        return new SnapshotLoadResult
        {
            V1Id = record.Id,
            Status = dryRun ? SnapshotLoadStatus.Simulated : SnapshotLoadStatus.Materialized,
            V2Id = targetRef.V2Id,
            Materialized = materialized,
            Skipped = skipped,
            Failed = failed,
            Duplicated = duplicated,
            Issues = issues,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Aplana la cadena de excepciones. EF envuelve el error real de Postgres —la restricción que
    /// se violó— en un mensaje genérico; sin el interior, el reporte del lote no dice nada útil.
    /// </summary>
    private static string Describe(Exception ex)
    {
        var mensajes = new List<string>();
        for (var actual = ex; actual is not null; actual = actual.InnerException)
        {
            if (!mensajes.Contains(actual.Message, StringComparer.Ordinal))
            {
                mensajes.Add(actual.Message);
            }
        }

        return string.Join(" → ", mensajes);
    }

    /// <summary>Sube una pieza al file-manager de V2 y la registra. Devuelve un aviso no fatal, o null.</summary>
    private async Task<string?> MaterializeOneAsync(
        V1SourceRecord record,
        TramiteTarget targetRef,
        V1Snapshot snapshot,
        V1SnapshotPiece piece,
        string column,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var bytes = Convert.FromBase64String(piece.ContentBase64);

        // El sha256 lo recalculamos sobre el binario recibido: es la verificación de que la pieza
        // llegó completa. Si no coincide con el que declaró V1, se avisa y manda el calculado.
        var sha256 = FileManagerClient.Sha256Hex(bytes);
        string? warning = null;
        if (!string.Equals(piece.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            warning = $"{piece.Key}: el sha256 declarado por V1 ({piece.Sha256}) no coincide con el del binario "
                + $"recibido ({sha256}); se usa el calculado.";
        }

        // En dry-run NO se sube: una subida real no la revierte el rollback de la base.
        string storagePath;
        if (dryRun)
        {
            storagePath = $"(dry-run: no subido; pieza {piece.Key})";
        }
        else
        {
            var uploaded = await target.UploadAsync(
                targetRef.V2Id, piece.DocumentTypeCode, piece.Filename, bytes, sha256, cancellationToken);
            storagePath = uploaded.Id;
        }

        var attachmentId = DeterministicGuid.ForV1Child(record.SourceTable, record.Id, $"attach:{column}");

        db.Set<ProcedureInstanceAttachment>().Add(new ProcedureInstanceAttachment
        {
            Id = attachmentId,
            TenantId = targetRef.TenantId,
            ProcedureInstanceId = targetRef.V2Id,
            Tipo = piece.DocumentTypeCode,
            Filename = piece.Filename,
            Mimetype = piece.MimeType,
            SizeBytes = bytes.LongLength,
            Sha256 = sha256,
            StoragePath = storagePath,
            Source = SourceTag,
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
                // La pieza no viene de un archivo de V1 salvo que sea un adjunto ya cargado; en ese
                // caso se guarda su id de origen para poder rastrearla.
                SourceFileId = piece.SourceFileId ?? SnapshotOrigin(snapshot),
                V2AttachmentId = attachmentId,
                V2ProcedureInstanceId = targetRef.V2Id,
                TenantId = targetRef.TenantId,
                Tipo = piece.DocumentTypeCode,
                Mode = ModeTag,
                StoragePath = storagePath,
                Sha256 = sha256,
                SizeBytes = bytes.LongLength,
                Filename = piece.Filename,
                Mimetype = piece.MimeType,
            },
            batchId,
            cancellationToken);

        return warning;
    }

    /// <summary>
    /// Marca de origen de una pieza generada: no hay archivo en V1 que la respalde, así que se deja
    /// constancia del momento exacto en que V1 la construyó.
    /// </summary>
    private static string SnapshotOrigin(V1Snapshot snapshot) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"(generado por V1 el {snapshot.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}, estado {snapshot.StatusName})");
}
