using System.Text.Json;

namespace Flit.Tramites.Domain.Tramites.Services;

/// <summary>
/// Vista tipada del <c>validation_profile</c> JSONB de una regla de conformación (actor) —
/// FEATURE-08 / CFD-05. Deserialización tolerante: JSON vacío/nulo/corrupto degrada al perfil por
/// defecto. Aplica a OWNER/BUYER/LESSEE y demás actores; <c>EntryMode</c> solo es significativo en la
/// regla de la entidad VEHICLE.
/// </summary>
public sealed record ConformationRuleProfile
{
    public bool AllowsNaturalPerson { get; init; }
    public bool AllowsJuridicalPerson { get; init; }
    public bool AllowsMultiple { get; init; }
    public bool RequiresRunt { get; init; }
    public bool RequiresSimit { get; init; }
    public string? EntryMode { get; init; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Deserializa el <c>validation_profile</c>; perfil por defecto si es nulo/vacío/corrupto.</summary>
    public static ConformationRuleProfile FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ConformationRuleProfile();

        try
        {
            return JsonSerializer.Deserialize<ConformationRuleProfile>(json, Options)
                   ?? new ConformationRuleProfile();
        }
        catch (JsonException)
        {
            return new ConformationRuleProfile();
        }
    }
}
