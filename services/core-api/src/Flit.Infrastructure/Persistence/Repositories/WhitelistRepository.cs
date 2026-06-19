using System.Text.Json;
using Flit.Admin.Domain.Companies.Whitelist;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de la lista blanca de correos exentos (HU #10191, RF05).
///
/// RLS: <c>admin.tenant_whitelist_users</c> y <c>admin.tenant_config_audit_logs</c>
/// están aisladas por <c>app.current_tenant_id</c>. Para la operación cross-tenant
/// de SuperAdmin se fija ese GUC <em>dentro</em> de la transacción con
/// <c>set_config(..., is_local := true)</c> (parametrizado, sin concatenar SQL)
/// antes de escribir, y se revierte al cerrar la transacción.
///
/// Atomicidad (AC4): el alta de los correos y la inserción de sus filas de auditoría
/// ocurren en una única transacción (todo o nada). En proveedores no relacionales
/// (InMemory, tests) se omite la transacción explícita y el GUC, preservando el
/// comportamiento de un único <c>SaveChanges</c>.
/// </summary>
internal sealed class WhitelistRepository : IWhitelistRepository
{
    private const string EntityName = "tenant_whitelist_users";
    private const string FieldName = "email";

    private readonly FlitDbContext _context;

    public WhitelistRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<bool> IsEmailWhitelistedAsync(
        Guid tenantId,
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        var normalized = WhitelistEmail.Normalize(email);

        return await _context.TenantWhitelistUsers
            .AsNoTracking()
            .AnyAsync(w => w.TenantId == tenantId && w.Email == normalized, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TenantWhitelistEntry>> ListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _context.TenantWhitelistUsers
            .AsNoTracking()
            .Where(w => w.TenantId == tenantId)
            .OrderBy(w => w.CreatedAt)
            .Select(w => new TenantWhitelistEntry(w.Email, w.CreatedAt, w.AddedBy))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<WhitelistAddOutcome> AddEmailsAsync(
        Guid tenantId,
        IReadOnlyList<string> normalizedEmails,
        Guid? addedBy,
        Guid? correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(normalizedEmails);

        if (_context.Database.IsRelational())
        {
            var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                // RLS: habilita el acceso cross-tenant solo para esta transacción.
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                    cancellationToken).ConfigureAwait(false);

                var outcome = await PersistAsync(tenantId, normalizedEmails, addedBy, correlationId, cancellationToken)
                    .ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return outcome;
            }
        }

        // Proveedor no relacional (InMemory): un solo SaveChanges atómico.
        return await PersistAsync(tenantId, normalizedEmails, addedBy, correlationId, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<WhitelistAddOutcome> PersistAsync(
        Guid tenantId,
        IReadOnlyList<string> normalizedEmails,
        Guid? addedBy,
        Guid? correlationId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var existing = await _context.TenantWhitelistUsers
            .Where(w => w.TenantId == tenantId)
            .Select(w => w.Email)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var known = new HashSet<string>(existing, StringComparer.Ordinal);

        var added = new List<string>();
        var skipped = new List<string>();

        foreach (var email in normalizedEmails)
        {
            // Idempotencia: omite duplicados ya existentes sin error (AC4, 201 parcial).
            if (!known.Add(email))
            {
                skipped.Add(email);
                continue;
            }

            _context.TenantWhitelistUsers.Add(new TenantWhitelistUser
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Email = email,
                AddedBy = addedBy,
                CreatedBy = addedBy,
                CreatedAt = now,
            });

            _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EntityName = EntityName,
                FieldName = FieldName,
                OldValue = null,
                NewValue = JsonSerializer.Serialize(email),
                ChangedAt = now,
                ChangedBy = addedBy,
                CorrelationId = correlationId,
            });

            added.Add(email);
        }

        if (added.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new WhitelistAddOutcome(added, skipped);
    }
}
