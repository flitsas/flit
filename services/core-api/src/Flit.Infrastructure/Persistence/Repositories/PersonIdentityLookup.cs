using Flit.Admin.Application.Identity;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Adaptador de <see cref="IPersonIdentityLookup"/> (HU #11028): busca la validación biométrica
/// aprobada y vigente de una persona en <c>tramites.procedure_instance_biometric_validations</c>,
/// acotada a las compañías con grant HABILITADO en el organismo de tránsito. Mismo criterio de
/// vigencia que <see cref="RepresentativeIdentityLookup"/> (filtro grueso en SQL + corte fino por día
/// calendario con <see cref="BiometricRules.EsAprobadaVigente"/>), compuesto desde los mismos extension
/// methods de <see cref="BiometricDocumentMatchQuery"/> (Bug #11583: documento normalizado Trim+Upper).
/// <para>La lectura es CROSS-TENANT por naturaleza —el mandatario valida su identidad dentro del
/// trámite de una compañía, no del organismo— así que va con <c>SET LOCAL row_security = off</c>,
/// reutilizando la transacción en curso si la hay (HU #10992). <c>DocumentNumber</c> es PII: no
/// loguear.</para>
/// </summary>
internal sealed class PersonIdentityLookup : IPersonIdentityLookup
{
    private readonly FlitDbContext _context;

    public PersonIdentityLookup(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<PersonIdentitySnapshot?> FindVigenteInTransitOfficeAsync(
        Guid transitOfficeId,
        string documentType,
        string documentNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        var nowUtc = now.ToUniversalTime();

        async Task<PersonIdentitySnapshot?> QueryAsync()
        {
            // Perímetro: solo compañías con grant habilitado en ESTE organismo.
            var tenants = await _context.TenantTransitOfficeGrants
                .AsNoTracking()
                .Where(g => g.TransitOfficeId == transitOfficeId && g.IsEnabled)
                .Select(g => g.TenantId)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (tenants.Count == 0)
                return null;

            var candidates = await _context.ProcedureInstanceBiometricValidations
                .AsNoTracking()
                .Where(v => tenants.Contains(v.TenantId) && v.Status == BiometricEstados.Aprobado)
                .WhereDocumentoVigenteCandidato(documentType, documentNumber)
                .WhereVentanaVigencia(nowUtc)
                .OrderByDescending(v => v.ValidatedAt)
                .Take(10)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var vigente = candidates.FirstOrDefault(v => BiometricRules.EsAprobadaVigente(v, nowUtc));
            return vigente is null || vigente.ValidatedAt is null
                ? null
                : new PersonIdentitySnapshot(
                    vigente.Id,
                    vigente.TenantId,
                    vigente.Provider,
                    vigente.CertificateHash,
                    vigente.ValidatedAt.Value,
                    vigente.ValidUntil);
        }

        if (!_context.Database.IsRelational())
            return await QueryAsync().ConfigureAwait(false);

        // Guarda de HU #10992: dentro de una transacción abierta NO se anida otra.
        if (_context.Database.CurrentTransaction is not null)
        {
            await _context.Database
                .ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
                .ConfigureAwait(false);
            return await QueryAsync().ConfigureAwait(false);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var transaction = await _context.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await _context.Database
                    .ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
                    .ConfigureAwait(false);
                var result = await QueryAsync().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }
}
