using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="IMandateSignerDirectory"/> (ADR-0036 §D9, HU #10916). Consulta
/// los mandatarios (<c>admin.mandate_signers</c>) ACTIVOS asignados a una compañía gestora en un OT
/// (join con <c>admin.mandate_signer_companies</c> activo; ambas SIN RLS ⇒ lectura directa). Para cada
/// candidato marca <see cref="MandateSignerCandidate.IdentityVigente"/> leyendo su validación de
/// identidad admin (HU #10911): APROBADA y con <c>valid_until</c> nulo o en el futuro. Esa tabla
/// (<c>admin.admin_identity_validations</c>) SÍ tiene RLS por tenant (anclada al tenant del OT), y el
/// directorio corre en el contexto del tenant gestora, así que la lectura de identidad va con
/// <c>SET LOCAL row_security = off</c> (misma técnica que <c>DbMandateSignerReader</c>). No expone PII
/// adicional (solo nombre, documento, cuenta de usuario y el booleano de vigencia).
/// </summary>
internal sealed class MandateSignerDirectory : IMandateSignerDirectory
{
    /// <summary>Estado "aprobado" del CHECK de <c>admin_identity_validations</c> (HU #10907).</summary>
    private const string AprobadoStatus = "aprobado";

    private readonly FlitDbContext _context;

    public MandateSignerDirectory(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
        Guid transitOfficeId, Guid companyTenantId, CancellationToken cancellationToken = default)
    {
        if (transitOfficeId == Guid.Empty || companyTenantId == Guid.Empty)
        {
            return [];
        }

        var signers = await (
            from s in _context.MandateSigners.AsNoTracking()
            join c in _context.MandateSignerCompanies.AsNoTracking() on s.Id equals c.MandateSignerId
            where c.TransitOfficeId == transitOfficeId
                && c.CompanyTenantId == companyTenantId
                && c.IsActive
                && s.IsActive
            select new { s.Id, s.FullName, s.DocumentNumber, s.UserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (signers.Count == 0)
        {
            return [];
        }

        var vigentes = await LoadVigentIdentitiesAsync(
            signers.Select(s => s.Id).ToList(), cancellationToken).ConfigureAwait(false);

        return
        [
            .. signers.Select(s => new MandateSignerCandidate(
                s.Id, s.FullName, s.DocumentNumber, s.UserId, vigentes.Contains(s.Id))),
        ];
    }

    public async Task<MandateSignerCandidate?> GetByIdAsync(
        Guid mandateSignerId, CancellationToken cancellationToken = default)
    {
        if (mandateSignerId == Guid.Empty)
        {
            return null;
        }

        var signer = await _context.MandateSigners.AsNoTracking()
            .Where(s => s.Id == mandateSignerId && s.IsActive)
            .Select(s => new { s.Id, s.FullName, s.DocumentNumber, s.UserId })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (signer is null)
        {
            return null;
        }

        var vigentes = await LoadVigentIdentitiesAsync([signer.Id], cancellationToken).ConfigureAwait(false);
        return new MandateSignerCandidate(
            signer.Id, signer.FullName, signer.DocumentNumber, signer.UserId, vigentes.Contains(signer.Id));
    }

    /// <summary>
    /// Ids de los mandatarios con validación de identidad admin APROBADA y VIGENTE (valid_until nulo o en
    /// el futuro). La tabla tiene RLS por tenant (anclada al OT) y el directorio corre en el tenant
    /// gestora ⇒ lectura con <c>row_security = off</c>. En proveedor no relacional (tests) corre directo.
    /// </summary>
    private async Task<HashSet<Guid>> LoadVigentIdentitiesAsync(
        IReadOnlyList<Guid> signerIds, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        async Task<List<Guid>> QueryAsync() => await _context.AdminIdentityValidations
            .AsNoTracking()
            .Where(v => v.SubjectType == AdminIdentitySubjectTypes.MandateSigner
                && signerIds.Contains(v.SubjectRef)
                && v.Status == AprobadoStatus
                && (v.ValidUntil == null || v.ValidUntil > now))
            .Select(v => v.SubjectRef)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!_context.Database.IsRelational())
        {
            return [.. await QueryAsync().ConfigureAwait(false)];
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        var vigentes = await strategy.ExecuteAsync(async () =>
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

        return [.. vigentes];
    }
}
