using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementación EF Core de la identidad de marca (HU #12412, ADR-0060 D1) sobre
/// <c>admin.tenant_brandings</c> / <c>admin.tenant_brand_logos</c>. Sin <c>HasQueryFilter</c>
/// (convención vigente, delta-hechos #12): cada consulta filtra <c>tenant_id</c> explícitamente. La
/// auditoría legible (old/new) se escribe en <c>admin.tenant_config_audit_logs</c> en el MISMO
/// <c>SaveChanges</c> que la fila (patrón <c>CompanyWriteRepository</c>); el rastro técnico por columna
/// lo cubre el trigger <c>tr_*_audit</c> de BD (DDL 115). El disparador
/// <c>identity.trg_require_marca_blanca_head()</c> rechaza con <c>check_violation</c> y
/// <c>ConstraintName</c> = <c>ck_tenant_brandings_marca_blanca</c> / <c>ck_tenant_brand_logos_marca_blanca</c>;
/// se traduce a <see cref="BrandingTenantNotMarcaBlancaException"/> (AC2).
/// </summary>
internal sealed class TenantBrandingRepository : ITenantBrandingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly FlitDbContext _context;
    private readonly IAuditContextAccessor _auditContext;

    public TenantBrandingRepository(FlitDbContext context, IAuditContextAccessor? auditContext = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _auditContext = auditContext ?? NullAuditContextAccessor.Instance;
    }

    public async Task<TenantBranding?> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.TenantBrandings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : Map(entity);
    }

    public async Task<TenantBranding> UpsertDraftAsync(
        Guid tenantId,
        BrandingDraft draft,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var entity = await _context.TenantBrandings
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var newDraftJson = SerializeDraft(draft);
        string? oldDraftJson = entity?.Draft;
        string operation;

        if (entity is null)
        {
            entity = new TenantBrandingEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Draft = newDraftJson,
                CreatedAt = now,
                CreatedBy = changedBy,
                UpdatedAt = now,
                UpdatedBy = changedBy,
            };
            _context.TenantBrandings.Add(entity);
            operation = AuditVocabulary.Operations.Create;
        }
        else
        {
            entity.Draft = newDraftJson;
            entity.UpdatedAt = now;
            entity.UpdatedBy = changedBy;
            operation = AuditVocabulary.Operations.Update;
        }

        AddAudit(tenantId, "TenantBranding", "draft", oldDraftJson, newDraftJson, operation, changedBy, now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsMarcaBlancaRejection(ex))
        {
            _context.ChangeTracker.Clear();
            throw new BrandingTenantNotMarcaBlancaException(tenantId);
        }

        await _context.Entry(entity).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return Map(entity);
    }

    public async Task<TenantBranding> PublishAsync(
        Guid tenantId,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.TenantBrandings
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No existe marca para el tenant {tenantId} (verificar antes de publicar).");

        var now = DateTimeOffset.UtcNow;
        var oldPublished = entity.Published;

        entity.Published = entity.Draft;
        entity.PublishedVersion += 1;
        entity.PublishedAt = now;
        entity.PublishedBy = changedBy;
        entity.UpdatedAt = now;
        entity.UpdatedBy = changedBy;

        AddAudit(tenantId, "TenantBranding", "published", oldPublished, entity.Published, AuditVocabulary.Operations.Update, changedBy, now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsMarcaBlancaRejection(ex))
        {
            _context.ChangeTracker.Clear();
            throw new BrandingTenantNotMarcaBlancaException(tenantId);
        }

        await _context.Entry(entity).ReloadAsync(cancellationToken).ConfigureAwait(false);
        return Map(entity);
    }

    public async Task<TenantBranding> RetireAsync(
        Guid tenantId,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.TenantBrandings
            .FirstOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No existe marca para el tenant {tenantId} (verificar antes de retirar).");

        var now = DateTimeOffset.UtcNow;

        if (entity.DeletedAt is null)
        {
            entity.DeletedAt = now;
            entity.DeletedBy = changedBy;
            entity.UpdatedAt = now;
            entity.UpdatedBy = changedBy;

            AddAudit(tenantId, "TenantBranding", "retired", null, JsonSerializer.Serialize(now, JsonOptions), AuditVocabulary.Operations.Update, changedBy, now);

            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _context.Entry(entity).ReloadAsync(cancellationToken).ConfigureAwait(false);
        }

        return Map(entity);
    }

    public async Task<TenantBrandLogoVersion> AddLogoVersionAsync(
        Guid tenantId,
        NewBrandLogo logo,
        Guid? changedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logo);

        var now = DateTimeOffset.UtcNow;

        var previousActive = await _context.TenantBrandLogos
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.Status == TenantBrandLogoVersion.StatusActive && x.DeletedAt == null,
                cancellationToken)
            .ConfigureAwait(false);

        var lastVersion = await _context.TenantBrandLogos
            .Where(x => x.TenantId == tenantId)
            .Select(x => (int?)x.Version)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false) ?? 0;

        if (previousActive is not null)
        {
            previousActive.Status = TenantBrandLogoVersion.StatusSuperseded;
            previousActive.SupersededAt = now;
            previousActive.SupersededBy = changedBy;
            previousActive.UpdatedAt = now;
            previousActive.UpdatedBy = changedBy;
        }

        var entity = new TenantBrandLogoEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Version = lastVersion + 1,
            Status = TenantBrandLogoVersion.StatusActive,
            ContentType = logo.ContentType,
            Filename = logo.Filename,
            StoragePath = logo.StoragePath,
            StorageSha256 = logo.StorageSha256,
            SizeBytes = logo.SizeBytes,
            WidthPx = logo.WidthPx,
            HeightPx = logo.HeightPx,
            CreatedAt = now,
            CreatedBy = changedBy,
            UpdatedAt = now,
            UpdatedBy = changedBy,
        };
        _context.TenantBrandLogos.Add(entity);

        AddAudit(
            tenantId,
            "TenantBrandLogo",
            "logo",
            previousActive is null ? null : JsonSerializer.Serialize(new { previousActive.Id, previousActive.Version }, JsonOptions),
            JsonSerializer.Serialize(new { entity.Id, entity.Version }, JsonOptions),
            AuditVocabulary.Operations.Create,
            changedBy,
            now);

        try
        {
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsMarcaBlancaRejection(ex))
        {
            _context.ChangeTracker.Clear();
            throw new BrandingTenantNotMarcaBlancaException(tenantId);
        }

        return MapLogo(entity);
    }

    public async Task<TenantBrandLogoVersion?> GetLogoVersionAsync(
        Guid tenantId,
        Guid logoId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.TenantBrandLogos
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.Id == logoId && x.DeletedAt == null,
                cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapLogo(entity);
    }

    public async Task<TenantBrandLogoVersion?> GetLogoVersionByIdAsync(
        Guid logoId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.TenantBrandLogos
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == logoId && x.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);

        return entity is null ? null : MapLogo(entity);
    }

    private void AddAudit(
        Guid tenantId,
        string entityName,
        string fieldName,
        string? oldValue,
        string? newValue,
        string operation,
        Guid? changedBy,
        DateTimeOffset now)
    {
        _context.TenantConfigAuditLogs.Add(new TenantConfigAuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EntityName = entityName,
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedAt = now,
            ChangedBy = changedBy,
            ClientIp = _auditContext.ClientIp,
            Operation = operation,
            Result = AuditVocabulary.Results.Success,
            Module = AuditVocabulary.Modules.Companies,
            TargetEntityType = "TENANT_BRANDING",
            TargetEntityId = tenantId,
        });
    }

    private static bool IsMarcaBlancaRejection(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.CheckViolation
        && (pg.ConstraintName == "ck_tenant_brandings_marca_blanca" || pg.ConstraintName == "ck_tenant_brand_logos_marca_blanca");

    private static string SerializeDraft(BrandingDraft draft) =>
        JsonSerializer.Serialize(
            new BrandingJson(
                BrandingDraft.SchemaVersion,
                draft.PlatformName,
                draft.Colors is null ? null : new BrandColorsJson(draft.Colors.Primary, draft.Colors.Secondary, draft.Colors.OnPrimary),
                draft.LogoId),
            JsonOptions);

    private static BrandingDraft DeserializeDraft(string json)
    {
        var dto = JsonSerializer.Deserialize<BrandingJson>(json, JsonOptions) ?? new BrandingJson(BrandingDraft.SchemaVersion, null, null, null);
        var colors = dto.Colors is null ? null : new BrandColors(dto.Colors.Primary, dto.Colors.Secondary, dto.Colors.OnPrimary);
        return new BrandingDraft(dto.PlatformName, colors, dto.LogoId);
    }

    private static TenantBranding Map(TenantBrandingEntity entity) => new()
    {
        TenantId = entity.TenantId,
        Draft = DeserializeDraft(entity.Draft),
        Published = entity.Published is null ? null : DeserializeDraft(entity.Published),
        PublishedVersion = entity.PublishedVersion,
        PublishedAt = entity.PublishedAt,
        PublishedBy = entity.PublishedBy,
        DeletedAt = entity.DeletedAt,
        RowVersion = entity.RowVersion,
    };

    private static TenantBrandLogoVersion MapLogo(TenantBrandLogoEntity entity) => new(
        entity.Id,
        entity.TenantId,
        entity.Version,
        entity.Status,
        entity.ContentType,
        entity.Filename,
        entity.StoragePath,
        entity.StorageSha256,
        entity.SizeBytes,
        entity.WidthPx,
        entity.HeightPx);

    private sealed record BrandingJson(int SchemaVersion, string? PlatformName, BrandColorsJson? Colors, Guid? LogoId);

    private sealed record BrandColorsJson(string Primary, string Secondary, string OnPrimary);
}
