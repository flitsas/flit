using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Tramites.Application.Ocr;
using Flit.Tramites.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// CRUD SuperAdmin de <c>admin.transit_office_mandate_config</c> + plantilla propia (PDF/editor) + OCR.
/// Default implícito (sin fila) = plantilla genérica + assignment_mode signer + sin custom.
/// El listado solo incluye OT <b>activos en FLIT</b> (tienen tenant OT y <c>tenants.is_active</c>).
/// </summary>
internal sealed class MandateConfigAdminService : IMandateConfigAdminService
{
    private const long MaxPdfBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> Families = new(StringComparer.OrdinalIgnoreCase)
    {
        MandatoFamiliaCodes.Individuo,
        MandatoFamiliaCodes.OrganismoTransito,
    };

    private static readonly HashSet<string> AssignmentModes = new(StringComparer.OrdinalIgnoreCase)
    {
        MandatoAssignmentModeCodes.Signer,
        MandatoAssignmentModeCodes.Institutional,
        MandatoAssignmentModeCodes.Open,
    };

    private readonly FlitDbContext _db;
    private readonly ITransitOfficeCatalog _catalog;
    private readonly ITransitOfficeOperationalStatusReader _operationalStatus;
    private readonly IMandateTemplateStorage _templateStorage;
    private readonly IEffectiveTransitOfficeListResolver? _effectiveOffices;

    public MandateConfigAdminService(
        FlitDbContext db,
        ITransitOfficeCatalog catalog,
        ITransitOfficeOperationalStatusReader operationalStatus,
        IDocumentOcrAnalyzer ocr,
        IMandateTemplateStorage templateStorage,
        IEffectiveTransitOfficeListResolver? effectiveOffices = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _operationalStatus = operationalStatus ?? throw new ArgumentNullException(nameof(operationalStatus));
        // HU #13158: el extract del documento se retiró; el parámetro se conserva para no alterar los puntos
        // de construcción (deuda: retirarlo cuando F6 y F7 estén integradas).
        ArgumentNullException.ThrowIfNull(ocr);
        _templateStorage = templateStorage ?? throw new ArgumentNullException(nameof(templateStorage));

        // Bug #12912 — compañías por OT según la lista efectiva de red. Opcional: sin él (tests que
        // construyen el servicio a mano) se conserva el criterio previo de grant propio.
        _effectiveOffices = effectiveOffices;
    }

    public async Task<IReadOnlyList<MandateOtConfigView>> ListAsync(CancellationToken ct = default)
    {
        var configs = await _db.TransitOfficeMandateConfigs.AsNoTracking()
            .ToDictionaryAsync(c => c.TransitOfficeId, ct)
            .ConfigureAwait(false);

        // Solo OT dados de alta en FLIT y con tenant activo (mismo criterio que listado
        // SuperAdmin de organismos → filtro «Activo»).
        var activeOfficeIds = (await _operationalStatus.ListAsync(ct).ConfigureAwait(false))
            .Where(o => o.HasTenant && o.EstadoActivo == true)
            .Select(o => o.Id)
            .ToHashSet();

        return _catalog.All
            .Where(o => activeOfficeIds.Contains(o.Id))
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(o => ToView(o, configs.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<MandateOtConfigView?> GetAsync(Guid officeId, CancellationToken ct = default)
    {
        var office = _catalog.GetById(officeId);
        if (office is null) return null;

        var cfg = await _db.TransitOfficeMandateConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TransitOfficeId == officeId, ct)
            .ConfigureAwait(false);

        return await AttachOtDefaultSignerNameAsync(ToView(office, cfg), ct).ConfigureAwait(false);
    }

    public async Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> UpsertAsync(
        Guid officeId,
        UpsertMandateOtConfigRequest request,
        Guid? userId,
        CancellationToken ct = default)
    {
        var office = _catalog.GetById(officeId);
        if (office is null) return (MandateConfigWriteStatus.OfficeNotFound, null);

        var template = (request.TemplateCode ?? string.Empty).Trim().ToLowerInvariant();
        if (!MandatoTemplateResolver.IsAcceptedTemplateCode(template))
            return (MandateConfigWriteStatus.InvalidTemplate, null);

        var family = (request.MandataryFamily ?? string.Empty).Trim().ToLowerInvariant();
        if (!Families.Contains(family))
            return (MandateConfigWriteStatus.InvalidFamily, null);

        // HU #13161 — se valida el valor ENVIADO: Resolve() convierte cualquier texto desconocido en «signer», así
        // que validar después de resolver aceptaba un tipo inventado y lo guardaba como Persona natural. Un
        // valor ausente sigue significando «signer» (clientes anteriores que no lo envían).
        var rawAssignmentMode = request.AssignmentMode?.Trim();
        if (!string.IsNullOrEmpty(rawAssignmentMode) && !AssignmentModes.Contains(rawAssignmentMode))
            return (MandateConfigWriteStatus.InvalidAssignmentMode, null);
        var assignmentMode = MandatoAssignmentModeCodes.Resolve(rawAssignmentMode);

        // Datos institucionales del OT (texto de plantilla); el tipo de negocio vive en company_ot_mandate_rules.
        if (string.Equals(family, MandatoFamiliaCodes.OrganismoTransito, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.InstitutionalMandataryName))
        {
            return (MandateConfigWriteStatus.InstitutionalRequired, null);
        }

        var (entity, conflict) = await GetOrCreateEntityAsync(officeId, request.RowVersion, userId, ct)
            .ConfigureAwait(false);
        if (conflict) return (MandateConfigWriteStatus.Conflict, null);

        entity.TemplateCode = template;
        entity.RequiresForNaturalPerson = true;
        entity.MandataryFamily = family;
        // Se conserva por compatibilidad; la resolución en trámite usa la regla compañía×OT.
        entity.AssignmentMode = assignmentMode;
        entity.InstitutionalMandataryName = NullIfEmpty(request.InstitutionalMandataryName);
        entity.InstitutionalMandataryNit = NullIfEmpty(request.InstitutionalMandataryNit);
        entity.ChamberCity = NullIfEmpty(request.ChamberCity);
        entity.MandatarySigla = NullIfEmpty(request.MandatarySigla);
        // La plantilla y el firmante son escrituras distintas: guardar redacción no pisa el mandatario general.
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = userId;

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (MandateConfigWriteStatus.Conflict, null);
        }

        // El trigger trg_row_version incrementa en BD; hay que refrescar o el cliente reenvía un token viejo → 409.
        await _db.Entry(entity).ReloadAsync(ct).ConfigureAwait(false);
        return (MandateConfigWriteStatus.Ok, await AttachOtDefaultSignerNameAsync(ToView(office, entity), ct).ConfigureAwait(false));
    }

    public async Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> SetOtDefaultSignerAsync(
        Guid officeId,
        SetOtDefaultSignerRequest request,
        Guid? userId,
        CancellationToken ct = default)
    {
        var office = _catalog.GetById(officeId);
        if (office is null) return (MandateConfigWriteStatus.OfficeNotFound, null);

        Guid? otDefault = null;
        if (request.DefaultMandateSignerId is { } candidate && candidate != Guid.Empty)
        {
            var ok = await ExecuteCrossTenantReadAsync(
                () => IsValidOtDefaultSignerAsync(officeId, candidate, ct),
                ct).ConfigureAwait(false);
            if (!ok)
                return (MandateConfigWriteStatus.InvalidDefaultSigner, null);
            otDefault = candidate;
        }

        var (entity, conflict) = await GetOrCreateForSignerAsync(officeId, request.RowVersion, userId, ct)
            .ConfigureAwait(false);
        if (conflict) return (MandateConfigWriteStatus.Conflict, null);

        entity.DefaultMandateSignerId = otDefault;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = userId;

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (MandateConfigWriteStatus.Conflict, null);
        }

        await _db.Entry(entity).ReloadAsync(ct).ConfigureAwait(false);
        return (MandateConfigWriteStatus.Ok, await AttachOtDefaultSignerNameAsync(ToView(office, entity), ct).ConfigureAwait(false));
    }

