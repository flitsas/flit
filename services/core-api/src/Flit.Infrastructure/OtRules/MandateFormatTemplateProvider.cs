using Flit.Infrastructure.Persistence;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Lee de <c>admin.mandate_format_settings</c> y <c>admin.mandate_format_versions</c> (DDL 124) la plantilla que debe
/// usar el contrato de un formato (HU #13172). Solo lectura: las versiones son inmutables.
/// </summary>
internal sealed class MandateFormatTemplateProvider(FlitDbContext db) : IMandateFormatTemplateProvider
{
    public async Task<MandateFormatTemplate> ResolveAsync(
        string formatCode,
        int? pinnedVersion,
        CancellationToken ct = default)
    {
        var code = (formatCode ?? string.Empty).Trim().ToLowerInvariant();
        var setting = await db.MandateFormatSettings.AsNoTracking()
            .Where(s => s.FormatCode == code)
            .Select(s => new { s.Id, s.CurrentVersion })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (setting is null)
            return new MandateFormatTemplate(code, 0, null);

        // Contrato ya emitido: la versión registrada manda. 0 = salió con la redacción del generador.
        if (pinnedVersion is { } pinned)
        {
            if (pinned <= 0)
                return new MandateFormatTemplate(code, 0, null);

            var body = await BodyOfAsync(setting.Id, pinned, ct).ConfigureAwait(false);
            if (body is not null)
                return new MandateFormatTemplate(code, pinned, body);
            // Una versión inmutable no desaparece; si aun así falta, se cae a la vigente en vez de romper la generación.
        }

        if (setting.CurrentVersion <= 0)
            return new MandateFormatTemplate(code, 0, null);

        var current = await BodyOfAsync(setting.Id, setting.CurrentVersion, ct).ConfigureAwait(false);
        return current is null
            ? new MandateFormatTemplate(code, 0, null)
            : new MandateFormatTemplate(code, setting.CurrentVersion, current);
    }

    private Task<string?> BodyOfAsync(Guid settingId, int version, CancellationToken ct) =>
        db.MandateFormatVersions.AsNoTracking()
            .Where(v => v.FormatSettingId == settingId && v.VersionNumber == version)
            .Select(v => (string?)v.Body)
            .FirstOrDefaultAsync(ct);
}
