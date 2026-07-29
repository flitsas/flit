using Flit.DataMigration.V1.Mapping;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.EntityFrameworkCore;

namespace Flit.DataMigration.V1.Loading;

/// <summary>Qué terminó pasando con un trámite.</summary>
public enum LoadStatus
{
    /// <summary>Escrito en V2.</summary>
    Migrated,

    /// <summary>Ya estaba migrado; no se tocó (idempotencia).</summary>
    Skipped,

    /// <summary>Simulado y revertido (<c>--dry-run</c>).</summary>
    Simulated,

    /// <summary>No se pudo migrar; queda para revisión manual.</summary>
    Quarantined,
}

public sealed class LoadResult
{
    public required long V1Id { get; init; }
    public required LoadStatus Status { get; init; }
    public Guid? V2Id { get; init; }
    public string? FinalStatus { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public int FieldCount { get; init; }
    public int ActorCount { get; init; }
    public int HistoryCount { get; init; }
}

/// <summary>
/// Escribe el trámite en V2 respetando la SECUENCIA OBLIGATORIA que impone el trigger
/// <c>tr_procedure_instance_field_values_immutable</c>.
/// <para>
/// El trigger solo permite escribir <c>field_values</c> mientras el trámite padre esté en
/// <c>borrador</c>. Como los trámites históricos llegan en estados finales, hay que insertarlos
/// primero como borrador, cargarles todo, y solo entonces subirlos a su estado real. Invertir
/// este orden hace fallar la migración con <c>check_violation</c>.
/// </para>
/// </summary>
public sealed class ProcedureInstanceLoader(
    FlitDbContext db,
    MigrationMapStore migrationMap,
    string batchId)
{
    public async Task<LoadResult> LoadAsync(
        MappedProcedure mapped,
        bool dryRun,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mapped);

        var existing = await migrationMap.FindAsync(mapped.V1Table, mapped.V1Id, cancellationToken);
        if (existing is not null && !force)
        {
            return new LoadResult
            {
                V1Id = mapped.V1Id,
                Status = LoadStatus.Skipped,
                V2Id = existing,
                Reason = "Ya migrado (está en migration_map). Use --force para re-migrar.",
            };
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // RLS: la sesión declara sobre qué tenant trabaja. En local el owner la ignora,
            // pero dejarlo puesto hace que el mismo binario funcione con un rol restringido.
            await db.Database.ExecuteSqlRawAsync(
                "SELECT set_config('app.current_tenant_id', {0}, true)",
                [mapped.Instance.TenantId.ToString()],
                cancellationToken);

            if (existing is not null && force)
            {
                await migrationMap.DeleteMigratedAsync(
                    mapped.V1Table, mapped.V1Id, existing.Value, cancellationToken);
            }

            // ---- Paso 1: la instancia entra en 'borrador' (lo exige el trigger).
            db.ProcedureInstances.Add(mapped.Instance);
            await db.SaveChangesAsync(cancellationToken);

            // ---- Paso 2 y 3: actores y campos, con el padre todavía en borrador.
            db.ProcedureInstanceActors.AddRange(mapped.Actors);
            db.ProcedureInstanceFieldValues.AddRange(mapped.FieldValues);
            // Datos comerciales (1:1), si V1 traía valor de venta. Va en la misma transacción para
            // que el paso "comercial" del wizard quede con contenido y no se cree a medias.
            if (mapped.Commercial is not null)
            {
                db.ProcedureInstanceCommercials.Add(mapped.Commercial);
            }
            await db.SaveChangesAsync(cancellationToken);

            // ---- Paso 4: recién ahora el estado real. La máquina de estados de V2 se valida
            // en la capa de aplicación, no en la base, así que un histórico puede quedar
            // directamente en 'aprobado' sin simular todo el ciclo de vida.
            if (!string.Equals(mapped.FinalStatus, TramiteEstado.Borrador, StringComparison.Ordinal))
            {
                mapped.Instance.Status = mapped.FinalStatus;
                await db.SaveChangesAsync(cancellationToken);
            }

            // ---- Paso 5: la línea temporal. No se llena sola en inserción directa.
            db.ProcedureInstanceStatusHistories.AddRange(mapped.StatusHistory);
            await db.SaveChangesAsync(cancellationToken);

            // ---- Paso 6: la libreta, dentro de la MISMA transacción. Si algo falla después,
            // no queda un trámite migrado sin registrar (ni al revés).
            await migrationMap.RecordAsync(
                mapped.V1Table, mapped.V1Id, mapped.Instance.Id, mapped.Instance.TenantId,
                batchId, mapped.FinalStatus, mapped.Warnings, cancellationToken);

            if (dryRun)
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            else
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new LoadResult
            {
                V1Id = mapped.V1Id,
                Status = dryRun ? LoadStatus.Simulated : LoadStatus.Migrated,
                V2Id = mapped.Instance.Id,
                FinalStatus = mapped.FinalStatus,
                Warnings = mapped.Warnings,
                FieldCount = mapped.FieldValues.Count,
                ActorCount = mapped.Actors.Count,
                HistoryCount = mapped.StatusHistory.Count,
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new LoadResult
            {
                V1Id = mapped.V1Id,
                Status = LoadStatus.Quarantined,
                Reason = ex.Message,
                Warnings = mapped.Warnings,
            };
        }
        finally
        {
            // Cada trámite parte de cero: sin esto, el segundo del lote choca con las
            // entidades que el primero dejó rastreadas.
            db.ChangeTracker.Clear();
        }
    }
}
