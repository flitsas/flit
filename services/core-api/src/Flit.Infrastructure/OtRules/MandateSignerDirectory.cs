using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Identity;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.UseCases.Persons;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Identity;
using Flit.Tramites.Domain.Integration;
using Flit.Queries.Domain.Time;
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
/// <para><b>Qué tenant se consulta (HU #13121).</b> La validación biométrica del mandatario se registra en
/// el tenant de la COMPAÑÍA que lo registró (el módulo Identidad devuelve 403 a los usuarios de un OT, así
/// que nunca nace en el tenant del organismo). Se resuelve en cada compañía vinculada (regla única: vigente
/// en alguna) con <see cref="MandateSignerIdentityTenantResolver"/>; el tenant del OT solo es respaldo para
/// mandatarios sin compañías vinculadas.</para>

/// <para><b>Reutiliza, no duplica.</b> La consulta y clasificación de vigencia vive en
/// <see cref="IdentityVigenciaPorDocumentoResolver"/> (HU #11751, capa Application): este directorio NO
/// tiene su propia query contra <c>tramites.procedure_instance_biometric_validations</c>.</para>
/// </summary>
internal sealed class MandateSignerDirectory : IMandateSignerDirectory
{
    /// <summary>Identidad de un mandatario: su estado (ADR-0050), certificado y hasta cuándo vale.</summary>
    private sealed record IdentidadResuelta(string Status, string? Certificado, DateTimeOffset? ValidUntil)
    {
        public bool Vigente => Status == IdentityVigenciaEstados.AprobadaVigente;
    }

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

