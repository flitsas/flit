using Flit.Admin.Domain.Companies.SignatureVault;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Escrituras EF Core del baúl de firmas (HU #10642, ADR-0025). El baúl es tenant-scoped: cada
/// operación fija <c>app.current_tenant_id</c> con <c>set_config(..., is_local := true)</c> —igual
/// que <see cref="OtRequirementsRepository"/>/<see cref="MandateSignerRepository"/>— para que la
/// política RLS <c>tenant_isolation</c> acote las filas. La exclusividad "una 'activa' por (tenant,
/// NIT, documento)" la garantiza en última instancia el índice único parcial de BD.
/// <c>DocumentNumber</c> es PII (Ley 1581): no se registra en logs.
/// </summary>
internal sealed class SignatureVaultRepository : ISignatureVaultRepository
{
    private readonly FlitDbContext _context;

    public SignatureVaultRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<Guid> CreateAsync(
        CreateSignatureVaultData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ExecuteInTenantScopeAsync(
            data.TenantId,
            () => PersistCreateAsync(data, cancellationToken),
            cancellationToken);
    }

    public Task<bool> RevokeAsync(
        RevokeSignatureVaultData data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ExecuteInTenantScopeAsync(
            data.TenantId,
            () => PersistRevokeAsync(data, cancellationToken),
            cancellationToken);
    }

    private async Task<Guid> PersistCreateAsync(
        CreateSignatureVaultData data,
        CancellationToken cancellationToken)
    {
        // Valida invariantes de alta (vigencia, obligatorios) vía el agregado antes de persistir.
        var aggregate = SignatureVault.Create(
            data.TenantId,
            data.DocumentType,
            data.DocumentNumber,
            data.NitEmpresa,
            data.FullName,
            data.SignatureHash,
            data.StoragePath,
            data.StorageSha256,
            data.VigenciaDesde,
            data.VigenciaHasta,
            data.MandateSignerId);

        var now = DateTimeOffset.UtcNow;
        var entity = new SignatureVaultEntity
        {
            Id = aggregate.Id,
            TenantId = aggregate.TenantId,
            DocumentType = aggregate.DocumentType,
            DocumentNumber = aggregate.DocumentNumber,
            NitEmpresa = aggregate.NitEmpresa,
            FullName = aggregate.FullName,
            SignatureHash = aggregate.SignatureHash,
            StoragePath = aggregate.StoragePath,
            StorageSha256 = aggregate.StorageSha256,
            Estado = SignatureVaultEstadoMapping.ToDb(aggregate.Estado),
            VigenciaDesde = aggregate.VigenciaDesde,
            VigenciaHasta = aggregate.VigenciaHasta,
            MandateSignerId = aggregate.MandateSignerId,
            CreatedAt = now,
            CreatedBy = data.CreatedBy,
        };

        _context.SignatureVault.Add(entity);
        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsActiveUniqueViolation(ex))
        {
            // Índice único parcial uq_signature_vault_activa: a lo sumo UNA firma 'activa' por
            // (tenant, NIT, documento). Se traduce el 23505 a una excepción de dominio (checklist
            // §B12); el handler la mapea a 422 (firma_activa_existente). Sin PII en el mensaje.
            throw new SignatureVaultActiveConflictException();
        }

        return entity.Id;
    }

    private const string ActiveUniqueIndex = "uq_signature_vault_activa";

    private static bool IsActiveUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation
        && pg.ConstraintName == ActiveUniqueIndex;

    private async Task<bool> PersistRevokeAsync(
        RevokeSignatureVaultData data,
        CancellationToken cancellationToken)
    {
        var entity = await _context.SignatureVault
            .FirstOrDefaultAsync(s => s.Id == data.Id, cancellationToken)
            .ConfigureAwait(false);

        // Idempotente: false si no existe o ya estaba revocada.
        if (entity is null || entity.Estado != SignatureVaultEstadoMapping.Activa)
        {
            return false;
        }

        var aggregate = SignatureVaultEstadoMapping.Rehydrate(entity);
        aggregate.Revocar();

        entity.Estado = SignatureVaultEstadoMapping.ToDb(aggregate.Estado);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = data.ChangedBy;

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Ejecuta <paramref name="persist"/> bajo el contexto RLS del tenant. En proveedor relacional
    /// abre transacción + <c>set_config</c>; en InMemory delega directo.
    /// </summary>
    private async Task<T> ExecuteInTenantScopeAsync<T>(
        Guid tenantId,
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
                        $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
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
