using Flit.Admin.Application.Companies.RuntPersonOverrides;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Tramites.Application.UseCases.Consultations;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de overrides de persona RUNT por tenant (HU #14642).
/// Implementa tanto el puerto de Admin (<see cref="ITenantRuntPersonOverrideRepository"/>)
/// como el lector liviano de Trámites (<see cref="ITenantRuntPersonOverrideReader"/>).
/// </summary>
internal sealed class TenantRuntPersonOverrideRepository(FlitDbContext db)
    : ITenantRuntPersonOverrideRepository, ITenantRuntPersonOverrideReader
{
    // ── ITenantRuntPersonOverrideReader ───────────────────────────────────────

    public async Task<TenantRuntPersonOverride?> FindActiveAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        CancellationToken ct)
    {
        // Comparación normalizada: upper/trim para tipo, lower/trim para número.
        var normalizedType = documentType.ToUpperInvariant().Trim();
        var normalizedNumber = documentNumber.ToLowerInvariant().Trim();
        var now = DateTimeOffset.UtcNow;

        var entity = await db.TenantRuntPersonOverrides
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId
                && x.IsActive
                && (x.ExpiresAt == null || x.ExpiresAt > now)
                && x.DocumentType.ToUpper().Trim() == normalizedType
                && x.DocumentNumber.ToLower().Trim() == normalizedNumber)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToReaderModel(entity);
    }

    // ── ITenantRuntPersonOverrideRepository ───────────────────────────────────

    public async Task<IReadOnlyList<TenantRuntPersonOverrideView>> ListByTenantAsync(
        Guid tenantId,
        string? documentTypeFilter,
        CancellationToken ct)
    {
        var query = db.TenantRuntPersonOverrides
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(documentTypeFilter))
        {
            var normalizedFilter = documentTypeFilter.ToUpperInvariant().Trim();
            query = query.Where(x => x.DocumentType.ToUpper().Trim() == normalizedFilter);
        }

        var list = await query
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return list.Select(ToView).ToList();
    }

    public async Task<TenantRuntPersonOverrideView?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var entity = await db.TenantRuntPersonOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToView(entity);
    }

    public async Task<TenantRuntPersonOverrideView> CreateAsync(
        CreateTenantRuntPersonOverrideRequest request,
        CancellationToken ct)
    {
        // Verificar unicidad de override activo.
        var normalizedType = request.DocumentType.ToUpperInvariant().Trim();
        var normalizedNumber = request.DocumentNumber.ToLowerInvariant().Trim();
        var now = DateTimeOffset.UtcNow;

        var exists = await db.TenantRuntPersonOverrides
            .AnyAsync(x =>
                x.TenantId == request.TenantId
                && x.IsActive
                && (x.ExpiresAt == null || x.ExpiresAt > now)
                && x.DocumentType.ToUpper().Trim() == normalizedType
                && x.DocumentNumber.ToLower().Trim() == normalizedNumber,
                ct)
            .ConfigureAwait(false);

        if (exists)
            throw new DuplicateRuntOverrideException(request.TenantId, request.DocumentType, request.DocumentNumber);

        var entity = new TenantRuntPersonOverrideEntity
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            DocumentType = request.DocumentType.ToUpperInvariant().Trim(),
            DocumentNumber = request.DocumentNumber.Trim(),
            FirstName = request.FirstName.Trim().ToUpperInvariant(),
            SecondName = string.IsNullOrWhiteSpace(request.SecondName)
                ? null
                : request.SecondName.Trim().ToUpperInvariant(),
            FirstLastName = request.FirstLastName.Trim().ToUpperInvariant(),
            SecondLastName = string.IsNullOrWhiteSpace(request.SecondLastName)
                ? null
                : request.SecondLastName.Trim().ToUpperInvariant(),
            CitizenStatus = request.CitizenStatus.Trim().ToUpperInvariant(),
            DriverStatus = request.DriverStatus.Trim().ToUpperInvariant(),
            HasActiveLicense = request.HasActiveLicense,
            LicenseCategories = string.IsNullOrWhiteSpace(request.LicenseCategories)
                ? null
                : request.LicenseCategories.Trim().ToUpperInvariant(),
            HasPendingFines = request.HasPendingFines,
            NroPazYSalvo = string.IsNullOrWhiteSpace(request.NroPazYSalvo)
                ? null
                : request.NroPazYSalvo.Trim(),
            Reason = request.Reason.Trim(),
            IsActive = true,
            ExpiresAt = request.ExpiresAt,
            CreatedBy = request.RequestedBy,
            CreatedAt = now,
        };

        db.TenantRuntPersonOverrides.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToView(entity);
    }

    public async Task<TenantRuntPersonOverrideView?> UpdateAsync(
        Guid id,
        UpdateTenantRuntPersonOverrideRequest request,
        CancellationToken ct)
    {
        var entity = await db.TenantRuntPersonOverrides
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
            return null;

        if (request.FirstName is not null)
            entity.FirstName = request.FirstName.Trim().ToUpperInvariant();
        if (request.SecondName is not null)
            entity.SecondName = string.IsNullOrWhiteSpace(request.SecondName)
                ? null
                : request.SecondName.Trim().ToUpperInvariant();
        if (request.FirstLastName is not null)
            entity.FirstLastName = request.FirstLastName.Trim().ToUpperInvariant();
        if (request.SecondLastName is not null)
            entity.SecondLastName = string.IsNullOrWhiteSpace(request.SecondLastName)
                ? null
                : request.SecondLastName.Trim().ToUpperInvariant();
        if (request.CitizenStatus is not null)
            entity.CitizenStatus = request.CitizenStatus.Trim().ToUpperInvariant();
        if (request.DriverStatus is not null)
            entity.DriverStatus = request.DriverStatus.Trim().ToUpperInvariant();
        if (request.HasActiveLicense.HasValue)
            entity.HasActiveLicense = request.HasActiveLicense.Value;
        if (request.LicenseCategories is not null)
            entity.LicenseCategories = string.IsNullOrWhiteSpace(request.LicenseCategories)
                ? null
                : request.LicenseCategories.Trim().ToUpperInvariant();
        if (request.HasPendingFines.HasValue)
            entity.HasPendingFines = request.HasPendingFines.Value;
        if (request.NroPazYSalvo is not null)
            entity.NroPazYSalvo = string.IsNullOrWhiteSpace(request.NroPazYSalvo)
                ? null
                : request.NroPazYSalvo.Trim();
        if (request.Reason is not null)
            entity.Reason = request.Reason.Trim();
        if (request.IsActive.HasValue)
            entity.IsActive = request.IsActive.Value;
        if (request.ExpiresAt is not null)
            entity.ExpiresAt = request.ExpiresAt;

        entity.UpdatedBy = request.RequestedBy;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToView(entity);
    }

    // ── Mappers ───────────────────────────────────────────────────────────────

    private static TenantRuntPersonOverride ToReaderModel(TenantRuntPersonOverrideEntity e) =>
        new(
            e.Id,
            e.TenantId,
            e.DocumentType,
            e.DocumentNumber,
            e.FirstName,
            e.SecondName,
            e.FirstLastName,
            e.SecondLastName,
            e.CitizenStatus,
            e.DriverStatus,
            e.HasActiveLicense,
            e.LicenseCategories,
            e.HasPendingFines,
            e.NroPazYSalvo);

    private static TenantRuntPersonOverrideView ToView(TenantRuntPersonOverrideEntity e) =>
        new(
            e.Id,
            e.TenantId,
            e.DocumentType,
            e.DocumentNumber,
            e.FirstName,
            e.SecondName,
            e.FirstLastName,
            e.SecondLastName,
            e.CitizenStatus,
            e.DriverStatus,
            e.HasActiveLicense,
            e.LicenseCategories,
            e.HasPendingFines,
            e.NroPazYSalvo,
            e.Reason,
            e.IsActive,
            e.ExpiresAt,
            e.CreatedBy,
            e.CreatedAt,
            e.UpdatedBy,
            e.UpdatedAt);
}
