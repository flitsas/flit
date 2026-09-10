using Flit.Admin.Application.Companies.LegalRepresentatives;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Adaptador del puerto <see cref="IRepresentativeIdentityLookup"/> (HU #10900, ADR-0033): resuelve,
/// para el resolutor de firma/identidad del módulo Admin, la validación biométrica APROBADA y
/// VIGENTE de una persona por documento. Consulta directamente
/// <c>tramites.procedure_instance_biometric_validations</c> con el MISMO criterio de vigencia que
/// <c>ProcedureInstanceRepository.FindVigenteApprovedByDocumentAsync</c> (reuso HU #10350), compuesto con
/// los extension methods de <see cref="BiometricDocumentMatchQuery"/> (Bug #11583): documento normalizado
/// (Trim+Upper), ventana de vigencia y prevalidaciones standalone son la MISMA cláusula para ambos
/// consumidores, filtro grueso por timestamp en SQL + corte fino por día calendario con
/// <see cref="BiometricRules.EsAprobadaVigente"/> en memoria. Así Admin no depende de
/// <c>Tramites.Domain</c> ni de <c>IProcedureInstanceRepository</c> (evita la ambigüedad de DI entre
/// las dos implementaciones registradas). <c>DocumentNumber</c> es PII (Ley 1581): no loguear.
/// </summary>
internal sealed class RepresentativeIdentityLookup : IRepresentativeIdentityLookup
{
    private readonly FlitDbContext _context;

    public RepresentativeIdentityLookup(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Guid?> FindVigenteIdentityRefAsync(
        Guid tenantId,
        string tipoDocumento,
        string documento,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipoDocumento);
        ArgumentException.ThrowIfNullOrWhiteSpace(documento);

        // Npgsql solo acepta offset 0 (UTC) al escribir un parámetro `timestamptz`: los llamadores
        // (resolutor de guardado y lookup por NIT) anclan "ahora" a la medianoche de Colombia
        // (offset -05:00), lo que rompía la consulta con ArgumentException → 500. Se reconvierte a UTC
        // conservando el MISMO instante; el corte fino por día calendario (EsAprobadaVigente) vuelve a
        // Colombia con .ToOffset, así que la vigencia no cambia. Mismo criterio que
        // ProcedureInstanceBiometricValidation.FechaFinVigencia (que también devuelve UTC por esto).
        var nowUtc = now.ToUniversalTime();

        var candidates = await _context.ProcedureInstanceBiometricValidations
            .AsNoTracking()
            .Where(v => v.TenantId == tenantId && v.Status == BiometricEstados.Aprobado)
            .WhereDocumentoVigenteCandidato(tipoDocumento, documento)
            .WhereVentanaVigencia(nowUtc)
            .WhereInstanciaVigente()
            .OrderByDescending(v => v.ValidatedAt)
            .Take(10)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var vigente = candidates.FirstOrDefault(v => BiometricRules.EsAprobadaVigente(v, nowUtc));
        return vigente?.Id;
    }
}
