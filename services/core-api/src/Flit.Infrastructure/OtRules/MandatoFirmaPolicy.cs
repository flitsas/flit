using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Adaptador de <see cref="IMandatoFirmaPolicy"/>: lee el convenio comercial
/// (<c>admin.company_transit_office_agreements</c>) y la marca de firma física del mandatario en ese
/// organismo (<c>admin.mandate_signer_transit_offices.signs_physically</c>).
///
/// <para>Ambas tablas son de Admin y NO llevan RLS por compañía —se consultan cruzando compañía y
/// organismo desde los dos lados—, así que la lectura es directa. Es el mismo criterio que
/// <see cref="MandateSignerDirectory"/> con <c>mandate_signers</c> y sus puentes.</para>
/// </summary>
internal sealed class MandatoFirmaPolicy : IMandatoFirmaPolicy
{
    private readonly FlitDbContext _context;

    public MandatoFirmaPolicy(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<MandatoFirmaContexto> ResolveAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        Guid? mandateSignerId,
        CancellationToken cancellationToken = default)
    {
        if (transitOfficeId == Guid.Empty)
        {
            // Sin organismo no hay nada que resolver. Se responde como el default seguro: el bloque de
            // firma se conserva, porque el mandatario es un actor obligatorio.
            return new MandatoFirmaContexto(false, true, false);
        }

        var convenios = await _context.CompanyTransitOfficeAgreements.AsNoTracking()
            .Where(a => a.TransitOfficeId == transitOfficeId && a.IsActive)
            .Select(a => a.CompanyTenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tieneConvenio = companyTenantId != Guid.Empty && convenios.Contains(companyTenantId);

        // "Ninguna compañía" y "esta compañía no" son casos distintos: un organismo al que todavía nadie
        // le registró convenios no debe comportarse como uno que sí trabaja con ellos.
        var organismoSinNingunConvenio = convenios.Count == 0;

        var firmaFisica = mandateSignerId is { } signerId
            && signerId != Guid.Empty
            && await _context.MandateSignerTransitOffices.AsNoTracking()
                .AnyAsync(
                    p => p.MandateSignerId == signerId
                        && p.TransitOfficeId == transitOfficeId
                        && p.IsActive
                        && p.SignsPhysically,
                    cancellationToken)
                .ConfigureAwait(false);

        return new MandatoFirmaContexto(tieneConvenio, organismoSinNingunConvenio, firmaFisica);
    }
}
