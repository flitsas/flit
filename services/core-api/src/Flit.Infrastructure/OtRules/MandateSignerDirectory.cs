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
    /// <summary>Identidad aprobada y vigente de un mandatario: su certificado y hasta cuándo vale.</summary>
    private sealed record IdentidadVigente(string? Certificado, DateTimeOffset? ValidUntil);

    /// <summary>Estado "aprobado" del CHECK de <c>admin_identity_validations</c> (HU #10907).</summary>
    private const string AprobadoStatus = "aprobado";

    private readonly FlitDbContext _context;

    public MandateSignerDirectory(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<MandateSignerCandidate>> GetCandidatesAsync(
        Guid transitOfficeId,
        Guid companyTenantId,
        string? nitMandante = null,
        CancellationToken cancellationToken = default)
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
            select new { s.Id, s.FullName, s.DocumentNumber, s.UserId, s.SignatureVaultId, s.DocumentType })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (signers.Count == 0)
        {
            return [];
        }

        var vigentes = await LoadVigentIdentitiesAsync(
            signers.Select(s => s.Id).ToList(), cancellationToken).ConfigureAwait(false);

        // Quién firma a mano ANTE ESTE organismo: es una propiedad del vínculo, no de la persona.
        var fisicos = await _context.MandateSignerTransitOffices.AsNoTracking()
            .Where(o => o.TransitOfficeId == transitOfficeId && o.IsActive && o.SignsPhysically)
            .Select(o => o.MandateSignerId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var firmanAMano = fisicos.ToHashSet();

        var admitidos = await ResolverPorEmpresaAsync(
            transitOfficeId, nitMandante, [.. signers.Select(s => s.Id)], cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. signers.Where(s => admitidos.Contains(s.Id)).Select(s => new MandateSignerCandidate(
                s.Id, s.FullName, s.DocumentNumber, s.UserId, vigentes.ContainsKey(s.Id),
                s.SignatureVaultId, s.DocumentType, vigentes.GetValueOrDefault(s.Id)?.Certificado,
                vigentes.GetValueOrDefault(s.Id)?.ValidUntil,
                firmanAMano.Contains(s.Id))),
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
            .Select(s => new { s.Id, s.FullName, s.DocumentNumber, s.UserId, s.SignatureVaultId, s.DocumentType })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (signer is null)
        {
            return null;
        }

        var vigentes = await LoadVigentIdentitiesAsync([signer.Id], cancellationToken).ConfigureAwait(false);
        return new MandateSignerCandidate(
            signer.Id, signer.FullName, signer.DocumentNumber, signer.UserId, vigentes.ContainsKey(signer.Id),
            signer.SignatureVaultId, signer.DocumentType, vigentes.GetValueOrDefault(signer.Id)?.Certificado,
            vigentes.GetValueOrDefault(signer.Id)?.ValidUntil);
    }

    /// <summary>
    /// Ids de los mandatarios con validación de identidad admin APROBADA y VIGENTE (valid_until nulo o en
    /// el futuro). La tabla tiene RLS por tenant (anclada al OT) y el directorio corre en el tenant
    /// gestora ⇒ lectura con <c>row_security = off</c>. En proveedor no relacional (tests) corre directo.
    /// </summary>
    private async Task<Dictionary<Guid, IdentidadVigente>> LoadVigentIdentitiesAsync(
        IReadOnlyList<Guid> signerIds, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // HU #11030 — además de la vigencia se trae el certificado, que alimenta el sello de firma del
        // mandato cuando el mandatario no tiene firma en el baúl.
        async Task<List<(Guid SubjectRef, string? Certificado, DateTimeOffset? ValidUntil)>> QueryAsync() =>
            [.. (await _context.AdminIdentityValidations
                .AsNoTracking()
                .Where(v => v.SubjectType == AdminIdentitySubjectTypes.MandateSigner
                    && signerIds.Contains(v.SubjectRef)
                    && v.Status == AprobadoStatus
                    && (v.ValidUntil == null || v.ValidUntil > now))
                .OrderByDescending(v => v.ValidatedAt)
                .Select(v => new { v.SubjectRef, v.CertificateHash, v.ValidUntil })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false))
                .Select(x => (x.SubjectRef, x.CertificateHash, x.ValidUntil))];

        static Dictionary<Guid, IdentidadVigente> ToMap(
            List<(Guid SubjectRef, string? Certificado, DateTimeOffset? ValidUntil)> rows) =>
            rows.GroupBy(r => r.SubjectRef)
                .ToDictionary(
                    g => g.Key,
                    g => new IdentidadVigente(
                        g.Select(r => r.Certificado).FirstOrDefault(c => c is not null),
                        // La más reciente manda: es la que sigue en pie.
                        g.Select(r => r.ValidUntil).FirstOrDefault()));

        if (!_context.Database.IsRelational())
        {
            return ToMap(await QueryAsync().ConfigureAwait(false));
        }

        // Si ya existe una transacción activa (p. ej. el scope de tenant del cliente que abre
        // OtClientProcedureRepository.ExecuteInClientTenantScopeAsync durante la aprobación), NO abrir
        // otra ni usar una ExecutionStrategy anidada: eso lanzaba "The connection is already in a
        // transaction and cannot participate in another transaction" (HU #10992). Se reutiliza la
        // transacción en curso aplicando el SET LOCAL sobre ella; el ajuste solo vive hasta su commit y
        // la consulta que sigue es la última lectura del scope, así que no filtra a otras operaciones.
        if (_context.Database.CurrentTransaction is not null)
        {
            await _context.Database
                .ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
                .ConfigureAwait(false);
            return ToMap(await QueryAsync().ConfigureAwait(false));
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

        return ToMap(vigentes);
    }

    /// <summary>
    /// Mandatarios admitidos para la empresa que otorga el mandato: los asociados a ESA empresa en el
    /// organismo, más los que no tienen ninguna asociada.
    ///
    /// <para>La ausencia significa "aplica a todas" a propósito: los mandatarios registrados antes de
    /// esta acotación no tienen filas, y sin esa regla desaparecerían de todos los trámites al
    /// desplegar. Sin NIT del mandante tampoco se acota: no hay contra qué comparar.</para>
    /// </summary>
    private async Task<HashSet<Guid>> ResolverPorEmpresaAsync(
        Guid transitOfficeId,
        string? nitMandante,
        List<Guid> signerIds,
        CancellationToken cancellationToken)
    {
        var todos = signerIds.ToHashSet();
        if (string.IsNullOrWhiteSpace(nitMandante) || signerIds.Count == 0)
        {
            return todos;
        }

        var nit = nitMandante.Trim();

        var filas = await (
            from a in _context.MandateSignerRepresentedCompanies.AsNoTracking()
            join e in _context.RepresentedCompanies.AsNoTracking()
                on a.RepresentedCompanyId equals e.Id
            where a.TransitOfficeId == transitOfficeId
                && a.IsActive
                && signerIds.Contains(a.MandateSignerId)
            select new { a.MandateSignerId, e.DocumentNumber })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Con asociaciones: solo pasan los de esta empresa. Sin ninguna: pasa igual.
        var conAsociacion = filas.Select(f => f.MandateSignerId).ToHashSet();
        var deLaEmpresa = filas
            .Where(f => string.Equals(f.DocumentNumber?.Trim(), nit, StringComparison.Ordinal))
            .Select(f => f.MandateSignerId)
            .ToHashSet();

        return [.. todos.Where(id => !conAsociacion.Contains(id) || deLaEmpresa.Contains(id))];
    }
}