        // Solo el tenant del trámite: la cabeza de red no presta mandatarios a las hijas
        // (Epic #12235). Cada compañía firma con su propio directorio.
        var signers = await (
            from s in _context.MandateSigners.AsNoTracking()
            join c in _context.MandateSignerCompanies.AsNoTracking() on s.Id equals c.MandateSignerId
            where c.TransitOfficeId == transitOfficeId
                && c.CompanyTenantId == companyTenantId
                && c.IsActive
                && s.IsActive
                && s.DeletedAt == null
            select new
            {
                s.Id, s.FullName, s.DocumentNumber, s.UserId, s.SignatureVaultId, s.DocumentType, s.SignerModel,
                s.SignatureMethod, s.ValidityKind, s.ValidFrom, s.ValidTo, s.IsActive,
                // HU #13142 — el origen del vínculo decide el nivel de la prelación (ADR-0066).
                Origen = c.ConfiguredByScope,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (signers.Count == 0)
        {
            return [];
        }

        var identidades = await LoadIdentitiesAsync(
            transitOfficeId,
            // HU #13129 — Persona jurídica y Formato en blanco no tienen identidad que resolver.
            signers.Where(s => s.SignerModel == "natural" && !string.IsNullOrWhiteSpace(s.DocumentNumber))
                .DistinctBy(s => s.Id)
                .Select(s => (s.Id, s.DocumentType, s.DocumentNumber!)).ToList(),
            cancellationToken).ConfigureAwait(false);
        var vigentes = identidades.Where(kv => kv.Value.Vigente).ToDictionary(kv => kv.Key, kv => kv.Value);

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

        var today = ColombiaTime.Today(TimeProvider.System);

        return
        [
            .. signers.Where(s => admitidos.Contains(s.Id)).Select(s =>
            {
                var firma = EvaluarFirma(
                    s.SignerModel, s.SignatureMethod, s.IsActive, s.ValidityKind, s.ValidFrom, s.ValidTo,
                    s.SignatureVaultId, identidades.GetValueOrDefault(s.Id), today);
                return new MandateSignerCandidate(
                    s.Id, s.FullName, s.DocumentNumber ?? string.Empty, s.UserId, vigentes.ContainsKey(s.Id),
                    s.SignatureVaultId, s.DocumentType, vigentes.GetValueOrDefault(s.Id)?.Certificado,
                    vigentes.GetValueOrDefault(s.Id)?.ValidUntil,
                    firmanAMano.Contains(s.Id),
                    firma?.Valida ?? true, firma?.Motivo,
                    s.Origen, s.SignerModel, MetodoEfectivo(s.SignerModel, s.SignatureMethod, s.SignatureVaultId));
            }),
        ];
    }

    public Task<MandateSignerCandidate?> GetByIdAsync(
        Guid mandateSignerId, CancellationToken cancellationToken = default) =>
        GetByIdAsync(mandateSignerId, incluirEliminados: false, cancellationToken);

    /// <summary>
    /// HU #13142 (AC4) — por defecto NO devuelve mandatarios con baja lógica (<c>deleted_at</c>): un eliminado
    /// no puede ser el default del OT ni firmar trámites nuevos. Con <paramref name="incluirEliminados"/> se
    /// conserva la referencia (marcada <see cref="MandateSignerCandidate.Eliminado"/>) para los trámites ya
    /// firmados, cuyo PDF sigue mostrando a quien firmó.
    /// </summary>
    public async Task<MandateSignerCandidate?> GetByIdAsync(
        Guid mandateSignerId, bool incluirEliminados, CancellationToken cancellationToken = default)
    {
        if (mandateSignerId == Guid.Empty)
        {
            return null;
        }

        var signer = await _context.MandateSigners.AsNoTracking()
            .Where(s => s.Id == mandateSignerId && s.IsActive && (incluirEliminados || s.DeletedAt == null))
            .Select(s => new
            {
                s.Id, s.FullName, s.DocumentNumber, s.UserId, s.SignatureVaultId, s.DocumentType,
                s.TransitOfficeId, s.SignerModel, s.SignatureMethod, s.ValidityKind, s.ValidFrom, s.ValidTo,
                s.IsActive, Eliminado = s.DeletedAt != null,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (signer is null)
        {
            return null;
        }

        // Sin OT (registro huérfano) no hay tenant contra el que resolver identidad: se devuelve el
        // candidato sin vigencia, igual que antes cuando no había fila admin.
        var identidades = await LoadIdentitiesAsync(
            signer.TransitOfficeId,
            signer.SignerModel == "natural" && !string.IsNullOrWhiteSpace(signer.DocumentNumber)
                ? [(signer.Id, signer.DocumentType, signer.DocumentNumber)]
                : [],
            cancellationToken).ConfigureAwait(false);

        var identidad = identidades.GetValueOrDefault(signer.Id);
        var vigente = identidad?.Vigente == true ? identidad : null;
        var firma = EvaluarFirma(
            signer.SignerModel, signer.SignatureMethod, signer.IsActive, signer.ValidityKind, signer.ValidFrom,
            signer.ValidTo, signer.SignatureVaultId, identidad, ColombiaTime.Today(TimeProvider.System));

        return new MandateSignerCandidate(
            signer.Id, signer.FullName, signer.DocumentNumber ?? string.Empty, signer.UserId, vigente is not null,
            signer.SignatureVaultId, signer.DocumentType, vigente?.Certificado, vigente?.ValidUntil,
            FirmaFisica: false, FirmaValida: firma?.Valida ?? true, MotivoSinFirma: firma?.Motivo,
            // El default del OT no viene de un vínculo con la compañía: su origen es el del organismo.
            Origen: MandateSignerOrigins.Organismo, SignerModel: signer.SignerModel,
            SignatureMethod: MetodoEfectivo(signer.SignerModel, signer.SignatureMethod, signer.SignatureVaultId),
            Eliminado: signer.Eliminado);
    }

    /// <summary>
    /// Forma de firma efectiva (HU #13142): nula en Persona jurídica y Formato en blanco; en una Persona natural
    /// legado sin forma, baúl si tiene firma vinculada y biometría si no (misma inferencia que
    /// <see cref="MandateSignerFirmaValidez"/>).
    /// </summary>
    private static string? MetodoEfectivo(string signerModel, string? signatureMethod, Guid? signatureVaultId) =>
        signerModel != MandateSignerModels.Natural
            ? null
            : signatureMethod ?? (signatureVaultId is not null
                ? MandateSignatureMethods.Baul
                : MandateSignatureMethods.Biometria);

    /// <summary>
    /// HU #13130 — firma válida del mandatario: vigencia propia activa Y (si firma con biometría) validación
    /// biométrica vigente. <c>null</c> cuando el modelo no es Persona natural (no hay firma personal).
    /// </summary>
    private static MandateSignerFirmaValidez.Resultado? EvaluarFirma(
        string signerModel, string? signatureMethod, bool isActive, string validityKind,
        DateOnly? validFrom, DateOnly? validTo, Guid? signatureVaultId, IdentidadResuelta? identidad, DateOnly today)
    {
        var vigencia = MandateValidityStatus.Compute(isActive, validityKind, validFrom, validTo, today);
        var identidadLegacy = identidad?.Status switch
        {
            IdentityVigenciaEstados.AprobadaVigente => AdminIdentityVigencia.Valid,
            IdentityVigenciaEstados.EnCurso => AdminIdentityVigencia.Pending,
            IdentityVigenciaEstados.Vencida => AdminIdentityVigencia.Expired,
            _ => AdminIdentityVigencia.None,
        };
        return MandateSignerFirmaValidez.Evaluar(
            signerModel, signatureMethod, vigencia, identidadLegacy, signatureVaultId is not null);
    }

    /// <summary>
    /// Identidades de los mandatarios indicados (HU #11752, ADR-0050; ahora con su estado para HU #13130), resueltas
    /// contra el módulo Identidad en el tenant de la(s) COMPAÑÍA(S) que registró al mandatario (HU #13121;
    /// vigente en alguna de sus compañías vinculadas). El tenant del OT solo es respaldo si no tiene compañías.
    /// </summary>
    private async Task<Dictionary<Guid, IdentidadResuelta>> LoadIdentitiesAsync(
        Guid transitOfficeId,
        List<(Guid Id, string DocumentType, string DocumentNumber)> signers,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<Guid, IdentidadResuelta>();
        if (signers.Count == 0 || transitOfficeId == Guid.Empty)
        {
            return map;
        }

        var now = DateTimeOffset.UtcNow;
        var resueltos = await MandateSignerIdentityTenantResolver.ResolveAsync(
            _context,
            _otStatus,
            [.. signers.Select(s => new MandateSignerIdentityTenantResolver.SignerRef(
                s.Id, transitOfficeId, s.DocumentType, s.DocumentNumber))],
            (tenantId, documentos, ct) => _identityResolver.ResolveManyAsync(tenantId, documentos, now, ct),
            cancellationToken).ConfigureAwait(false);

        foreach (var (signerId, result) in resueltos)
        {
            map[signerId] = new IdentidadResuelta(result.Status, result.CertificateHash, result.ValidUntil);
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
