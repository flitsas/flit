using Flit.Admin.Domain.Improntas;
using Flit.Infrastructure.Persistence.Entities.Admin;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de historial de improntas (HU #10466 / ADR-0022). A diferencia de las tablas
/// <c>admin.*</c> con RLS (ver <see cref="OtApiCallLogRepository"/>), <c>admin.impronta_generations</c>
/// NO tiene política <c>tenant_isolation</c> (dispensa documentada de A10: log global de auditoría
/// SuperAdmin) — no se fija <c>app.current_tenant_id</c> antes de escribir.
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
}
