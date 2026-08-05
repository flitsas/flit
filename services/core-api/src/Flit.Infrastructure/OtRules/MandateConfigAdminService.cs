using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Tramites.Application.Ocr;
using Flit.Tramites.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// CRUD SuperAdmin de <c>admin.transit_office_mandate_config</c> + extracción OCR de mandato de referencia.
/// Default implícito (sin fila) = plantilla genérica.
/// </summary>
internal sealed class MandateConfigAdminService : IMandateConfigAdminService
{
    private static readonly HashSet<string> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        MandatoTemplateResolver.Generico,
        MandatoTemplateResolver.Sabaneta,
        MandatoTemplateResolver.Bello,
    };

    private static readonly HashSet<string> Families = new(StringComparer.OrdinalIgnoreCase)
    {
        MandatoFamiliaCodes.Individuo,
        MandatoFamiliaCodes.OrganismoTransito,
    };

    private readonly FlitDbContext _db;
    private readonly ITransitOfficeCatalog _catalog;
    private readonly IDocumentOcrAnalyzer _ocr;

    public MandateConfigAdminService(
        FlitDbContext db,
        ITransitOfficeCatalog catalog,
        IDocumentOcrAnalyzer ocr)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
    }

    public async Task<IReadOnlyList<MandateOtConfigView>> ListAsync(CancellationToken ct = default)
    {
        var configs = await _db.TransitOfficeMandateConfigs.AsNoTracking()
            .ToDictionaryAsync(c => c.TransitOfficeId, ct)
            .ConfigureAwait(false);

        return _catalog.All
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

        if (string.Equals(family, MandatoFamiliaCodes.OrganismoTransito, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.InstitutionalMandataryName))
        {
            return (MandateConfigWriteStatus.InstitutionalRequired, null);
        }

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
                CreatedAt = now,
                CreatedBy = userId,
            };
            _db.TransitOfficeMandateConfigs.Add(entity);
        }
        else if (request.RowVersion is { } expected && entity.RowVersion != expected)
        {
            return (MandateConfigWriteStatus.Conflict, null);
        }

        entity.TemplateCode = template;
        // Columna legacy: el mandato aplica siempre (PN y PJ); se persiste en true.
        entity.RequiresForNaturalPerson = true;
        entity.MandataryFamily = family;
        entity.InstitutionalMandataryName = NullIfEmpty(request.InstitutionalMandataryName);
        entity.InstitutionalMandataryNit = NullIfEmpty(request.InstitutionalMandataryNit);
        entity.ChamberCity = NullIfEmpty(request.ChamberCity);
        entity.MandatarySigla = NullIfEmpty(request.MandatarySigla);
        entity.UpdatedAt = now;
        entity.UpdatedBy = userId;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

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

        _db.TransitOfficeMandateConfigs.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return MandateConfigWriteStatus.Ok;
    }

    /// <summary>Extrae campos sugeridos desde un PDF/imagen de mandato de referencia (no persiste).</summary>
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
                analysis.Message ?? "No se pudo extraer información del documento.");
        }

        return MandatoConfigOcr.Parse(analysis.Data);
    }

    private static MandateOtConfigView ToView(TransitOfficeEntry office, TransitOfficeMandateConfigEntity? cfg)
    {
        if (cfg is null)
        {
            return new MandateOtConfigView(
                office.Id,
                office.Code,
                office.Name,
                MandatoTemplateResolver.Generico,
                RequiresForNaturalPerson: true,
                MandatoFamiliaCodes.Individuo,
                null, null, null, null,
                HasExplicitConfig: false,
                RowVersion: null);
        }

        return new MandateOtConfigView(
            office.Id,
            office.Code,
            office.Name,
            cfg.TemplateCode,
            cfg.RequiresForNaturalPerson,
            cfg.MandataryFamily,
            cfg.InstitutionalMandataryName,
            cfg.InstitutionalMandataryNit,
            cfg.ChamberCity,
            cfg.MandatarySigla,
            HasExplicitConfig: true,
            cfg.RowVersion);
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
            or MandatoTemplateResolver.Bello))
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

        static string? EmptyToNull(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        return new MandateConfigExtractResult(
            suggested,
            RequiresForNaturalPerson: true,
            family,
            EmptyToNull(Str(data, "institutionalMandataryName")),
            EmptyToNull(Str(data, "institutionalMandataryNit")),
            EmptyToNull(Str(data, "chamberCity")),
            EmptyToNull(Str(data, "mandatarySigla")),
            EmptyToNull(Str(data, "notes")));
    }

    private static string InferTemplate(string name, string sigla, string notes)
    {
        var blob = $"{name} {sigla} {notes}".ToUpperInvariant();
        if (blob.Contains("SETSA", StringComparison.Ordinal) || blob.Contains("SABANETA", StringComparison.Ordinal))
            return MandatoTemplateResolver.Sabaneta;
        if (blob.Contains("MAB", StringComparison.Ordinal) || blob.Contains("BELLO", StringComparison.Ordinal))
            return MandatoTemplateResolver.Bello;
        return MandatoTemplateResolver.Generico;
    }
}
