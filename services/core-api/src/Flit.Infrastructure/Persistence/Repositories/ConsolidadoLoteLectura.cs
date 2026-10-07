using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13379 (Épica #13216, ADR-0070 D7/D8) — <see cref="IConsolidadoLoteLectura"/> sobre PostgreSQL con LINQ de EF
/// (parametrizado). Toda lectura de un lote filtra por <c>requested_by_user_id</c> (I1: el dueño es el <c>sub</c>; RLS
/// decorativa, A3.1) y nunca por tenant. La fila <c>parte_descargada</c> se inserta en su propia transacción implícita
/// (<c>SaveChanges</c>); si falla se devuelve <c>false</c> y el contexto queda limpio. Logs sin PII: ids y tipo de error.
/// </summary>
internal sealed partial class ConsolidadoLoteLectura(
    FlitDbContext db,
    ILogger<ConsolidadoLoteLectura>? logger = null) : IConsolidadoLoteLectura
{
    private static readonly string[] EstadosActivos = [.. ConsolidadoExportStatus.Activos];

    private readonly ILogger _logger = logger ?? NullLogger<ConsolidadoLoteLectura>.Instance;

    public Task<ConsolidadoExportBatch?> ObtenerDelDuenoAsync(Guid loteId, Guid usuarioId, CancellationToken ct = default) =>
        db.ConsolidadoExportBatches.AsNoTracking()
            .Where(b => b.Id == loteId && b.RequestedByUserId == usuarioId && b.DeletedAt == null)
            .FirstOrDefaultAsync(ct);

    public Task<ConsolidadoExportBatch?> ObtenerActualDelDuenoAsync(Guid usuarioId, CancellationToken ct = default) =>
        // Activo primero (índice único parcial: como mucho uno); si no, el último terminal aún retenido.
        db.ConsolidadoExportBatches.AsNoTracking()
            .Where(b => b.RequestedByUserId == usuarioId && b.DeletedAt == null && b.PurgedAt == null)
            .OrderByDescending(b => EstadosActivos.Contains(b.Status))
            .ThenByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ConsolidadoExportBatchPart>> ObtenerPartesAsync(Guid loteId, CancellationToken ct = default) =>
        await db.ConsolidadoExportBatchParts.AsNoTracking()
            .Where(p => p.BatchId == loteId)
            .OrderBy(p => p.PartNumber)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<Guid>> ObtenerVencidosAsync(DateTimeOffset ahora, int maximo, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximo, 1);
        return await db.ConsolidadoExportBatches.AsNoTracking()
            .Where(b => b.PurgedAt == null && b.ExpiresAt != null && b.ExpiresAt <= ahora
                        && b.DeletedAt == null && !EstadosActivos.Contains(b.Status))
            .OrderBy(b => b.ExpiresAt)
            .ThenBy(b => b.Id)
            .Select(b => b.Id)
            .Take(maximo)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> RegistrarDescargaAsync(ParteDescargadaRegistro registro, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(registro);
        var lote = registro.Lote;
        try
        {
            db.ConsolidadoExportAuditEntries.Add(new ConsolidadoExportAuditEntry
            {
                Id = Guid.CreateVersion7(),
                OccurredAt = registro.OcurridoEn,
                Event = ConsolidadoExportAuditEvent.ParteDescargada,
                Origin = lote.Origin,
                BatchId = lote.Id,
                ActorUserId = lote.RequestedByUserId,
                // ck_consolidado_export_audit_tenant_origin: NULL si y solo si el origen es superadmin (el del lote).
                ActorTenantId = lote.TenantId,
                ActorRoleCode = registro.RolCodigo,
                ScopeTenantId = lote.ScopeTenantId,
                DocumentType = lote.DocumentType,
                PartNumber = registro.PartNumber,
                ClientIp = registro.ClientIp,
                UserAgent = registro.UserAgent,
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            LogAuditoriaFallida(_logger, lote.Id, registro.PartNumber, ex.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Lote {LoteId}: no se pudo registrar parte_descargada de la parte {PartNumber} ({Tipo}); no se entregan bytes.")]
    private static partial void LogAuditoriaFallida(ILogger logger, Guid loteId, short partNumber, string tipo);
}
