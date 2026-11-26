using Flit.Tramites.Application.Identity.Rounds;
using Flit.Tramites.Domain.Identity;

namespace Flit.Tramites.Application.UseCases.IdentityRounds;

/// <summary>Configuración global de rondas tal como la ve el SuperAdmin (y la respuesta del PUT).</summary>
public sealed record IdentityRoundsConfigDto(
    int RondasMaximas, string DisparadorAlerta, IReadOnlyList<string> CorreosAlerta, bool LimiteActivo, int UmbralAlertaSoporte)
{
    internal static IdentityRoundsConfigDto From(IdentityRoundsConfig c) =>
        new(c.RondasMaximas, c.DisparadorAlerta, c.CorreosAlerta, c.LimiteActivo, c.UmbralAlertaSoporte);
}

public sealed class GetIdentityRoundsConfigHandler(IIdentityRoundsStore store)
{
    public async Task<IdentityRoundsConfigDto> HandleAsync(CancellationToken ct = default) =>
        IdentityRoundsConfigDto.From(await store.GetConfigAsync(ct).ConfigureAwait(false));
}

public sealed record UpdateIdentityRoundsConfigCommand(
    int? RondasMaximas, string? DisparadorAlerta, IReadOnlyList<string?>? CorreosAlerta, Guid? ActorUserId,
    bool? LimiteActivo = null, int? UmbralAlertaSoporte = null);

/// <summary>
/// Cambia la configuración GLOBAL de rondas. Las reglas viven en <see cref="IdentityRoundsConfig.Create"/> (el endpoint no las
/// repite); un valor inválido lanza <see cref="IdentityRoundsConfigException"/> sin tocar nada. Cada cambio deja auditoría con quién,
/// cuándo y los valores anterior y nuevo.
/// </summary>
public sealed class UpdateIdentityRoundsConfigHandler(
    IIdentityRoundsStore store,
    IIdentityRoundsConfigAuditor auditor,
    TimeProvider? clock = null)
{
    public async Task<IdentityRoundsConfigDto> HandleAsync(UpdateIdentityRoundsConfigCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.RondasMaximas is null)
            throw new IdentityRoundsConfigException(new Dictionary<string, string[]> { ["rondasMaximas"] = ["Las rondas máximas son obligatorias."] });

        var nueva = IdentityRoundsConfig.Create(
            command.RondasMaximas.Value, command.DisparadorAlerta, command.CorreosAlerta,
            command.LimiteActivo ?? true, command.UmbralAlertaSoporte ?? IdentityRoundsConfig.DefaultUmbralAlertaSoporte);
        var anterior = await store.GetConfigAsync(ct).ConfigureAwait(false);

        await store.SaveConfigAsync(nueva, command.ActorUserId, (clock ?? TimeProvider.System).GetUtcNow(), ct).ConfigureAwait(false);
        await auditor.WriteAsync(command.ActorUserId, anterior, nueva, ct).ConfigureAwait(false);
        return IdentityRoundsConfigDto.From(nueva);
    }
}
