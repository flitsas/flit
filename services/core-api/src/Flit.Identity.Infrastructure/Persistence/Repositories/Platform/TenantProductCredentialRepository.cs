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
    /// <summary>HU #14711 — campo de la fila resumen del alta del proveedor (motivo CREDENCIAL_OBTENIDA o CLAVE_ROTADA).</summary>
    internal const string CampoAltaProveedor = "impuesto_alta_proveedor";

    /// <summary>HU #14711 — operación de la fila resumen del alta del proveedor (propia de este rastro, como la lectura para copia).</summary>
    internal const string OperacionAltaProveedor = "obtain_from_provider";
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
            AddAudit(tenantId, row.Id, credentialCode, null, ValorConfigurada, AuditVocabulary.Operations.Create, changedBy, now);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return TenantProductCredentialWrite.Created;
        }

        row.SecretProtected = secretProtected;
        row.UpdatedAt = now;
        row.UpdatedBy = changedBy;
        AddAudit(tenantId, row.Id, credentialCode, ValorConfigurada, ValorReemplazada, AuditVocabulary.Operations.Update, changedBy, now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return TenantProductCredentialWrite.Replaced;
    }

    /// <summary>
    /// HU #14694 (M04b) — reemplaza solo si la fila existe; la auditoría (configurada a reemplazada, operación update, el cliente
    /// de servicio en <c>user_agent</c>) va en el mismo guardado. Sin fila no escribe nada.
    /// </summary>
    public async Task<bool> ReplaceExistingFromServiceAsync(
        Guid tenantId,
        string productCode,
        string credentialCode,
        string secretProtected,
        string servicio,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretProtected);
        ArgumentException.ThrowIfNullOrWhiteSpace(servicio);

        var row = await _context.Set<TenantProductCredentialEntity>()
            .FirstOrDefaultAsync(
                r => r.TenantId == tenantId && r.ProductCode == productCode && r.CredentialCode == credentialCode,
                cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
            return false;

        var now = _clock.GetUtcNow();
        row.SecretProtected = secretProtected;
        row.UpdatedAt = now;
        row.UpdatedBy = null;
        AddAudit(tenantId, row.Id, credentialCode, ValorConfigurada, ValorReemplazada, AuditVocabulary.Operations.Update, null, now, servicio);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// HU #14711 — las credenciales del alta del proveedor en UN <c>SaveChanges</c>: crea o reemplaza cada fila (todas con el
    /// mismo <c>updated_at</c>; el del secreto anterior marca el inicio de su ventana de 24 h), una auditoría por fila con su
    /// código real y una fila resumen (<see cref="CampoAltaProveedor"/>) con el motivo. <paramref name="antesDeGuardar"/> encola
    /// en el mismo contexto. Sin valores ni huellas en la auditoría.
    /// </summary>
    public async Task<DateTimeOffset> GuardarAltaProveedorAsync(
        Guid tenantId,
        string productCode,
        IReadOnlyList<EscrituraCredencialAlta> escrituras,
        string motivo,
        Guid? changedBy,
        Action<DateTimeOffset>? antesDeGuardar,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);
        ArgumentNullException.ThrowIfNull(escrituras);
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        if (escrituras.Count == 0)
            throw new ArgumentException("El alta no trae credenciales que guardar.", nameof(escrituras));

        var codigos = escrituras.Select(e => e.CredentialCode).ToList();
        var filas = await _context.Set<TenantProductCredentialEntity>()
            .Where(r => r.TenantId == tenantId && r.ProductCode == productCode && codigos.Contains(r.CredentialCode))
            .ToDictionaryAsync(r => r.CredentialCode, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        foreach (var escritura in escrituras)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(escritura.CredentialCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(escritura.SecretProtected);
            if (filas.TryGetValue(escritura.CredentialCode, out var row))
            {
                row.SecretProtected = escritura.SecretProtected;
                row.UpdatedAt = now;
                row.UpdatedBy = changedBy;
                AddAudit(tenantId, row.Id, row.CredentialCode, ValorConfigurada, ValorReemplazada, AuditVocabulary.Operations.Update, changedBy, now);
                continue;
            }

            row = new TenantProductCredentialEntity
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                ProductCode = productCode,
                CredentialCode = escritura.CredentialCode,
                SecretProtected = escritura.SecretProtected,
                CreatedAt = now,
                CreatedBy = changedBy,
                UpdatedAt = now,
                UpdatedBy = changedBy,
            };
            _context.Set<TenantProductCredentialEntity>().Add(row);
            AddAudit(tenantId, row.Id, row.CredentialCode, null, ValorConfigurada, AuditVocabulary.Operations.Create, changedBy, now);
        }

        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = AuditEntityName,
            FieldName = CampoAltaProveedor,
            OldValue = null,
            NewValue = JsonSerializer.Serialize(motivo),
            ChangedAt = now,
            ChangedBy = changedBy,
            ClientIp = _auditContext.ClientIp,
            Operation = OperacionAltaProveedor,
            Result = AuditVocabulary.Results.Success,
            Module = AuditVocabulary.Modules.Companies,
            TargetEntityType = AuditTargetType,
            TargetEntityId = null,
        });

        antesDeGuardar?.Invoke(now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return now;
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
        AddAudit(tenantId, row.Id, credentialCode, ValorConfigurada, ValorNoConfigurada, AuditVocabulary.Operations.Delete, changedBy, now);
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
        string credentialCode,
        string? oldValue,
        string newValue,
        string operation,
        Guid? changedBy,
        DateTimeOffset now,
        string? servicio = null)
    {
        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = AuditEntityName,
            FieldName = credentialCode,
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
            UserAgent = servicio,
        });
    }

    private static TenantProductCredential Map(TenantProductCredentialEntity e) =>
        new(e.TenantId, e.ProductCode, e.CredentialCode, e.SecretProtected, e.RowVersion, e.UpdatedAt);
}
