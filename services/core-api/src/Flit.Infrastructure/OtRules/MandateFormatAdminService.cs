using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Domain.Documents;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Edición de los formatos de contrato de mandato por el Super Admin (HU #13171, Feature #13118). Une el catálogo del
/// sistema (<see cref="MandatoFormatCatalog"/>, lo fijo) con la personalización persistida
/// (<see cref="IMandateFormatRepository"/>, lo editable) y valida nombre, tipo y plantilla antes de escribir.
/// <para>Un formato sin fila de configuración (ambiente sin el DDL 124) se muestra con los valores de fábrica y sin
/// RowVersion: no se puede editar hasta que la migración lo siembre.</para>
/// </summary>
internal sealed class MandateFormatAdminService(IMandateFormatRepository repository) : IMandateFormatAdminService
{
    internal const int MaxNameLength = 80;

    private static readonly HashSet<string> AssignmentModes = new(StringComparer.Ordinal)
    {
        MandatoAssignmentModeCodes.Signer,
        MandatoAssignmentModeCodes.Institutional,
        MandatoAssignmentModeCodes.Open,
    };

    public async Task<IReadOnlyList<MandateFormatView>> ListAsync(CancellationToken ct = default)
    {
        var settings = (await repository.ListAsync(ct).ConfigureAwait(false)).ToDictionary(s => s.Code);
        return MandatoFormatCatalog.All.Select(f => ToView(f, settings.GetValueOrDefault(f.Code))).ToList();
    }

    public async Task<MandateFormatDetailView?> GetAsync(string code, CancellationToken ct = default)
    {
        var format = MandatoFormatCatalog.Find(code);
        if (format is null)
            return null;

        var setting = await repository.GetAsync(format.Code, ct).ConfigureAwait(false);
        var versions = await repository.ListVersionsAsync(format.Code, ct).ConfigureAwait(false);
        var current = setting is { CurrentVersion: > 0 }
            ? versions.FirstOrDefault(v => v.VersionNumber == setting.CurrentVersion)
            : null;
        return new MandateFormatDetailView(
            ToView(format, setting),
            current?.Body,
            versions.Select(v => new MandateFormatVersionInfo(v.VersionNumber, v.BodySha256, v.CreatedAt, v.CreatedBy))
                .ToList());
    }

    public async Task<(MandateFormatVersionInfo Info, string Body)?> GetVersionAsync(
        string code, int versionNumber, CancellationToken ct = default)
    {
        var format = MandatoFormatCatalog.Find(code);
        if (format is null)
            return null;

        var v = await repository.GetVersionAsync(format.Code, versionNumber, ct).ConfigureAwait(false);
        return v is null
            ? null
            : (new MandateFormatVersionInfo(v.VersionNumber, v.BodySha256, v.CreatedAt, v.CreatedBy), v.Body);
    }

    public async Task<MandateFormatUpdateResult> UpdateAsync(
        string code,
        UpdateMandateFormatRequest request,
        Guid? userId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Sin crear: un código fuera del catálogo es 404 aunque traiga datos válidos.
        var format = MandatoFormatCatalog.Find(code);
        if (format is null)
            return new(MandateFormatUpdateStatus.NotFound);

        var setting = await repository.GetAsync(format.Code, ct).ConfigureAwait(false);
        var before = ToView(format, setting);

        MandateFormatUpdateResult Bad(string error, IReadOnlyList<MandateFormatUnknownVariable>? unknown = null) =>
            new(MandateFormatUpdateStatus.BadRequest, error, before, before, UnknownVariables: unknown);

        if (setting is null)
            return new(MandateFormatUpdateStatus.NotFound, "formato_sin_configuracion", before, before);

        // 1) Validación de contenido (400): nada se escribe.
        string? name = null;
        if (request.Name is not null)
        {
            name = request.Name.Trim();
            if (name.Length == 0)
                return Bad("nombre_vacio");
            if (name.Length > MaxNameLength)
                return Bad("nombre_demasiado_largo");
            var all = await repository.ListAsync(ct).ConfigureAwait(false);
            if (all.Any(s => s.Code != format.Code
                    && string.Equals(s.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                return Bad("nombre_repetido");
        }

        string? mode = null;
        if (request.AssignmentMode is not null)
        {
            mode = request.AssignmentMode.Trim().ToLowerInvariant();
            if (!AssignmentModes.Contains(mode))
                return Bad("assignment_mode_invalido");
        }

        string? body = null;
        if (request.Body is not null)
        {
            // auto delega en la plantilla de sistema del organismo: no tiene redacción propia que personalizar.
            if (!format.IsRedaction)
                return Bad("formato_sin_plantilla");
            body = request.Body.Trim();
            if (body.Length > MandateFormatRepository.MaxBodyLength)
                return Bad("plantilla_demasiado_larga");
            var validation = MandatoTemplateValidator.Validate(body);
            if (!validation.IsValid)
            {
                return Bad(
                    validation.Error!,
                    validation.UnknownVariables.Select(u => new MandateFormatUnknownVariable(u.Name, u.Line, u.Column))
                        .ToList());
            }
        }

        // 2) Escritura atómica con control de concurrencia (409).
        var saved = await repository
            .SaveAsync(format.Code, new SaveMandateFormatCommand(request.RowVersion, name, mode, body), userId, ct)
            .ConfigureAwait(false);

        return saved.Status switch
        {
            MandateFormatWriteStatus.Ok => new(
                MandateFormatUpdateStatus.Ok, null, before, ToView(format, saved.Setting),
                saved.PublishedVersion?.VersionNumber, saved.Changed),
            MandateFormatWriteStatus.Conflict => new(
                MandateFormatUpdateStatus.Conflict, "row_version_conflict", before, before),
            MandateFormatWriteStatus.InvalidBody => Bad("plantilla_demasiado_larga"),
            _ => new(MandateFormatUpdateStatus.NotFound, "template_code_invalido", before, before),
        };
    }

    public async Task<MandateFormatUpdateResult> ResetTemplateAsync(
        string code,
        long? rowVersion,
        Guid? userId,
        CancellationToken ct = default)
    {
        var format = MandatoFormatCatalog.Find(code);
        if (format is null)
            return new(MandateFormatUpdateStatus.NotFound);

        var setting = await repository.GetAsync(format.Code, ct).ConfigureAwait(false);
        var before = ToView(format, setting);
        if (setting is null)
            return new(MandateFormatUpdateStatus.NotFound, "formato_sin_configuracion", before, before);

        // auto delega en la plantilla de sistema del organismo: no hay redacción propia que restablecer.
        if (!format.IsRedaction)
            return new(MandateFormatUpdateStatus.BadRequest, "formato_sin_plantilla", before, before);

        var saved = await repository.ResetTemplateAsync(format.Code, rowVersion, userId, ct).ConfigureAwait(false);
        return saved.Status switch
        {
            MandateFormatWriteStatus.Ok => new(
                MandateFormatUpdateStatus.Ok, null, before, ToView(format, saved.Setting), null, saved.Changed),
            MandateFormatWriteStatus.Conflict => new(
                MandateFormatUpdateStatus.Conflict, "row_version_conflict", before, before),
            _ => new(MandateFormatUpdateStatus.NotFound, "template_code_invalido", before, before),
        };
    }

    private static MandateFormatView ToView(MandatoFormatDefinition f, MandateFormatSettingView? s) =>
        new(
            f.Code,
            s?.Name ?? f.DefaultName,
            s?.AssignmentMode ?? f.DefaultAssignmentMode,
            f.BaseRedaction,
            f.IsRedaction,
            s?.CurrentVersion ?? 0,
            s?.RowVersion,
            s?.UpdatedAt);
}
