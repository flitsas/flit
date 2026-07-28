using System.Text.Json;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Escrituras EF Core de mandatarios (ADR-0023). Cada operación persiste el mandatario, la
/// reasignación de compañías y su fila de auditoría en <c>admin.tenant_config_audit_logs</c>
/// dentro de una única transacción (todo o nada), fijando <c>app.current_tenant_id</c> al
/// tenant del OT con <c>set_config(..., is_local := true)</c> —igual que
/// <see cref="TransitGrantRepository"/>—. La auditoría <b>no</b> registra el número de
/// documento (PII, Ley 1581): solo id, huella de integridad y compañías.
///
/// La exclusividad (OT, compañía) → un mandatario activo la valida el handler antes; el índice
/// único parcial <c>uq_mandate_signer_companies_active</c> es el guardián último en BD.
/// </summary>
internal sealed class MandateSignerRepository : IMandateSignerRepository
{
    private const string EntityName = "mandate_signer";

    private readonly FlitDbContext _context;

    public MandateSignerRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<Guid> CreateAsync(
        CreateMandateSignerData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ExecuteInTenantScopeAsync(
            data.OtTenantId,
            () => PersistCreateAsync(data, cancellationToken),
            cancellationToken);
    }

    public Task<bool> UpdateAsync(
        UpdateMandateSignerData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ExecuteInTenantScopeAsync(
            data.OtTenantId,
            () => PersistUpdateAsync(data, cancellationToken),
            cancellationToken);
    }

    public Task<bool> InactivateAsync(
        InactivateMandateSignerData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ExecuteInTenantScopeAsync(
            data.OtTenantId,
            () => PersistInactivateAsync(data, cancellationToken),
            cancellationToken);
    }

    public Task<bool> ReactivateAsync(
        ReactivateMandateSignerData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ExecuteInTenantScopeAsync(
            data.OtTenantId,
            () => PersistReactivateAsync(data, cancellationToken),
            cancellationToken);
    }

    private async Task<Guid> PersistCreateAsync(
        CreateMandateSignerData data,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var signerId = Guid.NewGuid();

        _context.MandateSigners.Add(new MandateSigner
        {
            Id = signerId,
            TransitOfficeId = data.TransitOfficeId,
            FullName = data.FullName,
            DocumentType = data.DocumentType,
            DocumentNumber = data.DocumentNumber,
            IntegrityHash = data.IntegrityHash,
            Email = data.Email,
            UserId = data.UserId,
            RegisteredAt = data.RegisteredAt,
            IsActive = true,
            CreatedAt = now,
            CreatedBy = data.CreatedBy,
        });

        foreach (var companyId in Distinct(data.CompanyTenantIds))
        {
            _context.MandateSignerCompanies.Add(NewAssignment(signerId, data.TransitOfficeId, companyId, now));
        }

        AddAudit(
            data.OtTenantId,
            fieldName: "created",
            oldValue: null,
            newValue: AuditPayload(signerId, data.IntegrityHash, data.CompanyTenantIds),
            changedAt: now,
            changedBy: data.CreatedBy,
            correlationId: data.CorrelationId);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return signerId;
    }

