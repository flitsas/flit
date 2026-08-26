using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
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

    private static readonly HashSet<string> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        // "auto" no es una redacción: delega en la plantilla de sistema del organismo. Es la forma de
        // devolverle la decisión al builtin ahora que la elección explícita le gana (HU #11703).
        MandatoTemplateResolver.Auto,
        MandatoTemplateResolver.Generico,
        MandatoTemplateResolver.Sabaneta,
        MandatoTemplateResolver.Bello,
        MandatoTemplateResolver.Municipio,
    };

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
    private readonly IDocumentOcrAnalyzer _ocr;
    private readonly IMandateTemplateStorage _templateStorage;

    public MandateConfigAdminService(
        FlitDbContext db,
        ITransitOfficeCatalog catalog,
        ITransitOfficeOperationalStatusReader operationalStatus,
        IDocumentOcrAnalyzer ocr,
        IMandateTemplateStorage templateStorage)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _operationalStatus = operationalStatus ?? throw new ArgumentNullException(nameof(operationalStatus));
        _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
        _templateStorage = templateStorage ?? throw new ArgumentNullException(nameof(templateStorage));
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

        return ToView(office, cfg);
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
        if (!Templates.Contains(template))
            return (MandateConfigWriteStatus.InvalidTemplate, null);

        var family = (request.MandataryFamily ?? string.Empty).Trim().ToLowerInvariant();
        if (!Families.Contains(family))
            return (MandateConfigWriteStatus.InvalidFamily, null);

        var assignmentMode = MandatoAssignmentModeCodes.Resolve(request.AssignmentMode);
        if (!AssignmentModes.Contains(assignmentMode))
            return (MandateConfigWriteStatus.InvalidAssignmentMode, null);

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
        return (MandateConfigWriteStatus.Ok, ToView(office, entity));
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

    public async Task<MandateConfigExtractResult> ExtractAsync(
        ReadOnlyMemory<byte> content,
        string mediaType,
        CancellationToken ct = default)
    {
        var analysis = await _ocr
            .AnalyzeAsync(MandatoConfigOcr.Tipo, content, mediaType, ct)
            .ConfigureAwait(false);

        if (!analysis.Ok || analysis.Data is null)
        {
            return new MandateConfigExtractResult(
                MandatoTemplateResolver.Generico,
                false,
                MandatoFamiliaCodes.Individuo,
                null, null, null, null,
                analysis.Message ?? "No se pudo extraer información del documento.",
                MandatoAssignmentModeCodes.Signer);
        }

        return MandatoConfigOcr.Parse(analysis.Data);
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
        return (MandateConfigWriteStatus.Ok, ToView(office, entity));
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
        return (MandateConfigWriteStatus.Ok, ToView(office, entity));
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
            return (MandateConfigWriteStatus.Ok, ToView(office, null));

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
        return (MandateConfigWriteStatus.Ok, ToView(office, entity));
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
        CancellationToken ct = default)
    {
        if (_catalog.GetById(officeId) is null)
            return [];

        return await ExecuteCrossTenantReadAsync(
            async () =>
            {
                var grants = await _db.TenantTransitOfficeGrants.AsNoTracking()
                    .Where(g => g.TransitOfficeId == officeId && g.IsEnabled)
                    .Select(g => g.TenantId)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                if (grants.Count == 0)
                    return (IReadOnlyList<CompanyOtMandateRuleView>)[];

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
                    // Migración 61 aún no aplicada: listar compañías con default signer.
                    rules = new Dictionary<Guid, CompanyOtMandateRuleEntity>();
                }

                var tenants = await _db.Tenants.AsNoTracking()
                    .Where(t => grants.Contains(t.Id))
                    .OrderBy(t => t.LegalName)
                    .Select(t => new { t.Id, t.LegalName })
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return (IReadOnlyList<CompanyOtMandateRuleView>)tenants
                    .Select(t =>
                    {
                        if (rules.TryGetValue(t.Id, out var rule))
                        {
                            return new CompanyOtMandateRuleView(
                                t.Id,
                                t.LegalName,
                                MandatoAssignmentModeCodes.Resolve(rule.AssignmentMode),
                                rule.MandataryFamily,
                                rule.InstitutionalMandataryName,
                                rule.InstitutionalMandataryNit,
                                rule.ChamberCity,
                                rule.MandatarySigla,
                                HasExplicitRule: true,
                                rule.DefaultMandateSignerId);
                        }

                        return new CompanyOtMandateRuleView(
                            t.Id,
                            t.LegalName,
                            MandatoAssignmentModeCodes.Signer,
                            MandatoFamiliaCodes.Individuo,
                            null,
                            null,
                            null,
                            null,
                            HasExplicitRule: false,
                            DefaultMandateSignerId: null);
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
        CancellationToken ct = default)
    {
        if (_catalog.GetById(officeId) is null)
            return (MandateConfigWriteStatus.OfficeNotFound, null);

        var mode = MandatoAssignmentModeCodes.Resolve(request.AssignmentMode);
        if (!AssignmentModes.Contains(mode))
            return (MandateConfigWriteStatus.InvalidAssignmentMode, null);

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

        var hasGrant = await ExecuteCrossTenantReadAsync(
            () => _db.TenantTransitOfficeGrants.AsNoTracking()
                .AnyAsync(
                    g => g.TransitOfficeId == officeId
                        && g.TenantId == companyTenantId
                        && g.IsEnabled,
                    ct),
            ct).ConfigureAwait(false);

        if (!hasGrant)
            return (MandateConfigWriteStatus.CompanyNotFound, null);

        var companyName = await ExecuteCrossTenantReadAsync(
            async () => await _db.Tenants.AsNoTracking()
                .Where(t => t.Id == companyTenantId)
                .Select(t => t.LegalName)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false),
            ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(companyName))
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

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return (MandateConfigWriteStatus.Ok, new CompanyOtMandateRuleView(
            companyTenantId,
            companyName,
            mode,
            family,
            entity.InstitutionalMandataryName,
            entity.InstitutionalMandataryNit,
            entity.ChamberCity,
            entity.MandatarySigla,
            HasExplicitRule: true,
            entity.DefaultMandateSignerId));
    }

    private async Task<bool> IsValidDefaultSignerAsync(
        Guid officeId,
        Guid companyTenantId,
        Guid mandateSignerId,
        CancellationToken ct)
    {
        var signerOk = await _db.MandateSigners.AsNoTracking()
            .AnyAsync(s => s.Id == mandateSignerId && s.IsActive, ct)
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
        CancellationToken ct = default)
    {
        if (_catalog.GetById(officeId) is null)
            return MandateConfigWriteStatus.OfficeNotFound;

        var entity = await _db.CompanyOtMandateRules
            .FirstOrDefaultAsync(
                r => r.TransitOfficeId == officeId && r.CompanyTenantId == companyTenantId,
                ct)
            .ConfigureAwait(false);

        if (entity is null)
            return MandateConfigWriteStatus.Ok;

        _db.CompanyOtMandateRules.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
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
            entity = new TransitOfficeMandateConfigEntity
            {
                Id = Guid.NewGuid(),
                TransitOfficeId = officeId,
                TemplateCode = MandatoOtBirthDefaults.TemplateCode,
                RequiresForNaturalPerson = MandatoOtBirthDefaults.RequiresForNaturalPerson,
                MandataryFamily = MandatoOtBirthDefaults.MandataryFamily,
                AssignmentMode = MandatoOtBirthDefaults.AssignmentMode,
                InstitutionalMandataryName = null,
                InstitutionalMandataryNit = null,
                ChamberCity = null,
                MandatarySigla = null,
                CustomTemplateKind = MandatoCustomTemplateKindCodes.None,
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
                ConfiguredTemplateCode: MandatoTemplateResolver.Auto);
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
                : cfg.TemplateCode.Trim().ToLowerInvariant());
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Tipo OCR dedicado a extract de config (no entra al lote de trámites).</summary>
internal static class MandatoConfigOcr
{
    public const string Tipo = "mandato_config";

    public static MandateConfigExtractResult Parse(System.Text.Json.Nodes.JsonObject data)
    {
        static string Str(System.Text.Json.Nodes.JsonObject obj, string key)
        {
            if (obj[key] is System.Text.Json.Nodes.JsonValue jv
                && jv.TryGetValue<string>(out var s)
                && !string.IsNullOrWhiteSpace(s))
            {
                return s.Trim();
            }

            return obj[key]?.ToString()?.Trim() ?? string.Empty;
        }

        var suggested = Str(data, "suggestedTemplateCode").ToLowerInvariant();
        if (suggested is not (MandatoTemplateResolver.Generico or MandatoTemplateResolver.Sabaneta
            or MandatoTemplateResolver.Bello or MandatoTemplateResolver.Municipio))
        {
            suggested = InferTemplate(
                Str(data, "institutionalMandataryName"),
                Str(data, "mandatarySigla"),
                Str(data, "notes"));
        }

        var family = Str(data, "mandataryFamily").ToLowerInvariant();
        if (family is not (MandatoFamiliaCodes.Individuo or MandatoFamiliaCodes.OrganismoTransito))
        {
            family = suggested is MandatoTemplateResolver.Sabaneta or MandatoTemplateResolver.Bello
                ? MandatoFamiliaCodes.OrganismoTransito
                : MandatoFamiliaCodes.Individuo;
        }

        var assignmentMode = Str(data, "assignmentMode").ToLowerInvariant();
        if (assignmentMode is not (MandatoAssignmentModeCodes.Signer
            or MandatoAssignmentModeCodes.Institutional
            or MandatoAssignmentModeCodes.Open))
        {
            assignmentMode = suggested == MandatoTemplateResolver.Sabaneta
                ? MandatoAssignmentModeCodes.Institutional
                : MandatoAssignmentModeCodes.Signer;
        }

        static string? EmptyToNull(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        return new MandateConfigExtractResult(
            suggested,
            RequiresForNaturalPerson: true,
            family,
            EmptyToNull(Str(data, "institutionalMandataryName")),
            EmptyToNull(Str(data, "institutionalMandataryNit")),
            EmptyToNull(Str(data, "chamberCity")),
            EmptyToNull(Str(data, "mandatarySigla")),
            EmptyToNull(Str(data, "notes")),
            assignmentMode);
    }

    private static string InferTemplate(string name, string sigla, string notes)
    {
        var blob = $"{name} {sigla} {notes}".ToUpperInvariant();
        if (blob.Contains("SETSA", StringComparison.Ordinal) || blob.Contains("SABANETA", StringComparison.Ordinal))
            return MandatoTemplateResolver.Sabaneta;
        if (blob.Contains("MAB", StringComparison.Ordinal) || blob.Contains("BELLO", StringComparison.Ordinal))
            return MandatoTemplateResolver.Bello;
        if (blob.Contains("ENVIGADO", StringComparison.Ordinal)
            || blob.Contains("FUNZA", StringComparison.Ordinal)
            || blob.Contains("MEDELLIN", StringComparison.Ordinal)
            || blob.Contains("MEDELLÍN", StringComparison.Ordinal)
            || blob.Contains("MUNICIPIO", StringComparison.Ordinal))
            return MandatoTemplateResolver.Municipio;
        return MandatoTemplateResolver.Generico;
    }
}
