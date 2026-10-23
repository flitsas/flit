using System.Security.Cryptography;
using System.Text;
using Flit.Admin.Application.Plataforma.Mandatos;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Tramites.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación EF Core de <see cref="IMandateFormatRepository"/> (HU #13169, Feature #13118). Las versiones son
/// inmutables (el DDL 124 rechaza UPDATE y DELETE) y cada guardado es una sola unidad de trabajo: o se aplica el
/// nombre, el tipo y la versión nueva, o nada.
/// </summary>
internal sealed class MandateFormatRepository(FlitDbContext db) : IMandateFormatRepository
{
    /// <summary>Mismo límite que el editor de plantilla del organismo (<c>SaveEditorBodyAsync</c>).</summary>
    internal const int MaxBodyLength = 100_000;

    public async Task<IReadOnlyList<MandateFormatSettingView>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.MandateFormatSettings.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);
        var order = MandatoFormatCatalog.Codes.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        return rows
            .OrderBy(r => order.GetValueOrDefault(r.FormatCode, int.MaxValue))
            .ThenBy(r => r.FormatCode, StringComparer.Ordinal)
            .Select(ToView)
            .ToList();
    }

    public async Task<MandateFormatSettingView?> GetAsync(string code, CancellationToken ct = default)
    {
        var normalized = Normalize(code);
        if (normalized is null)
            return null;
        var row = await db.MandateFormatSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.FormatCode == normalized, ct).ConfigureAwait(false);
        return row is null ? null : ToView(row);
    }

    public async Task<MandateFormatVersionView?> GetCurrentVersionAsync(string code, CancellationToken ct = default)
    {
        var normalized = Normalize(code);
        if (normalized is null)
            return null;
        var setting = await db.MandateFormatSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.FormatCode == normalized, ct).ConfigureAwait(false);
        if (setting is null || setting.CurrentVersion <= 0)
            return null;
        return await GetVersionAsync(normalized, setting.CurrentVersion, ct).ConfigureAwait(false);
    }

    public async Task<MandateFormatVersionView?> GetVersionAsync(
        string code, int versionNumber, CancellationToken ct = default)
    {
        var normalized = Normalize(code);
        if (normalized is null || versionNumber < 1)
            return null;
        var row = await (
                from v in db.MandateFormatVersions.AsNoTracking()
                join s in db.MandateFormatSettings.AsNoTracking() on v.FormatSettingId equals s.Id
                where s.FormatCode == normalized && v.VersionNumber == versionNumber
                select v)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return row is null ? null : ToVersionView(normalized, row);
    }

    public async Task<IReadOnlyList<MandateFormatVersionView>> ListVersionsAsync(
        string code, CancellationToken ct = default)
    {
        var normalized = Normalize(code);
        if (normalized is null)
            return [];
        var rows = await (
                from v in db.MandateFormatVersions.AsNoTracking()
                join s in db.MandateFormatSettings.AsNoTracking() on v.FormatSettingId equals s.Id
                where s.FormatCode == normalized
                orderby v.VersionNumber
                select v)
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => ToVersionView(normalized, r)).ToList();
    }

    public async Task<MandateFormatSaveResult> SaveAsync(
        string code,
        SaveMandateFormatCommand command,
        Guid? userId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Un código fuera del catálogo nunca crea fila: el catálogo en código es la única fuente de formatos válidos.
        var normalized = Normalize(code);
        if (normalized is null || !MandatoFormatCatalog.Contains(normalized))
            return new(MandateFormatWriteStatus.UnknownCode);

        string? body = null;
        if (command.Body is not null)
        {
            body = command.Body.Trim();
            if (body.Length == 0 || body.Length > MaxBodyLength)
                return new(MandateFormatWriteStatus.InvalidBody);
        }

        var setting = await db.MandateFormatSettings
            .FirstOrDefaultAsync(s => s.FormatCode == normalized, ct).ConfigureAwait(false);
        if (setting is null)
            return new(MandateFormatWriteStatus.UnknownCode);

        // RowVersion ausente sobre un formato existente también es conflicto: no se escribe a ciegas.
        if (command.ExpectedRowVersion is not { } expected || setting.RowVersion != expected)
            return new(MandateFormatWriteStatus.Conflict, ToView(setting));

        var newName = command.Name?.Trim();
        var newMode = command.AssignmentMode?.Trim().ToLowerInvariant();
        var nameChanged = newName is not null && !string.Equals(newName, setting.DisplayName, StringComparison.Ordinal);
        var modeChanged = newMode is not null && !string.Equals(newMode, setting.AssignmentMode, StringComparison.Ordinal);
        if (!nameChanged && !modeChanged && body is null)
            return new(MandateFormatWriteStatus.Ok, ToView(setting), null, Changed: false);

        var now = DateTimeOffset.UtcNow;
        if (nameChanged)
            setting.DisplayName = newName!;
        if (modeChanged)
            setting.AssignmentMode = newMode!;

        MandateFormatVersionEntity? version = null;
        if (body is not null)
        {
            // La siguiente a la MÁS ALTA publicada, no a la vigente: tras restablecer la redacción de fábrica la vigente
            // es 0 y las versiones anteriores siguen ahí (son inmutables).
            var ultima = await db.MandateFormatVersions
                .Where(v => v.FormatSettingId == setting.Id)
                .MaxAsync(v => (int?)v.VersionNumber, ct).ConfigureAwait(false) ?? 0;
            version = new MandateFormatVersionEntity
            {
                Id = Guid.NewGuid(),
                FormatSettingId = setting.Id,
                VersionNumber = ultima + 1,
                Body = body,
                BodySha256 = Sha256Hex(body),
                CreatedAt = now,
                CreatedBy = userId,
            };
            db.MandateFormatVersions.Add(version);
            setting.CurrentVersion = version.VersionNumber;
        }

        setting.UpdatedAt = now;
        setting.UpdatedBy = userId;

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Concurrencia (otro guardó antes) o versión duplicada: no queda nada a medias y se descarta lo pendiente.
            db.ChangeTracker.Clear();
            return new(MandateFormatWriteStatus.Conflict);
        }

        await db.Entry(setting).ReloadAsync(ct).ConfigureAwait(false);
        return new(
            MandateFormatWriteStatus.Ok,
            ToView(setting),
            version is null ? null : ToVersionView(normalized, version),
            Changed: true);
    }

    public async Task<MandateFormatSaveResult> ResetTemplateAsync(
        string code,
        long? expectedRowVersion,
        Guid? userId,
        CancellationToken ct = default)
    {
        var normalized = Normalize(code);
        if (normalized is null || !MandatoFormatCatalog.Contains(normalized))
            return new(MandateFormatWriteStatus.UnknownCode);

        var setting = await db.MandateFormatSettings
            .FirstOrDefaultAsync(s => s.FormatCode == normalized, ct).ConfigureAwait(false);
        if (setting is null)
            return new(MandateFormatWriteStatus.UnknownCode);

        if (expectedRowVersion is not { } expected || setting.RowVersion != expected)
            return new(MandateFormatWriteStatus.Conflict, ToView(setting));

        if (setting.CurrentVersion == 0)
            return new(MandateFormatWriteStatus.Ok, ToView(setting), null, Changed: false);

        setting.CurrentVersion = 0;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        setting.UpdatedBy = userId;

        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            return new(MandateFormatWriteStatus.Conflict);
        }

        await db.Entry(setting).ReloadAsync(ct).ConfigureAwait(false);
        return new(MandateFormatWriteStatus.Ok, ToView(setting), null, Changed: true);
    }

    internal static string Sha256Hex(string body) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    private static string? Normalize(string? code)
    {
        var c = code?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(c) ? null : c;
    }

    private static MandateFormatSettingView ToView(MandateFormatSettingEntity s) =>
        new(s.FormatCode, s.DisplayName, s.AssignmentMode, s.CurrentVersion, s.RowVersion, s.UpdatedAt, s.UpdatedBy);

    private static MandateFormatVersionView ToVersionView(string code, MandateFormatVersionEntity v) =>
        new(code, v.VersionNumber, v.Body, v.BodySha256, v.CreatedAt, v.CreatedBy);
}
