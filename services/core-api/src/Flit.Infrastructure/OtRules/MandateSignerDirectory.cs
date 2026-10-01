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
/// <para><b>Qué validación cuenta (HU #13247, Feature #13245).</b> SOLO la validación lanzada PARA el mandatario
/// (party_role <c>mandatario</c> + referencia a su ficha), la más reciente con su documento actual
/// (<see cref="IdentityVigenciaPorDocumentoResolver.ResolveMandatariosAsync"/>). Ya no se busca por documento y
/// tenant: la aprobación de un comprador, un vendedor o una prevalidación con la misma cédula no cuenta. Se
/// registra en el tenant de la compañía (HU #13121, #13246), pero la lectura no depende del tenant.</para>

/// <para><b>Reutiliza, no duplica.</b> La consulta y clasificación de vigencia vive en
/// <see cref="IdentityVigenciaPorDocumentoResolver"/> (HU #11751, capa Application): este directorio NO
/// tiene su propia query contra <c>tramites.procedure_instance_biometric_validations</c>.</para>
/// </summary>
internal sealed class MandateSignerDirectory : IMandateSignerDirectory
{
    /// <summary>Fila de un mandatario candidato (propio o asociado) antes de resolver identidad y firma.</summary>
    private sealed class SignerRow
    {
        public Guid Id { get; init; }
        public string FullName { get; init; } = string.Empty;
        public string? DocumentNumber { get; init; }
        public Guid? UserId { get; init; }
        public Guid? SignatureVaultId { get; init; }
        public string DocumentType { get; init; } = string.Empty;
        public string SignerModel { get; init; } = string.Empty;
        public string? SignatureMethod { get; init; }
        public string ValidityKind { get; init; } = string.Empty;
        public DateOnly? ValidFrom { get; init; }
        public DateOnly? ValidTo { get; init; }
        public bool IsActive { get; init; }
        public string Origen { get; init; } = string.Empty;
    }

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
        var propios = await (
            from s in _context.MandateSigners.AsNoTracking()
            join c in _context.MandateSignerCompanies.AsNoTracking() on s.Id equals c.MandateSignerId
            where c.TransitOfficeId == transitOfficeId
                && c.CompanyTenantId == companyTenantId
                && c.IsActive
                && s.IsActive
                && s.DeletedAt == null
            select new SignerRow
            {
                Id = s.Id, FullName = s.FullName, DocumentNumber = s.DocumentNumber, UserId = s.UserId,
                SignatureVaultId = s.SignatureVaultId, DocumentType = s.DocumentType, SignerModel = s.SignerModel,
                SignatureMethod = s.SignatureMethod, ValidityKind = s.ValidityKind, ValidFrom = s.ValidFrom,
                ValidTo = s.ValidTo, IsActive = s.IsActive,
                // HU #13142 — el origen del vínculo decide el nivel de la prelación (ADR-0066).
                Origen = c.ConfiguredByScope,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // HU #13180 — nivel 3: mandatarios de OTRAS compañías asociados (por tenant) a la del trámite en este
        // organismo. Solo asociaciones activas de mandatarios activos, sin baja lógica y aplicables aquí (vínculo
        // propio activo en el organismo). Si el mandatario ya es propio de esta compañía manda su vínculo.
        var asociados = await (
            from a in _context.MandateSignerAssociatedCompanies.AsNoTracking()
            join s in _context.MandateSigners.AsNoTracking() on a.MandateSignerId equals s.Id
            join c in _context.MandateSignerCompanies.AsNoTracking() on s.Id equals c.MandateSignerId
            where a.TransitOfficeId == transitOfficeId
                && a.AssociatedCompanyTenantId == companyTenantId
                && a.IsActive
                && c.TransitOfficeId == transitOfficeId
                && c.IsActive
                && c.CompanyTenantId != companyTenantId
                && s.IsActive
                && s.DeletedAt == null
                && !_context.MandateSignerCompanies.Any(x =>
                    x.MandateSignerId == s.Id
                    && x.TransitOfficeId == transitOfficeId
                    && x.CompanyTenantId == companyTenantId
                    && x.IsActive)
            select new SignerRow
            {
                Id = s.Id, FullName = s.FullName, DocumentNumber = s.DocumentNumber, UserId = s.UserId,
                SignatureVaultId = s.SignatureVaultId, DocumentType = s.DocumentType, SignerModel = s.SignerModel,
                SignatureMethod = s.SignatureMethod, ValidityKind = s.ValidityKind, ValidFrom = s.ValidFrom,
                ValidTo = s.ValidTo, IsActive = s.IsActive,
                Origen = MandateSignerOrigins.Asociado,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var signers = propios
            .Concat(asociados.DistinctBy(x => x.Id))
            .ToList();

        if (signers.Count == 0)
        {
            return [];
        }

        // HU #13180b — compañías vinculadas de cada mandatario: ahí puede vivir su firma del baúl (asociado de
        // otra compañía, default del OT), que no siempre está en el tenant de la compañía del trámite.
        var tenantsVinculados = await LoadLinkedTenantsAsync(
            [.. signers.Select(s => s.Id).Distinct()], cancellationToken).ConfigureAwait(false);

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

        var today = ColombiaTime.Today(TimeProvider.System);

        return
        [
            .. signers.Select(s =>
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
                    s.Origen, s.SignerModel, MetodoEfectivo(s.SignerModel, s.SignatureMethod, s.SignatureVaultId),
                    VaultTenantIds: tenantsVinculados.GetValueOrDefault(s.Id));
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
            Eliminado: signer.Eliminado,
            // HU #13180b — el default del OT no está vinculado a la compañía del trámite: su baúl vive en sus propias compañías.
            VaultTenantIds: (await LoadLinkedTenantsAsync([signer.Id], cancellationToken).ConfigureAwait(false))
                .GetValueOrDefault(signer.Id));
    }

    /// <summary>
    /// HU #13180b — tenants de las compañías con vínculo ACTIVO de cada mandatario (en cualquier organismo): ahí
    /// puede vivir su firma del baúl.
    /// </summary>
    private async Task<Dictionary<Guid, IReadOnlyList<Guid>>> LoadLinkedTenantsAsync(
        List<Guid> signerIds, CancellationToken cancellationToken)
    {
        if (signerIds.Count == 0)
        {
            return [];
        }

        var links = await _context.MandateSignerCompanies.AsNoTracking()
            .Where(c => signerIds.Contains(c.MandateSignerId) && c.IsActive)
            .Select(c => new { c.MandateSignerId, c.CompanyTenantId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return links
            .GroupBy(l => l.MandateSignerId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(l => l.CompanyTenantId).Distinct()]);
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
        // HU #13247 — solo la validación propia del mandatario (party_role mandatario + su ficha) cuenta.
        var resueltos = await _identityResolver.ResolveMandatariosAsync(
            [.. signers.Select(s => new IdentityVigenciaPorDocumentoResolver.MandatarioIdentityRef(
                s.Id, s.DocumentType, s.DocumentNumber))],
            now,
            cancellationToken).ConfigureAwait(false);

        foreach (var (signerId, result) in resueltos)
        {
            map[signerId] = new IdentidadResuelta(result.Status, result.CertificateHash, result.ValidUntil);
        }

        return map;
    }
}