    public async Task<MandateConfigWriteStatus> DeleteAsync(Guid officeId, CancellationToken ct = default)
    {
        if (_catalog.GetById(officeId) is null)
            return MandateConfigWriteStatus.OfficeNotFound;

        var entity = await _db.TransitOfficeMandateConfigs
            .FirstOrDefaultAsync(c => c.TransitOfficeId == officeId, ct)
            .ConfigureAwait(false);

        if (entity is null)
            return MandateConfigWriteStatus.OfficeNotFound;

        _templateStorage.Delete(entity.CustomTemplateStoragePath);
        _db.TransitOfficeMandateConfigs.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return MandateConfigWriteStatus.Ok;
    }

    public async Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> UploadPdfTemplateAsync(
        Guid officeId,
        Stream content,
        string fileName,
        Guid? userId,
        CancellationToken ct = default)
    {
        var office = _catalog.GetById(officeId);
        if (office is null) return (MandateConfigWriteStatus.OfficeNotFound, null);

        if (content is null || !content.CanRead)
            return (MandateConfigWriteStatus.InvalidTemplateFile, null);

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct).ConfigureAwait(false);
        if (buffer.Length == 0 || buffer.Length > MaxPdfBytes)
            return (MandateConfigWriteStatus.InvalidTemplateFile, null);

        // Cabecera PDF mínima.
        buffer.Position = 0;
        Span<byte> header = stackalloc byte[5];
        _ = buffer.Read(header);
        if (header[0] != (byte)'%' || header[1] != (byte)'P' || header[2] != (byte)'D' || header[3] != (byte)'F')
            return (MandateConfigWriteStatus.InvalidTemplateFile, null);
        buffer.Position = 0;

        var (entity, _) = await GetOrCreateEntityAsync(officeId, expectedRowVersion: null, userId, ct)
            .ConfigureAwait(false);

        var previousPath = entity.CustomTemplateStoragePath;
        var stored = await _templateStorage
            .SavePdfAsync(officeId, fileName, buffer, ct)
            .ConfigureAwait(false);

