using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Adaptador de <see cref="IRepresentanteLegalIdentityUpdater"/> (HU #11196, AC4): tras aplicar el lote,
/// ancla la validación aprobada a los representantes ACTIVOS de esa persona en el tenant
/// (<c>admin.company_legal_representatives.identity_validation_ref</c>).
///
/// <para>Sin esto, el directorio seguiría diciendo que el representante no tiene con qué firmar y el
/// siguiente trámite de esa compañía volvería a pedirle la validación que acaba de completar.</para>
///
/// <para>Se llavea por documento y puede no encontrar a nadie: si esa persona no está registrada en el
/// directorio —el caso principal de la HU #11195— devuelve 0 y no es un error.</para>
/// </summary>
internal sealed class RepresentanteLegalIdentityUpdater : IRepresentanteLegalIdentityUpdater
{
    private readonly FlitDbContext _context;

    public RepresentanteLegalIdentityUpdater(FlitDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<int> ActualizarIdentidadVigenteAsync(
        Guid tenantId,
        string documentType,
        string documentNumber,
        Guid validationId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty
            || string.IsNullOrWhiteSpace(documentType)
            || string.IsNullOrWhiteSpace(documentNumber))
        {
            return 0;
        }

        var tipo = documentType.Trim();
        var documento = documentNumber.Trim();

        return await TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var representantes = await _context.CompanyLegalRepresentatives
                    .Where(r => r.TenantId == tenantId
                        && r.IsActive
                        && r.DocumentType == tipo
                        && r.DocumentNumber == documento)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (representantes.Count == 0)
                {
                    return 0;
                }

                var now = DateTimeOffset.UtcNow;
                foreach (var representante in representantes)
                {
                    representante.IdentityValidationRef = validationId;
                    representante.UpdatedAt = now;
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return representantes.Count;
            },
            cancellationToken)
            .ConfigureAwait(false);
    }
}
