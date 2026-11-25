using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Modules.Platform.Domain.TenantProducts;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories.Platform;

/// <summary>
/// Credenciales de producto por empresa (ADR-0092). Audita en
/// <c>admin.tenant_config_audit_logs</c> con valores legibles
/// <c>configurada</c> / <c>reemplazada</c> / <c>no_configurada</c>; nunca la llave ni una huella.
/// </summary>
internal sealed class TenantProductCredentialRepository : ITenantProductCredentialRepository
{
    private const string AuditEntityName = "TenantProductCredential";
    private const string AuditTargetType = "TENANT_PRODUCT_CREDENTIAL";
    private const string FieldName = "impuesto_api_key";
    private const string ValorConfigurada = "configurada";
    private const string ValorReemplazada = "reemplazada";
    private const string ValorNoConfigurada = "no_configurada";
    private const string ValorLeidaParaCopia = "leida_para_copia";

    /// <summary>HU #14694 — operación de la lectura para copia (no está en <c>AuditVocabulary</c>: es propia de este rastro).</summary>
    internal const string OperacionLecturaParaCopia = "read_for_copy";

    private readonly IIdentityDb _context;
    private readonly IAuditContextAccessor _auditContext;
    private readonly TimeProvider _clock;

    public TenantProductCredentialRepository(IIdentityDb context, IAuditContextAccessor? auditContext = null, TimeProvider? clock = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditContext = auditContext ?? NullAuditContextAccessor.Instance;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<TenantProductCredential?> GetAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialCode);

        var row = await _context.Set<TenantProductCredentialEntity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.ProductCode == productCode && r.CredentialCode == credentialCode,
                cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : Map(row);
    }

    public async Task<bool> ExistsAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialCode);

        return await _context.Set<TenantProductCredentialEntity>()
            .AsNoTracking()
            .AnyAsync(
                r => r.TenantId == tenantId && r.ProductCode == productCode && r.CredentialCode == credentialCode,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TenantProductCredentialWrite> SetAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        string secretProtected,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretProtected);

        var row = await _context.Set<TenantProductCredentialEntity>()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.ProductCode == productCode && r.CredentialCode == credentialCode,
                cancellationToken)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();

        if (row is null)
        {
            row = new TenantProductCredentialEntity
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                ProductCode = productCode,
                CredentialCode = credentialCode,
                SecretProtected = secretProtected,
                CreatedAt = now,
                CreatedBy = changedBy,
                UpdatedAt = now,
                UpdatedBy = changedBy,
            };
            _context.Set<TenantProductCredentialEntity>().Add(row);
            AddAudit(tenantId, row.Id, null, ValorConfigurada, AuditVocabulary.Operations.Create, changedBy, now);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return TenantProductCredentialWrite.Created;
        }

        row.SecretProtected = secretProtected;
        row.UpdatedAt = now;
        row.UpdatedBy = changedBy;
        AddAudit(tenantId, row.Id, ValorConfigurada, ValorReemplazada, AuditVocabulary.Operations.Update, changedBy, now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TenantProductCredentialWrite.Replaced;
    }

    public async Task<bool> DeleteAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialCode);

        var row = await _context.Set<TenantProductCredentialEntity>()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.ProductCode == productCode && r.CredentialCode == credentialCode,
                cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
            return false;

        var now = _clock.GetUtcNow();
        AddAudit(tenantId, row.Id, ValorConfigurada, ValorNoConfigurada, AuditVocabulary.Operations.Delete, changedBy, now);
        _context.Set<TenantProductCredentialEntity>().Remove(row);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// HU #14694 (D-CRED-DIAG) — una fila por lectura para copia, en su propio guardado: operación
    /// <see cref="OperacionLecturaParaCopia"/>, valor legible (<c>leida_para_copia</c> o <c>no_configurada</c>) y el cliente
    /// de servicio en <c>user_agent</c>. Nunca la llave ni una huella.
    /// </summary>
    public async Task RegistrarLecturaParaCopiaAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        string lector,
        bool existe,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(lector);

        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = AuditEntityName,
            FieldName = credentialCode,
            OldValue = null,
            NewValue = JsonSerializer.Serialize(existe ? ValorLeidaParaCopia : ValorNoConfigurada),
            ChangedAt = _clock.GetUtcNow(),
            ChangedBy = null,
            ClientIp = _auditContext.ClientIp,
            Operation = OperacionLecturaParaCopia,
            Result = AuditVocabulary.Results.Success,
            Module = AuditVocabulary.Modules.Companies,
            TargetEntityType = AuditTargetType,
            TargetEntityId = null,
            UserAgent = lector,
        });
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void AddAudit(
        Guid tenantId,
        Guid rowId,
        string? oldValue,
        string newValue,
        string operation,
        Guid? changedBy,
        DateTimeOffset now)
    {
        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = AuditEntityName,
            FieldName = FieldName,
            OldValue = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValue = JsonSerializer.Serialize(newValue),
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

    private static TenantProductCredential Map(TenantProductCredentialEntity e) =>
        new(e.TenantId, e.ProductCode, e.CredentialCode, e.SecretProtected, e.RowVersion, e.UpdatedAt);
}
