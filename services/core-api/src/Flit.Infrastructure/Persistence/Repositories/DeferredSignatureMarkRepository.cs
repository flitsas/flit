using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #11196 — persistencia de las marcas de firma a posteriori. Todas las consultas filtran por
/// <c>tenant_id</c> además de la RLS de la tabla: el AC5 (los trámites de otra empresa gestora quedan
/// fuera del lote) descansa en ese filtro.
/// </summary>
internal sealed class DeferredSignatureMarkRepository(FlitDbContext db) : IDeferredSignatureMarkRepository
{
    public Task<DeferredSignatureMark?> FindPendienteAsync(
        Guid tenantId,
        Guid procedureInstanceId,
        string partyRole,
        string? representativeDocumentNumber = null,
        CancellationToken cancellationToken = default)
    {
        // ADR-0053 (Múltiple Propietario) — sin el filtro por documento, dos actores jurídicos del mismo
        // rol (dos copropietarios) comparten (tenant, trámite, rol) y la consulta devolvía SIEMPRE la
        // primera marca pendiente que encontrara, sin importar a cuál representante pertenecía: la marca
        // de un copropietario se leía/pisaba con la del otro. Sin documento (mandatario, que no es un
        // actor y siempre es único por trámite) se preserva la búsqueda solo por rol.
        var documento = string.IsNullOrWhiteSpace(representativeDocumentNumber)
            ? null
            : representativeDocumentNumber.Trim();
        return db.DeferredSignatureMarks.FirstOrDefaultAsync(
            m => m.TenantId == tenantId
                && m.ProcedureInstanceId == procedureInstanceId
                && m.PartyRole == partyRole
                && (documento == null || m.RepresentativeDocumentNumber == documento)
                && m.Estado == DeferredSignatureEstados.Pendiente,
            cancellationToken);
    }

    public async Task<IReadOnlyList<DeferredSignatureMark>> ListPendientesByRepresentativeAsync(
        Guid tenantId,
        string representativeDocumentType,
        string representativeDocumentNumber,
        CancellationToken cancellationToken = default)
    {
        var tipo = representativeDocumentType.Trim();
        var documento = representativeDocumentNumber.Trim();

        // Orden determinista de creación: el lote se aplica siempre en el mismo orden, así la traza de
        // una corrida se puede comparar con la de otra.
        return await db.DeferredSignatureMarks
            .Where(m => m.TenantId == tenantId
                && m.Estado == DeferredSignatureEstados.Pendiente
                && m.RepresentativeDocumentType == tipo
                && m.RepresentativeDocumentNumber == documento)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(DeferredSignatureMark mark) => db.DeferredSignatureMarks.Add(mark);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
