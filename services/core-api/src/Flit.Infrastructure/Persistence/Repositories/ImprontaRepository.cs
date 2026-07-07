using Flit.Admin.Domain.Common;
using Flit.Admin.Domain.Improntas;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de historial de improntas (HU #10466/#10468 / ADR-0022). A diferencia de las
/// tablas <c>admin.*</c> con RLS (ver <see cref="OtApiCallLogRepository"/>),
/// <c>admin.impronta_generations</c> NO tiene política <c>tenant_isolation</c> (dispensa
/// documentada de A10: log global de auditoría SuperAdmin) — no se fija
/// <c>app.current_tenant_id</c> ni se necesita <c>SET LOCAL row_security = off</c> (la tabla no
/// tiene RLS habilitado en absoluto, a diferencia de <c>AnalyticsReadRepository</c>/
/// <c>TransitOfficeTenantWriteRepository</c> que sí leen tablas con RLS).
/// </summary>
internal sealed class ImprontaRepository : IImprontaRepository
{
    private readonly FlitDbContext _context;

    public ImprontaRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task SaveAsync(ImprontaGeneration generation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generation);

        var entity = new ImprontaGenerationEntity
        {
            Id = generation.Id == Guid.Empty ? Guid.NewGuid() : generation.Id,
            TenantId = generation.TenantId,
            FlitUserId = generation.FlitUserId,
            Radicado = generation.Radicado,
            HashSha256 = generation.HashSha256,
            FechaImpresa = generation.FechaImpresa,
            Placa = generation.Placa,
            NumMotor = generation.NumMotor,
            NumChasis = generation.NumChasis,
            NumSerie = generation.NumSerie,
            Marca = generation.Marca,
            Linea = generation.Linea,
            Modelo = generation.Modelo,
            OrgNombre = generation.OrgNombre,
            OrgNit = generation.OrgNit,
            OrgCiudad = generation.OrgCiudad,
            Operador = generation.Operador,
            PdfContent = generation.PdfContent,
            PdfSizeBytes = generation.PdfSizeBytes,
            CreatedAt = generation.CreatedAt == default ? DateTimeOffset.UtcNow : generation.CreatedAt,
        };

        _context.ImprontaGenerations.Add(entity);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<PagedResult<ImprontaGenerationListItem>> ListAsync(
        ImprontaGenerationFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = _context.ImprontaGenerations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Placa))
        {
            // ToLower() (no ToLowerInvariant) para que Npgsql lo traduzca a lower() en SQL;
            // ToLowerInvariant no es traducible y rompe en PostgreSQL real.
            var placa = filter.Placa.Trim().ToLower();
            query = query.Where(x => x.Placa.ToLower().Contains(placa));
        }

        if (!string.IsNullOrWhiteSpace(filter.Radicado))
        {
            var radicado = filter.Radicado.Trim().ToLower();
            query = query.Where(x => x.Radicado.ToLower().Contains(radicado));
        }

        if (filter.CreatedFrom is { } createdFrom)
        {
            query = query.Where(x => x.CreatedAt >= createdFrom);
        }

        if (filter.CreatedTo is { } createdTo)
        {
            query = query.Where(x => x.CreatedAt <= createdTo);
        }

        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        if (totalCount == 0)
        {
            return PagedResult<ImprontaGenerationListItem>.Empty;
        }

        // Proyección explícita: nunca seleccionar PdfContent (ADR-0022 — disciplina de
        // proyección obligatoria para no arrastrar el binario en cada página del listado).
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(x => new ImprontaGenerationListItem
            {
                Id = x.Id,
                TenantId = x.TenantId,
                FlitUserId = x.FlitUserId,
                Radicado = x.Radicado,
                HashSha256 = x.HashSha256,
                FechaImpresa = x.FechaImpresa,
                Placa = x.Placa,
                NumMotor = x.NumMotor,
                NumChasis = x.NumChasis,
                NumSerie = x.NumSerie,
                Marca = x.Marca,
                Linea = x.Linea,
                Modelo = x.Modelo,
                OrgNombre = x.OrgNombre,
                OrgNit = x.OrgNit,
                OrgCiudad = x.OrgCiudad,
                Operador = x.Operador,
                PdfSizeBytes = x.PdfSizeBytes,
                CreatedAt = x.CreatedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<ImprontaGenerationListItem>(items, totalCount);
    }
}
