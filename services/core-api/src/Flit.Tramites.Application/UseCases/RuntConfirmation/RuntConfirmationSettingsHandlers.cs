using System.Globalization;
using Flit.Tramites.Domain.RuntConfirmation;

namespace Flit.Tramites.Application.UseCases.RuntConfirmation;

/// <summary>Contrato de lectura/escritura de la configuración global (GET/PUT <c>/api/v1/admin/runt-confirmation/settings</c>).</summary>
public sealed record RuntConfirmationSettingsDto(
    bool Enabled,
    string RunAtLocal,
    string ProviderKey,
    int GraceDays,
    int DiscrepancyAfterRuns,
    int MaxAttempts,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy)
{
    public static RuntConfirmationSettingsDto From(RuntConfirmationSettings s) =>
        new(s.Enabled, s.RunAtLocal, s.ProviderKey, s.GraceDays, s.DiscrepancyAfterRuns, s.MaxAttempts, s.UpdatedAt, s.UpdatedBy);
}

public sealed record UpdateRuntConfirmationSettingsCommand(
    bool Enabled,
    string RunAtLocal,
    string ProviderKey,
    int GraceDays,
    int DiscrepancyAfterRuns,
    int MaxAttempts,
    Guid? ActorUserId);

public sealed record UpdateRuntConfirmationSettingsResult(
    RuntConfirmationSettingsDto? Settings,
    IReadOnlyList<RuntConfirmationSettingsError> Errors,
    IReadOnlyList<RuntConfirmationSettingChange> Changes)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class GetRuntConfirmationSettingsHandler(IRuntConfirmationSettingsRepository repository)
{
    public async Task<RuntConfirmationSettingsDto> HandleAsync(CancellationToken ct = default) =>
        RuntConfirmationSettingsDto.From(await repository.GetAsync(ct).ConfigureAwait(false));
}

/// <summary>
/// Guarda la configuración global (HU #12277). Valida rangos antes de tocar nada (AC2); calcula el
/// diff campo a campo y audita UNA entrada por campo cambiado con valor anterior, nuevo, usuario y
/// fecha (AC3). Un guardado sin cambios no escribe ni audita.
/// </summary>
public sealed class UpdateRuntConfirmationSettingsHandler(
    IRuntConfirmationSettingsRepository repository,
    IRuntConfirmationAuditWriter audit)
{
    public async Task<UpdateRuntConfirmationSettingsResult> HandleAsync(
        UpdateRuntConfirmationSettingsCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var current = await repository.GetAsync(ct).ConfigureAwait(false);

        var proposed = new RuntConfirmationSettings
        {
            Enabled = command.Enabled,
            RunAtLocal = (command.RunAtLocal ?? string.Empty).Trim(),
            ProviderKey = (command.ProviderKey ?? string.Empty).Trim(),
            GraceDays = command.GraceDays,
            DiscrepancyAfterRuns = command.DiscrepancyAfterRuns,
            MaxAttempts = command.MaxAttempts,
        };

        var errors = proposed.Validate();
        if (errors.Count > 0)
            return new UpdateRuntConfirmationSettingsResult(null, errors, []);

        var changes = Diff(current, proposed);
        if (changes.Count == 0)
            return new UpdateRuntConfirmationSettingsResult(RuntConfirmationSettingsDto.From(current), [], []);

        current.Enabled = proposed.Enabled;
        current.RunAtLocal = proposed.RunAtLocal;
        current.ProviderKey = proposed.ProviderKey;
        current.GraceDays = proposed.GraceDays;
        current.DiscrepancyAfterRuns = proposed.DiscrepancyAfterRuns;
        current.MaxAttempts = proposed.MaxAttempts;
        current.UpdatedAt = DateTimeOffset.UtcNow;
        current.UpdatedBy = command.ActorUserId;

        await repository.SaveAsync(current, ct).ConfigureAwait(false);

        foreach (var change in changes)
            await audit.WriteSettingChangeAsync(change, command.ActorUserId, ct).ConfigureAwait(false);

        return new UpdateRuntConfirmationSettingsResult(RuntConfirmationSettingsDto.From(current), [], changes);
    }

    private static List<RuntConfirmationSettingChange> Diff(RuntConfirmationSettings a, RuntConfirmationSettings b)
    {
        var changes = new List<RuntConfirmationSettingChange>();
        Add(changes, "enabled", a.Enabled, b.Enabled);
        Add(changes, "runAtLocal", a.RunAtLocal, b.RunAtLocal);
        Add(changes, "providerKey", a.ProviderKey, b.ProviderKey);
        Add(changes, "graceDays", a.GraceDays, b.GraceDays);
        Add(changes, "discrepancyAfterRuns", a.DiscrepancyAfterRuns, b.DiscrepancyAfterRuns);
        Add(changes, "maxAttempts", a.MaxAttempts, b.MaxAttempts);
        return changes;
    }

    private static void Add<T>(List<RuntConfirmationSettingChange> changes, string field, T before, T after)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
            return;
        changes.Add(new RuntConfirmationSettingChange(field, Text(before), Text(after)));
    }

    private static string? Text<T>(T value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
