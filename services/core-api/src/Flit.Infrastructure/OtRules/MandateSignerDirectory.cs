using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="IMandateSignerDirectory"/> (ADR-0036 §D9, HU #10916). Consulta
/// los mandatarios (<c>admin.mandate_signers</c>) ACTIVOS asignados a una compañía gestora en un OT
/// (join con <c>admin.mandate_signer_companies</c> activo; ambas SIN RLS ⇒ lectura directa). Para cada
/// candidato marca <see cref="MandateSignerCandidate.IdentityVigente"/> resolviendo su identidad
/// EXCLUSIVAMENTE contra el módulo Identidad (HU #11752, ADR-0050): fuente única de verdad, ya no
/// <c>admin.admin_identity_validations</c> (ADR-0034, superada).
///
/// <para><b>Qué tenant se consulta.</b> Las validaciones de identidad del mandatario viven en el tenant
/// PROPIO del organismo de tránsito (mismo criterio que el disparo admin ya retirado: la identidad de un
/// mandatario se ancla al OT donde está registrado, no a la compañía gestora que en cada llamada puede
/// variar). Se resuelve con <see cref="ITransitOfficeOperationalStatusReader"/> — MISMO mecanismo que
/// usaba <c>AdminMandateSignerIdentityEndpoints.BuildDescriptorAsync</c> antes de esta HU.</para>
///
/// <para><b>Reutiliza, no duplica.</b> La consulta y clasificación de vigencia vive en
/// <see cref="IdentityVigenciaPorDocumentoResolver"/> (HU #11751, capa Application): este directorio NO
/// tiene su propia query contra <c>tramites.procedure_instance_biometric_validations</c>.</para>
/// </summary>
internal sealed class MandateSignerDirectory : IMandateSignerDirectory
{
    /// <summary>Identidad aprobada y vigente de un mandatario: su certificado y hasta cuándo vale.</summary>
    private sealed record IdentidadVigente(string? Certificado, DateTimeOffset? ValidUntil);

    private readonly FlitDbContext _context;
    private readonly ITransitOfficeOperationalStatusReader _otStatus;
    private readonly IdentityVigenciaPorDocumentoResolver _identityResolver;

    public MandateSignerDirectory(
        FlitDbContext context,
        ITransitOfficeOperationalStatusReader otStatus,
        IdentityVigenciaPorDocumentoResolver identityResolver)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _otStatus = otStatus ?? throw new ArgumentNullException(nameof(otStatus));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
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
            transitOfficeId,
            signers.Select(s => (s.Id, s.DocumentType, s.DocumentNumber)).ToList(),
            cancellationToken).ConfigureAwait(false);

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
            .Select(s => new
            {
                s.Id, s.FullName, s.DocumentNumber, s.UserId, s.SignatureVaultId, s.DocumentType,
                s.TransitOfficeId,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (signer is null)
        {
            return null;
        }

        // Sin OT (registro huérfano) no hay tenant contra el que resolver identidad: se devuelve el
        // candidato sin vigencia, igual que antes cuando no había fila admin.
        var vigentes = await LoadVigentIdentitiesAsync(
            signer.TransitOfficeId,
            [(signer.Id, signer.DocumentType, signer.DocumentNumber)],
            cancellationToken).ConfigureAwait(false);

        return new MandateSignerCandidate(
            signer.Id, signer.FullName, signer.DocumentNumber, signer.UserId, vigentes.ContainsKey(signer.Id),
            signer.SignatureVaultId, signer.DocumentType, vigentes.GetValueOrDefault(signer.Id)?.Certificado,
            vigentes.GetValueOrDefault(signer.Id)?.ValidUntil);
    }

    /// <summary>
    /// Identidades APROBADAS y VIGENTES (HU #11752, ADR-0050) de los mandatarios indicados, resueltas
    /// contra el módulo Identidad en el tenant PROPIO del organismo <paramref name="transitOfficeId"/>.
    /// Devuelve <c>{}</c> sin consultar si el OT no tiene tenant dado de alta — un mandatario sin OT
    /// operativo no puede tener identidad vigente que apalancar.
    /// </summary>
    private async Task<Dictionary<Guid, IdentidadVigente>> LoadVigentIdentitiesAsync(
        Guid transitOfficeId,
        IReadOnlyList<(Guid Id, string DocumentType, string DocumentNumber)> signers,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, IdentidadVigente>();
        if (signers.Count == 0 || transitOfficeId == Guid.Empty)
        {
            return map;
        }

        var status = await _otStatus.GetByIdAsync(transitOfficeId, cancellationToken).ConfigureAwait(false);
        if (status is null || !status.HasTenant || status.TenantId is not { } otTenantId)
        {
            return map;
        }

        var documents = signers
            .Select(s => (s.DocumentType, s.DocumentNumber))
            .ToList();
        var resolved = await _identityResolver
            .ResolveManyAsync(otTenantId, documents, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        foreach (var s in signers)
        {
            var key = DocumentCanonicalNormalization.IdentidadKey(otTenantId, s.DocumentType, s.DocumentNumber);
            if (resolved.TryGetValue(key, out var result)
                && result.Status == IdentityVigenciaEstados.AprobadaVigente)
            {
                map[s.Id] = new IdentidadVigente(result.CertificateHash, result.ValidUntil);
            }
        }

        return map;
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