    private async Task<bool> PersistUpdateAsync(
        UpdateMandateSignerData data,
        CancellationToken cancellationToken)
    {
        var signer = await _context.MandateSigners
            .FirstOrDefaultAsync(s => s.Id == data.MandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        if (signer is null || !signer.IsActive)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        var currentAssignments = await _context.MandateSignerCompanies
            .Where(c => c.MandateSignerId == signer.Id && c.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var oldHash = signer.IntegrityHash;
        var oldCompanyIds = currentAssignments.Select(c => c.CompanyTenantId).ToList();

        signer.FullName = data.FullName;
        signer.DocumentType = data.DocumentType;
        signer.DocumentNumber = data.DocumentNumber;
        signer.IntegrityHash = data.IntegrityHash;
        signer.Email = data.Email;
        signer.UserId = data.UserId;
        signer.UpdatedAt = now;
        signer.UpdatedBy = data.UpdatedBy;

        var desired = Distinct(data.CompanyTenantIds).ToHashSet();

        // Baja lógica de las compañías retiradas del conjunto (liberan la exclusividad).
        foreach (var assignment in currentAssignments.Where(c => !desired.Contains(c.CompanyTenantId)))
        {
            assignment.IsActive = false;
        }

        // Alta de las compañías nuevas.
        var currentActive = currentAssignments
            .Where(c => c.IsActive)
            .Select(c => c.CompanyTenantId)
            .ToHashSet();

        foreach (var companyId in desired.Where(id => !currentActive.Contains(id)))
        {
            _context.MandateSignerCompanies.Add(NewAssignment(signer.Id, signer.TransitOfficeId, companyId, now));
        }

        AddAudit(
            data.OtTenantId,
            fieldName: "updated",
            oldValue: AuditPayload(signer.Id, oldHash, oldCompanyIds),
            newValue: AuditPayload(signer.Id, data.IntegrityHash, data.CompanyTenantIds),
            changedAt: now,
            changedBy: data.UpdatedBy,
            correlationId: data.CorrelationId);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> PersistInactivateAsync(
        InactivateMandateSignerData data,
        CancellationToken cancellationToken)
    {
        var signer = await _context.MandateSigners
            .FirstOrDefaultAsync(s => s.Id == data.MandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        // Idempotente: 404 si no existe o ya estaba inactivo.
        if (signer is null || !signer.IsActive)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        signer.IsActive = false;
        signer.UpdatedAt = now;
        signer.UpdatedBy = data.ChangedBy;

        // Libera las compañías: sus filas dejan de contar para el índice de exclusividad.
        var assignments = await _context.MandateSignerCompanies
            .Where(c => c.MandateSignerId == signer.Id && c.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var assignment in assignments)
        {
            assignment.IsActive = false;
        }

        AddAudit(
            data.OtTenantId,
            fieldName: "is_active",
            oldValue: JsonSerializer.Serialize(true),
            newValue: JsonSerializer.Serialize(false),
            changedAt: now,
            changedBy: data.ChangedBy,
            correlationId: data.CorrelationId);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> PersistReactivateAsync(
        ReactivateMandateSignerData data,
        CancellationToken cancellationToken)
    {
        var signer = await _context.MandateSigners
            .FirstOrDefaultAsync(s => s.Id == data.MandateSignerId, cancellationToken)
            .ConfigureAwait(false);

        // Idempotente: 404 si no existe o ya estaba activo.
        if (signer is null || signer.IsActive)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        // Vuelve activo SIN restaurar compañías: las liberadas al inactivar se reasignan a mano.
        signer.IsActive = true;
        signer.UpdatedAt = now;
        signer.UpdatedBy = data.ChangedBy;

        AddAudit(
            data.OtTenantId,
            fieldName: "is_active",
            oldValue: JsonSerializer.Serialize(false),
            newValue: JsonSerializer.Serialize(true),
            changedAt: now,
            changedBy: data.ChangedBy,
            correlationId: data.CorrelationId);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void AddAudit(
        Guid otTenantId,
        string fieldName,
        string? oldValue,
        string? newValue,
        DateTimeOffset changedAt,
        Guid? changedBy,
        Guid? correlationId)
    {
        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = otTenantId,
            EntityName = EntityName,
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = changedAt,
            ChangedBy = changedBy,
            CorrelationId = correlationId,
        });
    }

    private static MandateSignerCompany NewAssignment(
        Guid signerId, Guid transitOfficeId, Guid companyTenantId, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            MandateSignerId = signerId,
            TransitOfficeId = transitOfficeId,
            CompanyTenantId = companyTenantId,
            IsActive = true,
            CreatedAt = now,
        };

    /// <summary>Payload de auditoría sin PII: id, huella de integridad y compañías.</summary>
    private static string AuditPayload(Guid signerId, string integrityHash, IReadOnlyList<Guid> companyTenantIds) =>
        JsonSerializer.Serialize(new
        {
            mandateSignerId = signerId,
            integrityHash,
            companyTenantIds = Distinct(companyTenantIds),
        });

    private static IReadOnlyList<Guid> Distinct(IReadOnlyList<Guid> ids) =>
        ids is null ? [] : [.. ids.Distinct()];

    /// <summary>
    /// Ejecuta <paramref name="persist"/> bajo el contexto RLS del tenant OT. En proveedor
    /// relacional abre transacción + <c>set_config</c>; en InMemory delega directo (un único
    /// <c>SaveChanges</c> atómico).
    /// </summary>
    private async Task<T> ExecuteInTenantScopeAsync<T>(
        Guid otTenantId,
        Func<Task<T>> persist,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {otTenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await persist().ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await persist().ConfigureAwait(false);
    }
}
