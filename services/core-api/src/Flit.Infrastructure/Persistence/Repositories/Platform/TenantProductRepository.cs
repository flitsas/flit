using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Modules.Platform.Domain.TenantProducts;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories.Platform;

/// <summary>
/// Habilitación de productos por empresa sobre <c>platform.tenant_products</c> (HU #12958, ADR-0063).
/// Cada consulta filtra <c>tenant_id</c> explícitamente (sin <c>HasQueryFilter</c>, convención del
/// repo). La auditoría legible se escribe en <c>admin.tenant_config_audit_logs</c> en el MISMO
/// <c>SaveChanges</c> que la fila (patrón <c>TenantDomainRepository</c>), una fila por campo que
/// cambia; el rastro técnico lo deja el trigger <c>tr_tenant_products_audit</c>.
/// </summary>
internal sealed class TenantProductRepository : ITenantProductRepository
{
    private const string AuditEntityName = "TenantProduct";
    private const string AuditTargetType = "TENANT_PRODUCT";

    private readonly FlitDbContext _context;
    private readonly IAuditContextAccessor _auditContext;
    private readonly TimeProvider _clock;

    public TenantProductRepository(FlitDbContext context, IAuditContextAccessor? auditContext = null, TimeProvider? clock = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditContext = auditContext ?? NullAuditContextAccessor.Instance;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<TenantProduct>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.Set<TenantProductEntity>()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderBy(r => r.ProductCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(Map).ToList();
    }

    public async Task<TenantProduct?> GetAsync(Guid tenantId, string productCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);

        var row = await _context.Set<TenantProductEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.ProductCode == productCode, cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : Map(row);
    }

    public async Task<TenantProductChange> SetAsync(
        Guid tenantId,
        string productCode,
        bool enabled,
        string? notes,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);

        var row = await _context.Set<TenantProductEntity>()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.ProductCode == productCode, cancellationToken)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();

        if (row is null)
        {
            row = new TenantProductEntity
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                ProductCode = productCode,
                Enabled = enabled,
                Notes = notes,
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedBy = changedBy,
            };
            _context.Set<TenantProductEntity>().Add(row);

            AddAudit(tenantId, row.Id, "enabled", null, JsonSerializer.Serialize(enabled), AuditVocabulary.Operations.Create, changedBy, now);
            if (notes is not null)
            {
                AddAudit(tenantId, row.Id, "notes", null, JsonSerializer.Serialize(notes), AuditVocabulary.Operations.Create, changedBy, now);
            }

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new TenantProductChange(Map(row), Changed: true);
        }

        var enabledChanged = row.Enabled != enabled;
        var notesChanged = !string.Equals(row.Notes, notes, StringComparison.Ordinal);
        if (!enabledChanged && !notesChanged)
        {
            return new TenantProductChange(Map(row), Changed: false);
        }

        if (enabledChanged)
        {
            AddAudit(tenantId, row.Id, "enabled", JsonSerializer.Serialize(row.Enabled), JsonSerializer.Serialize(enabled), AuditVocabulary.Operations.Update, changedBy, now);
        }

        if (notesChanged)
        {
            // Guarda de null: JsonSerializer.Serialize(null) escribiría el literal JSON null en vez de NULL SQL.
            AddAudit(
                tenantId,
                row.Id,
                "notes",
                row.Notes is null ? null : JsonSerializer.Serialize(row.Notes),
                notes is null ? null : JsonSerializer.Serialize(notes),
                AuditVocabulary.Operations.Update,
                changedBy,
                now);
        }

        row.Enabled = enabled;
        row.Notes = notes;
        row.UpdatedAt = now;
        row.UpdatedBy = changedBy;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new TenantProductChange(Map(row), Changed: true);
    }

    private void AddAudit(
        Guid tenantId,
        Guid rowId,
        string fieldName,
        string? oldValueJson,
        string? newValueJson,
        string operation,
        Guid? changedBy,
        DateTimeOffset now)
    {
        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = AuditEntityName,
            FieldName = fieldName,
            OldValue = oldValueJson,
            NewValue = newValueJson,
            ChangedAt = now,
            ChangedBy = changedBy,
            ClientIp = _auditContext.ClientIp,
            Operation = operation,
            Result = AuditVocabulary.Results.Success,
            Module = AuditVocabulary.Modules.Companies,
            TargetEntityType = AuditTargetType,
            TargetEntityId = rowId,
        });
    }

    private static TenantProduct Map(TenantProductEntity e) =>
        new(e.TenantId, e.ProductCode, e.Enabled, e.Notes, e.UpdatedAt, e.UpdatedBy);
}
