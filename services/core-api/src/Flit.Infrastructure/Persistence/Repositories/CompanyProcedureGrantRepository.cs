using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.ProcedureGrants;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de los tipos de trámite habilitados por compañía (FEATURE-08, grant model).
/// Calca <see cref="TransitGrantRepository"/>: RLS cross-tenant de SuperAdmin fijando
/// <c>app.current_tenant_id</c> al tenant destino dentro de la transacción de escritura
/// (parametrizado, <c>is_local := true</c>), y auditoría en la misma transacción. Las lecturas usan el
/// owner-bypass del rol de core-api + <c>WHERE tenant_id</c> explícito.
/// </summary>
internal sealed class CompanyProcedureGrantRepository : ICompanyProcedureGrantRepository
{
    private const string EntityName = "company_procedure_type_grants";
    private const string FieldName = "procedure_type_id";

    private readonly FlitDbContext _context;
    private readonly IAuditContextAccessor _auditContext;

    public CompanyProcedureGrantRepository(FlitDbContext context, IAuditContextAccessor auditContext)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditContext = auditContext ?? throw new ArgumentNullException(nameof(auditContext));
    }

    public async Task<IReadOnlyList<Guid>> ListEnabledProcedureTypeIdsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CompanyProcedureTypeGrants
            .AsNoTracking()
            .Where(g => g.TenantId == tenantId && g.IsEnabled)
            .OrderBy(g => g.CreatedAt)
            .Select(g => g.ProcedureTypeId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<bool> AddGrantAsync(
        Guid tenantId,
        Guid procedureTypeId,
        Guid? createdBy,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            () => PersistAddAsync(tenantId, procedureTypeId, createdBy, cancellationToken),
            cancellationToken);

    public Task<bool> RemoveGrantAsync(
        Guid tenantId,
        Guid procedureTypeId,
        Guid? changedBy,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            () => PersistRemoveAsync(tenantId, procedureTypeId, changedBy, cancellationToken),
            cancellationToken);

    private async Task<bool> ExecuteInTenantScopeAsync(
        Guid tenantId,
        Func<Task<bool>> persist,
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
                    // RLS: habilita el acceso cross-tenant solo para esta transacción.
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await persist().ConfigureAwait(false);

                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        // Proveedor no relacional (InMemory): un solo SaveChanges atómico.
        return await persist().ConfigureAwait(false);
    }

    private async Task<bool> PersistAddAsync(
        Guid tenantId,
        Guid procedureTypeId,
        Guid? createdBy,
        CancellationToken cancellationToken)
    {
        var alreadyExists = await _context.CompanyProcedureTypeGrants
            .AnyAsync(g => g.TenantId == tenantId && g.ProcedureTypeId == procedureTypeId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyExists)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        _context.CompanyProcedureTypeGrants.Add(new CompanyProcedureTypeGrant
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ProcedureTypeId = procedureTypeId,
            IsEnabled = true,
            CreatedAt = now,
            CreatedBy = createdBy,
        });

        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = EntityName,
            FieldName = FieldName,
            OldValue = null,
            NewValue = JsonSerializer.Serialize(procedureTypeId),
            ChangedAt = now,
            ChangedBy = createdBy,
            CorrelationId = null,
            ClientIp = _auditContext.ClientIp,
            Operation = AuditVocabulary.Operations.Create,
            Result = AuditVocabulary.Results.Success,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> PersistRemoveAsync(
        Guid tenantId,
        Guid procedureTypeId,
        Guid? changedBy,
        CancellationToken cancellationToken)
    {
        var grant = await _context.CompanyProcedureTypeGrants
            .FirstOrDefaultAsync(
                g => g.TenantId == tenantId && g.ProcedureTypeId == procedureTypeId, cancellationToken)
            .ConfigureAwait(false);

        if (grant is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;

        _context.CompanyProcedureTypeGrants.Remove(grant);

        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = EntityName,
            FieldName = FieldName,
            OldValue = JsonSerializer.Serialize(procedureTypeId),
            NewValue = null,
            ChangedAt = now,
            ChangedBy = changedBy,
            CorrelationId = null,
            ClientIp = _auditContext.ClientIp,
            Operation = AuditVocabulary.Operations.Delete,
            Result = AuditVocabulary.Results.Success,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