        entity.CustomTemplateKind = MandatoCustomTemplateKindCodes.Pdf;
        entity.CustomTemplateStoragePath = stored.StoragePath;
        entity.CustomTemplateSha256 = stored.Sha256;
        entity.CustomTemplateFileName = string.IsNullOrWhiteSpace(fileName)
            ? "plantilla-mandato.pdf"
            : fileName.Trim();
        entity.CustomTemplateBody = null;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = userId;

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (MandateConfigWriteStatus.Conflict, null);
        }

        if (!string.IsNullOrWhiteSpace(previousPath)
            && !string.Equals(previousPath, stored.StoragePath, StringComparison.Ordinal))
        {
            _templateStorage.Delete(previousPath);
        }

        await _db.Entry(entity).ReloadAsync(ct).ConfigureAwait(false);
        return (MandateConfigWriteStatus.Ok, await AttachOtDefaultSignerNameAsync(ToView(office, entity), ct).ConfigureAwait(false));
    }

    public async Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> SaveEditorBodyAsync(
        Guid officeId,
        SaveMandateEditorBodyRequest request,
        Guid? userId,
        CancellationToken ct = default)
    {
        var office = _catalog.GetById(officeId);
        if (office is null) return (MandateConfigWriteStatus.OfficeNotFound, null);

        var body = request.Body?.Trim() ?? string.Empty;
        if (body.Length == 0 || body.Length > 100_000)
            return (MandateConfigWriteStatus.InvalidEditorBody, null);

        var (entity, conflict) = await GetOrCreateEntityAsync(officeId, request.RowVersion, userId, ct)
            .ConfigureAwait(false);
        if (conflict) return (MandateConfigWriteStatus.Conflict, null);

        var previousPath = entity.CustomTemplateStoragePath;
        entity.CustomTemplateKind = MandatoCustomTemplateKindCodes.Editor;
        entity.CustomTemplateBody = body;
        entity.CustomTemplateStoragePath = null;
        entity.CustomTemplateSha256 = null;
        entity.CustomTemplateFileName = null;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = userId;

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (MandateConfigWriteStatus.Conflict, null);
        }

        _templateStorage.Delete(previousPath);
        await _db.Entry(entity).ReloadAsync(ct).ConfigureAwait(false);
        return (MandateConfigWriteStatus.Ok, await AttachOtDefaultSignerNameAsync(ToView(office, entity), ct).ConfigureAwait(false));
    }

    public async Task<(MandateConfigWriteStatus Status, MandateOtConfigView? View)> DeleteCustomTemplateAsync(
        Guid officeId,
        Guid? userId,
        CancellationToken ct = default)
    {
        var office = _catalog.GetById(officeId);
        if (office is null) return (MandateConfigWriteStatus.OfficeNotFound, null);

        var entity = await _db.TransitOfficeMandateConfigs
            .FirstOrDefaultAsync(c => c.TransitOfficeId == officeId, ct)
            .ConfigureAwait(false);

        if (entity is null)
            return (MandateConfigWriteStatus.Ok, await AttachOtDefaultSignerNameAsync(ToView(office, null), ct).ConfigureAwait(false));

        _templateStorage.Delete(entity.CustomTemplateStoragePath);
        entity.CustomTemplateKind = MandatoCustomTemplateKindCodes.None;
        entity.CustomTemplateStoragePath = null;
        entity.CustomTemplateSha256 = null;
        entity.CustomTemplateFileName = null;
        entity.CustomTemplateBody = null;
        entity.CustomFieldManifest = null;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.UpdatedBy = userId;

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (MandateConfigWriteStatus.Conflict, null);
        }

        await _db.Entry(entity).ReloadAsync(ct).ConfigureAwait(false);
        return (MandateConfigWriteStatus.Ok, await AttachOtDefaultSignerNameAsync(ToView(office, entity), ct).ConfigureAwait(false));
    }

    public async Task<byte[]?> OpenCustomPdfAsync(Guid officeId, CancellationToken ct = default)
    {
        var cfg = await _db.TransitOfficeMandateConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TransitOfficeId == officeId, ct)
            .ConfigureAwait(false);

        if (cfg is null
            || !string.Equals(cfg.CustomTemplateKind, MandatoCustomTemplateKindCodes.Pdf, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(cfg.CustomTemplateStoragePath))
        {
            return null;
        }

        await using var stream = await _templateStorage
            .OpenReadAsync(cfg.CustomTemplateStoragePath, ct)
            .ConfigureAwait(false);
        if (stream is null) return null;

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }

    public async Task<IReadOnlyList<CompanyOtMandateRuleView>> ListCompanyRulesAsync(
        Guid officeId,
        OtCompanyVisibility visibility,
        CancellationToken ct = default)
    {
        if (_catalog.GetById(officeId) is null)
            return [];

        return await ExecuteCrossTenantReadAsync(
            async () =>
            {
                var grants = await ListOfficeCompaniesAsync(officeId, visibility, ct).ConfigureAwait(false);

                if (grants.Count == 0)
                    return (IReadOnlyList<CompanyOtMandateRuleView>)[];

                var otCfg = await _db.TransitOfficeMandateConfigs.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.TransitOfficeId == officeId, ct)
                    .ConfigureAwait(false);
                var inheritedMode = MandatoAssignmentModeCodes.ResolveEffective(
                    companyRuleMode: null,
                    otConfigMode: otCfg?.AssignmentMode,
                    otConfigExists: otCfg is not null);

                Dictionary<Guid, CompanyOtMandateRuleEntity> rules;
                try
                {
                    rules = await _db.CompanyOtMandateRules.AsNoTracking()
                        .Where(r => r.TransitOfficeId == officeId && grants.Contains(r.CompanyTenantId))
                        .ToDictionaryAsync(r => r.CompanyTenantId, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (IsMissingRelation(ex))
                {
                    // Migración 61 aún no aplicada: listar compañías con el modo heredado del OT.
                    rules = new Dictionary<Guid, CompanyOtMandateRuleEntity>();
                }

                var tenants = await _db.Tenants.AsNoTracking()
                    .Where(t => grants.Contains(t.Id))
                    .OrderBy(t => t.LegalName)
                    .Select(t => new { t.Id, t.LegalName, t.TaxId, t.Code })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                var signerIds = rules.Values
                    .Where(r => r.DefaultMandateSignerId is { } sid && sid != Guid.Empty)
                    .Select(r => r.DefaultMandateSignerId!.Value)
                    .Distinct()
                    .ToList();

                Dictionary<Guid, MandateSignerSnapshot> signers = [];
                if (signerIds.Count > 0)
                {
                    signers = await _db.MandateSigners.AsNoTracking()
                        .Where(s => signerIds.Contains(s.Id))
                        .Select(s => new MandateSignerSnapshot(
                            s.Id, s.FullName, s.DocumentType, s.DocumentNumber ?? string.Empty, s.IntegrityHash))
                        .ToDictionaryAsync(s => s.Id, ct)
                        .ConfigureAwait(false);
                }

                return (IReadOnlyList<CompanyOtMandateRuleView>)tenants
                    .Select(t =>
                    {
                        if (rules.TryGetValue(t.Id, out var rule))
                        {
                            signers.TryGetValue(rule.DefaultMandateSignerId ?? Guid.Empty, out var snapshot);
                            return MapCompanyRule(
                                t.Id, t.LegalName, t.TaxId, t.Code,
                                MandatoAssignmentModeCodes.Resolve(rule.AssignmentMode),
                                rule.MandataryFamily,
                                rule.InstitutionalMandataryName,
                                rule.InstitutionalMandataryNit,
                                rule.ChamberCity,
                                rule.MandatarySigla,
                                hasExplicitRule: true,
                                rule.DefaultMandateSignerId,
                                snapshot,
                                rule.RowVersion);
                        }

                        return MapCompanyRule(
                            t.Id, t.LegalName, t.TaxId, t.Code,
                            inheritedMode,
                            MandatoFamiliaCodes.Individuo,
                            null, null, null, null,
                            hasExplicitRule: false,
                            defaultSignerId: null,
                            snapshot: null);
                    })
                    .ToList();
            },
            ct).ConfigureAwait(false);
    }

    public async Task<(MandateConfigWriteStatus Status, CompanyOtMandateRuleView? View)> UpsertCompanyRuleAsync(
        Guid officeId,
        Guid companyTenantId,
        UpsertCompanyOtMandateRuleRequest request,
        Guid? userId,
        MandateRuleTypeChange? change = null,
        CancellationToken ct = default)
    {
        if (_catalog.GetById(officeId) is null)
            return (MandateConfigWriteStatus.OfficeNotFound, null);

        // HU #13154 — se valida el valor ENVIADO: Resolve() convierte cualquier texto desconocido en «signer»,
        // así que validar después de resolver aceptaba un tipo inventado y lo guardaba como Persona natural.
        var rawMode = request.AssignmentMode?.Trim();
        if (string.IsNullOrEmpty(rawMode) || !AssignmentModes.Contains(rawMode))
            return (MandateConfigWriteStatus.InvalidAssignmentMode, null);
        var mode = MandatoAssignmentModeCodes.Resolve(rawMode);

        var family = string.IsNullOrWhiteSpace(request.MandataryFamily)
            ? MandatoFamiliaCodes.Individuo
            : request.MandataryFamily.Trim().ToLowerInvariant();
        if (!Families.Contains(family))
            return (MandateConfigWriteStatus.InvalidFamily, null);

        if (mode == MandatoAssignmentModeCodes.Institutional
            && string.IsNullOrWhiteSpace(request.InstitutionalMandataryName))
        {
            return (MandateConfigWriteStatus.InstitutionalRequired, null);
        }

        var hasGrant = await CompanyCanUseOfficeAsync(
            officeId, companyTenantId, OtCompanyVisibility.WholeNetwork, ct).ConfigureAwait(false);

        if (!hasGrant)
            return (MandateConfigWriteStatus.CompanyNotFound, null);

        var company = await ExecuteCrossTenantReadAsync(
            async () => await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == companyTenantId)
                .Select(t => new { t.LegalName, t.TaxId, t.Code })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false),
            ct).ConfigureAwait(false);

        if (company is null || string.IsNullOrWhiteSpace(company.LegalName))
            return (MandateConfigWriteStatus.CompanyNotFound, null);

        Guid? defaultSignerId = null;
        if (mode == MandatoAssignmentModeCodes.Signer && request.DefaultMandateSignerId is { } candidate)
        {
            var ok = await ExecuteCrossTenantReadAsync(
                () => IsValidDefaultSignerAsync(officeId, companyTenantId, candidate, ct),
                ct).ConfigureAwait(false);
            if (!ok)
                return (MandateConfigWriteStatus.InvalidDefaultSigner, null);
            defaultSignerId = candidate;
        }

        var now = DateTimeOffset.UtcNow;
        var entity = await _db.CompanyOtMandateRules
            .FirstOrDefaultAsync(
                r => r.TransitOfficeId == officeId && r.CompanyTenantId == companyTenantId,
                ct)
            .ConfigureAwait(false);

        // HU #13148 — alta: sin versión (si llega una, la regla ya no existe: otro usuario la restableció).
        // Cambio: la versión es obligatoria y debe ser la vigente; si no, nada se escribe.
        if (entity is null ? request.RowVersion is not null : request.RowVersion != entity.RowVersion)
            return (MandateConfigWriteStatus.Conflict, null);

        // HU #13149 — tipo vigente ANTES de escribir (propio, o el heredado del OT si aún no hay regla).
        if (change is not null)
        {
            change.HadExplicitRule = entity is not null;
            change.PreviousMode = entity is not null
                ? MandatoAssignmentModeCodes.Resolve(entity.AssignmentMode)
                : await ResolveInheritedModeAsync(officeId, ct).ConfigureAwait(false);
            change.NewMode = mode;
        }

        if (entity is null)
        {
            entity = new CompanyOtMandateRuleEntity
            {
                Id = Guid.NewGuid(),
                CompanyTenantId = companyTenantId,
                TransitOfficeId = officeId,
                CreatedAt = now,
                CreatedBy = userId,
            };
            _db.CompanyOtMandateRules.Add(entity);
        }
        else
        {
            entity.UpdatedAt = now;
            entity.UpdatedBy = userId;
        }

        entity.AssignmentMode = mode;
        entity.MandataryFamily = family;
        entity.InstitutionalMandataryName = mode == MandatoAssignmentModeCodes.Institutional
            ? NullIfEmpty(request.InstitutionalMandataryName)
            : null;
        entity.InstitutionalMandataryNit = mode == MandatoAssignmentModeCodes.Institutional
            ? NullIfEmpty(request.InstitutionalMandataryNit)
            : null;
        entity.ChamberCity = NullIfEmpty(request.ChamberCity);
        entity.MandatarySigla = NullIfEmpty(request.MandatarySigla);
        entity.DefaultMandateSignerId = defaultSignerId;

        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (MandateConfigWriteStatus.Conflict, null);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Dos altas simultáneas de la primera regla: la segunda pierde la carrera (uq_company_ot_mandate_rules).
            return (MandateConfigWriteStatus.Conflict, null);
        }

        if (change is not null)
            change.Applied = true;

        // El trigger incrementa row_version en BD; hay que refrescar o el cliente reenvía un token viejo: 409.
        await _db.Entry(entity).ReloadAsync(ct).ConfigureAwait(false);

        MandateSignerSnapshot? snapshot = null;
        if (entity.DefaultMandateSignerId is { } savedSigner && savedSigner != Guid.Empty)
        {
            snapshot = await ExecuteCrossTenantReadAsync(
                () => _db.MandateSigners.AsNoTracking()
                    .Where(s => s.Id == savedSigner)
                    .Select(s => new MandateSignerSnapshot(
                        s.Id, s.FullName, s.DocumentType, s.DocumentNumber ?? string.Empty, s.IntegrityHash))
                    .FirstOrDefaultAsync(ct),
                ct).ConfigureAwait(false);
        }

        return (MandateConfigWriteStatus.Ok, MapCompanyRule(
            companyTenantId,
            company.LegalName,
            company.TaxId,
            company.Code,
            mode,
            family,
            entity.InstitutionalMandataryName,
            entity.InstitutionalMandataryNit,
            entity.ChamberCity,
            entity.MandatarySigla,
            hasExplicitRule: true,
            entity.DefaultMandateSignerId,
            snapshot,
            entity.RowVersion));
    }

    public async Task<(MandateConfigWriteStatus Status, CompanyOtMandateRuleView? View)> SetCompanyDefaultSignerAsync(
        Guid officeId,
        Guid companyTenantId,
        SetCompanyDefaultSignerRequest request,
        Guid? userId,
        OtCompanyVisibility visibility,
        CancellationToken ct = default)
    {
        if (_catalog.GetById(officeId) is null)
            return (MandateConfigWriteStatus.OfficeNotFound, null);

        var hasGrant = await CompanyCanUseOfficeAsync(officeId, companyTenantId, visibility, ct)
            .ConfigureAwait(false);

        if (!hasGrant)
            return (MandateConfigWriteStatus.CompanyNotFound, null);

        var company = await ExecuteCrossTenantReadAsync(
            async () => await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == companyTenantId)
                .Select(t => new { t.LegalName, t.TaxId, t.Code })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false),
            ct).ConfigureAwait(false);

        if (company is null || string.IsNullOrWhiteSpace(company.LegalName))
            return (MandateConfigWriteStatus.CompanyNotFound, null);

        Guid? defaultSignerId = null;
        if (request.DefaultMandateSignerId is { } candidate && candidate != Guid.Empty)
        {
            var ok = await ExecuteCrossTenantReadAsync(
                () => IsValidDefaultSignerAsync(officeId, companyTenantId, candidate, ct),
                ct).ConfigureAwait(false);
            if (!ok)
                return (MandateConfigWriteStatus.InvalidDefaultSigner, null);
            defaultSignerId = candidate;
        }

        var entity = await _db.CompanyOtMandateRules
            .FirstOrDefaultAsync(
                r => r.TransitOfficeId == officeId && r.CompanyTenantId == companyTenantId,
                ct)
            .ConfigureAwait(false);

        // HU #13148 — opcional aquí (el hub del OT no lo envía): si llega y no es el vigente, 409 sin escribir.
        if (request.RowVersion is { } expected && (entity is null || entity.RowVersion != expected))
            return (MandateConfigWriteStatus.Conflict, null);

        if (defaultSignerId is null)
        {
            if (entity is not null)
            {
                _db.CompanyOtMandateRules.Remove(entity);
                try
                {
                    await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return (MandateConfigWriteStatus.Conflict, null);
                }
            }

            var otCfg = await _db.TransitOfficeMandateConfigs.AsNoTracking()
                .FirstOrDefaultAsync(c => c.TransitOfficeId == officeId, ct)
                .ConfigureAwait(false);
            var inheritedMode = MandatoAssignmentModeCodes.ResolveEffective(
                    companyRuleMode: null,
                    otConfigMode: otCfg?.AssignmentMode,
                    otConfigExists: otCfg is not null);
            return (MandateConfigWriteStatus.Ok, MapCompanyRule(
                companyTenantId,
                company.LegalName,
                company.TaxId,
                company.Code,
                inheritedMode,
                MandatoFamiliaCodes.Individuo,
                null, null, null, null,
                hasExplicitRule: false,
                defaultSignerId: null,
                snapshot: null));
        }

        var now = DateTimeOffset.UtcNow;
        if (entity is null)
        {
            var otCfg = await _db.TransitOfficeMandateConfigs.AsNoTracking()
                .FirstOrDefaultAsync(c => c.TransitOfficeId == officeId, ct)
                .ConfigureAwait(false);
            var inheritedMode = MandatoAssignmentModeCodes.ResolveEffective(
                    companyRuleMode: null,
                    otConfigMode: otCfg?.AssignmentMode,
                    otConfigExists: otCfg is not null);
            entity = new CompanyOtMandateRuleEntity
            {
                Id = Guid.NewGuid(),
                CompanyTenantId = companyTenantId,
                TransitOfficeId = officeId,
                AssignmentMode = inheritedMode,
                MandataryFamily = string.IsNullOrWhiteSpace(otCfg?.MandataryFamily)
                    ? MandatoFamiliaCodes.Individuo
                    : otCfg.MandataryFamily,
                ChamberCity = otCfg?.ChamberCity,
                MandatarySigla = otCfg?.MandatarySigla,
                CreatedAt = now,
                CreatedBy = userId,
            };
            _db.CompanyOtMandateRules.Add(entity);
        }
        else
        {
            entity.UpdatedAt = now;
            entity.UpdatedBy = userId;
        }

        entity.DefaultMandateSignerId = defaultSignerId;
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (MandateConfigWriteStatus.Conflict, null);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return (MandateConfigWriteStatus.Conflict, null);
        }

        await _db.Entry(entity).ReloadAsync(ct).ConfigureAwait(false);

        MandateSignerSnapshot? snapshot = await ExecuteCrossTenantReadAsync(
            () => _db.MandateSigners.AsNoTracking()
                .Where(s => s.Id == defaultSignerId)
                .Select(s => new MandateSignerSnapshot(
                    s.Id, s.FullName, s.DocumentType, s.DocumentNumber ?? string.Empty, s.IntegrityHash))
                .FirstOrDefaultAsync(ct),
            ct).ConfigureAwait(false);

        return (MandateConfigWriteStatus.Ok, MapCompanyRule(
            companyTenantId,
            company.LegalName,
            company.TaxId,
            company.Code,
            entity.AssignmentMode,
            entity.MandataryFamily,
            entity.InstitutionalMandataryName,
            entity.InstitutionalMandataryNit,
            entity.ChamberCity,
            entity.MandatarySigla,
            hasExplicitRule: true,
            entity.DefaultMandateSignerId,
            snapshot,
            entity.RowVersion));
    }

    private async Task<bool> IsValidOtDefaultSignerAsync(
        Guid officeId,
        Guid mandateSignerId,
        CancellationToken ct)
    {
        var signerOk = await _db.MandateSigners.AsNoTracking()
            .AnyAsync(s => s.Id == mandateSignerId && s.IsActive && s.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (!signerOk)
            return false;

        var primaryOffice = await _db.MandateSigners.AsNoTracking()
            .AnyAsync(s => s.Id == mandateSignerId && s.TransitOfficeId == officeId, ct)
            .ConfigureAwait(false);
        if (primaryOffice)
            return true;

        return await _db.MandateSignerTransitOffices.AsNoTracking()
            .AnyAsync(
                l => l.MandateSignerId == mandateSignerId
                    && l.TransitOfficeId == officeId
                    && l.IsActive,
                ct)
            .ConfigureAwait(false);
    }

    private async Task<bool> IsValidDefaultSignerAsync(
        Guid officeId,
        Guid companyTenantId,
        Guid mandateSignerId,
        CancellationToken ct)
    {
        var signerOk = await _db.MandateSigners.AsNoTracking()
            .AnyAsync(s => s.Id == mandateSignerId && s.IsActive && s.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (!signerOk)
            return false;

        var companyOk = await _db.MandateSignerCompanies.AsNoTracking()
            .AnyAsync(
                c => c.MandateSignerId == mandateSignerId
                    && c.CompanyTenantId == companyTenantId
                    && c.IsActive,
                ct)
            .ConfigureAwait(false);
        if (!companyOk)
            return false;

        var primaryOffice = await _db.MandateSigners.AsNoTracking()
            .AnyAsync(s => s.Id == mandateSignerId && s.TransitOfficeId == officeId, ct)
            .ConfigureAwait(false);
        if (primaryOffice)
            return true;

        return await _db.MandateSignerTransitOffices.AsNoTracking()
            .AnyAsync(
                l => l.MandateSignerId == mandateSignerId
                    && l.TransitOfficeId == officeId
                    && l.IsActive,
                ct)
            .ConfigureAwait(false);
    }

    public async Task<MandateConfigWriteStatus> DeleteCompanyRuleAsync(
        Guid officeId,
        Guid companyTenantId,
        OtCompanyVisibility visibility,
        long? expectedRowVersion = null,
        MandateRuleTypeChange? change = null,
        CancellationToken ct = default)
    {
        if (_catalog.GetById(officeId) is null)
            return MandateConfigWriteStatus.OfficeNotFound;

        // Bug #12912 (Ley 1581) — el organismo no borra reglas de compañías que no puede ver.
        if (visibility == OtCompanyVisibility.DirectOrWithReceivedProcedures
            && !await CompanyCanUseOfficeAsync(officeId, companyTenantId, visibility, ct).ConfigureAwait(false))
        {
            return MandateConfigWriteStatus.CompanyNotFound;
        }

        var entity = await _db.CompanyOtMandateRules
            .FirstOrDefaultAsync(
                r => r.TransitOfficeId == officeId && r.CompanyTenantId == companyTenantId,
                ct)
            .ConfigureAwait(false);

        if (entity is null)
            return MandateConfigWriteStatus.Ok;

        // HU #13148 — opcional: si llega y la regla ya cambió, 409 y no se borra.
        if (expectedRowVersion is { } expected && entity.RowVersion != expected)
            return MandateConfigWriteStatus.Conflict;

        if (change is not null)
        {
            change.HadExplicitRule = true;
            change.PreviousMode = MandatoAssignmentModeCodes.Resolve(entity.AssignmentMode);
            change.NewMode = await ResolveInheritedModeAsync(officeId, ct).ConfigureAwait(false);
        }

        _db.CompanyOtMandateRules.Remove(entity);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return MandateConfigWriteStatus.Conflict;
        }

        if (change is not null)
            change.Applied = true;

        return MandateConfigWriteStatus.Ok;
    }

    private static bool IsMissingRelation(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            var msg = e.Message ?? string.Empty;
            if (msg.Contains("company_ot_mandate_rules", StringComparison.OrdinalIgnoreCase)
                && (msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("no existe", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("42P01", StringComparison.Ordinal)))
            {
                return true;
            }

            // Npgsql SqlState 42P01 (undefined_table)
            var sqlState = e.GetType().GetProperty("SqlState")?.GetValue(e) as string;
            if (sqlState == "42P01")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Bug #12912 — la compañía puede radicar en el OT: el organismo está en su lista efectiva de red
    /// (HU #12347). Sin resolver inyectado, criterio previo de grant propio habilitado. Con la vista del
    /// organismo (Ley 1581), además tiene que ser una compañía que el organismo puede ver por nombre.
    /// </summary>
    private Task<bool> CompanyCanUseOfficeAsync(
        Guid officeId,
        Guid companyTenantId,
        OtCompanyVisibility visibility,
        CancellationToken ct) =>
        ExecuteCrossTenantReadAsync(
            async () => visibility == OtCompanyVisibility.DirectOrWithReceivedProcedures
                ? (await ListOfficeCompaniesAsync(officeId, visibility, ct).ConfigureAwait(false))
                    .Contains(companyTenantId)
                : _effectiveOffices is not null
                    ? (await _effectiveOffices.ListEffectiveOfficeIdsAsync(companyTenantId, ct).ConfigureAwait(false))
                        .Contains(officeId)
                    : await _db.TenantTransitOfficeGrants.AsNoTracking()
                        .AnyAsync(
                            g => g.TransitOfficeId == officeId
                                && g.TenantId == companyTenantId
                                && g.IsEnabled,
                            ct)
                        .ConfigureAwait(false),
            ct);

    /// <summary>
    /// Compañías del OT según la lista efectiva de red (Bug #12912); con la vista del organismo,
    /// acotadas por la regla única de Ley 1581 (<see cref="OtVisibleCompanies"/>). Se llama dentro de
    /// una lectura cross-tenant.
    /// </summary>
    private async Task<List<Guid>> ListOfficeCompaniesAsync(
        Guid officeId,
        OtCompanyVisibility visibility,
        CancellationToken ct)
    {
        var effective = _effectiveOffices is not null
            ? [.. await _effectiveOffices.ListEffectiveTenantIdsForOfficeAsync(officeId, ct).ConfigureAwait(false)]
            : await _db.TenantTransitOfficeGrants.AsNoTracking()
                .Where(g => g.TransitOfficeId == officeId && g.IsEnabled)
                .Select(g => g.TenantId)
                .ToListAsync(ct)
                .ConfigureAwait(false);

        return visibility == OtCompanyVisibility.DirectOrWithReceivedProcedures
            ? [.. await OtVisibleCompanies.FilterAsync(_db, officeId, effective, ct).ConfigureAwait(false)]
            : effective;
    }

    private async Task<T> ExecuteCrossTenantReadAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_db.Database.IsRelational())
            return await action().ConfigureAwait(false);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await _db.Database
                .ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken)
                .ConfigureAwait(false);
            var result = await action().ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }).ConfigureAwait(false);
    }

    private async Task<(TransitOfficeMandateConfigEntity Entity, bool Conflict)> GetOrCreateForSignerAsync(
        Guid officeId,
        long? expectedRowVersion,
        Guid? userId,
        CancellationToken ct)
    {
        var entity = await _db.TransitOfficeMandateConfigs
            .FirstOrDefaultAsync(c => c.TransitOfficeId == officeId, ct)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        if (entity is null)
        {
            entity = new TransitOfficeMandateConfigEntity
            {
                Id = Guid.NewGuid(),
                TransitOfficeId = officeId,
                TemplateCode = MandatoTemplateResolver.Auto,
                RequiresForNaturalPerson = true,
                MandataryFamily = MandatoFamiliaCodes.Individuo,
                AssignmentMode = MandatoAssignmentModeCodes.Signer,
                CustomTemplateKind = MandatoCustomTemplateKindCodes.None,
                DefaultMandateSignerId = null,
                CreatedAt = now,
                CreatedBy = userId,
            };
            _db.TransitOfficeMandateConfigs.Add(entity);
            return (entity, false);
        }

        if (expectedRowVersion is { } expected && entity.RowVersion != expected)
            return (entity, true);

        return (entity, false);
    }

    private async Task<(TransitOfficeMandateConfigEntity Entity, bool Conflict)> GetOrCreateEntityAsync(
        Guid officeId,
        long? expectedRowVersion,
        Guid? userId,
        CancellationToken ct)
    {
        var entity = await _db.TransitOfficeMandateConfigs
            .FirstOrDefaultAsync(c => c.TransitOfficeId == officeId, ct)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        if (entity is null)
        {
            var officeCode = await _db.TransitOffices.AsNoTracking()
                .Where(o => o.Id == officeId)
                .Select(o => o.Code)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            var birth = MandatoOtBirthDefaults.ForOffice(officeCode);
            entity = new TransitOfficeMandateConfigEntity
            {
                Id = Guid.NewGuid(),
                TransitOfficeId = officeId,
                TemplateCode = birth.TemplateCode,
                RequiresForNaturalPerson = birth.RequiresForNaturalPerson,
                MandataryFamily = birth.MandataryFamily,
                AssignmentMode = birth.AssignmentMode,
                InstitutionalMandataryName = birth.InstitutionalMandataryName,
                InstitutionalMandataryNit = birth.InstitutionalMandataryNit,
                ChamberCity = birth.ChamberCity,
                MandatarySigla = birth.MandatarySigla,
                CustomTemplateKind = MandatoCustomTemplateKindCodes.None,
                DefaultMandateSignerId = null,
                CreatedAt = now,
                CreatedBy = userId,
            };
            _db.TransitOfficeMandateConfigs.Add(entity);
            return (entity, false);
        }

        if (expectedRowVersion is { } expected && entity.RowVersion != expected)
            return (entity, true);

        return (entity, false);
    }

    private static MandateOtConfigView ToView(TransitOfficeEntry office, TransitOfficeMandateConfigEntity? cfg)
    {
        var builtin = MandatoSystemOfficeTemplates.TryGetByOfficeCode(office.Code);

        if (cfg is null)
        {
            return new MandateOtConfigView(
                office.Id,
                office.Code,
                office.Name,
                MandatoSystemOfficeTemplates.ResolveTemplateCode(office.Code, null, null),
                builtin?.RequiresForNaturalPerson ?? true,
                builtin?.MandataryFamily ?? MandatoFamiliaCodes.Individuo,
                builtin?.InstitutionalMandataryName,
                builtin?.InstitutionalMandataryNit,
                builtin?.ChamberCity,
                builtin?.MandatarySigla,
                HasExplicitConfig: false,
                RowVersion: null,
                MandatoAssignmentModeCodes.Signer,
                MandatoCustomTemplateKindCodes.None,
                null,
                null,
                HasCustomTemplate: false,
                // Sin fila no hay elección: el organismo sigue a su plantilla de sistema.
                ConfiguredTemplateCode: MandatoTemplateResolver.Auto,
                DefaultMandateSignerId: null);
        }

        var kind = MandatoCustomTemplateKindCodes.Resolve(cfg.CustomTemplateKind);
        var hasCustom = MandatoCustomTemplateKindCodes.HasCustom(kind);
        var templateCode = MandatoSystemOfficeTemplates.ResolveTemplateCode(
            office.Code, cfg.TemplateCode, cfg.CustomTemplateKind);

        return new MandateOtConfigView(
            office.Id,
            office.Code,
            office.Name,
            templateCode,
            cfg.RequiresForNaturalPerson || (builtin?.RequiresForNaturalPerson ?? false),
            string.IsNullOrWhiteSpace(cfg.MandataryFamily)
                ? (builtin?.MandataryFamily ?? MandatoFamiliaCodes.Individuo)
                : cfg.MandataryFamily,
            cfg.InstitutionalMandataryName ?? builtin?.InstitutionalMandataryName,
            cfg.InstitutionalMandataryNit ?? builtin?.InstitutionalMandataryNit,
            cfg.ChamberCity ?? builtin?.ChamberCity,
            cfg.MandatarySigla ?? builtin?.MandatarySigla,
            HasExplicitConfig: true,
            cfg.RowVersion,
            MandatoAssignmentModeCodes.Resolve(cfg.AssignmentMode),
            kind,
            cfg.CustomTemplateFileName,
            cfg.CustomTemplateBody,
            hasCustom,
            ConfiguredTemplateCode: MandatoTemplateResolver.IsAuto(cfg.TemplateCode)
                ? MandatoTemplateResolver.Auto
                : cfg.TemplateCode.Trim().ToLowerInvariant(),
            DefaultMandateSignerId: cfg.DefaultMandateSignerId);
    }

    private sealed record MandateSignerSnapshot(
        Guid Id,
        string FullName,
        string DocumentType,
        string DocumentNumber,
        string IntegrityHash);

    private async Task<MandateOtConfigView> AttachOtDefaultSignerNameAsync(
        MandateOtConfigView view,
        CancellationToken ct)
    {
        if (view.DefaultMandateSignerId is not { } id || id == Guid.Empty)
            return view;

        var snapshot = await ExecuteCrossTenantReadAsync(
            () => _db.MandateSigners.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new MandateSignerSnapshot(
                    s.Id, s.FullName, s.DocumentType, s.DocumentNumber ?? string.Empty, s.IntegrityHash))
                .FirstOrDefaultAsync(ct),
            ct).ConfigureAwait(false);

        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.FullName))
            return view;

        return view with
        {
            DefaultMandateSignerName = snapshot.FullName,
            DefaultMandateSignerDocumentType = snapshot.DocumentType,
            DefaultMandateSignerDocumentNumber = snapshot.DocumentNumber,
            DefaultMandateSignerIntegrityHash = snapshot.IntegrityHash,
        };
    }

    private static CompanyOtMandateRuleView MapCompanyRule(
        Guid companyTenantId,
        string companyName,
        string? taxId,
        string? code,
        string assignmentMode,
        string family,
        string? institutionalName,
        string? institutionalNit,
        string? chamberCity,
        string? sigla,
        bool hasExplicitRule,
        Guid? defaultSignerId,
        MandateSignerSnapshot? snapshot,
        long? rowVersion = null) =>
        new(
            companyTenantId,
            companyName,
            assignmentMode,
            family,
            institutionalName,
            institutionalNit,
            chamberCity,
            sigla,
            hasExplicitRule,
            defaultSignerId,
            NullIfEmpty(taxId),
            NullIfEmpty(code),
            snapshot?.FullName,
            snapshot?.DocumentType,
            snapshot?.DocumentNumber,
            snapshot?.IntegrityHash,
            hasExplicitRule ? rowVersion : null);

    /// <summary>Tipo que hereda una compañía sin regla propia: el del OT (o el de nacimiento si no hay fila).</summary>
    private async Task<string> ResolveInheritedModeAsync(Guid officeId, CancellationToken ct)
    {
        var otCfg = await _db.TransitOfficeMandateConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TransitOfficeId == officeId, ct)
            .ConfigureAwait(false);
        return MandatoAssignmentModeCodes.ResolveEffective(
            companyRuleMode: null,
            otConfigMode: otCfg?.AssignmentMode,
            otConfigExists: otCfg is not null);
    }

    /// <summary>Violación de unicidad (SQLSTATE 23505) de Npgsql, sin referenciar el proveedor.</summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            if (e.GetType().GetProperty("SqlState")?.GetValue(e) as string == "23505")
                return true;
        }

        return false;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
