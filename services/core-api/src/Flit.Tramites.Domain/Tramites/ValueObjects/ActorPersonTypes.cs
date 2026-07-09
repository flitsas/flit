using System;

namespace Flit.Tramites.Domain.Tramites.ValueObjects;

/// <summary>
/// Tipo de persona del actor de un trámite (HU #10542). Vocabulario alineado con
/// <c>ConsultationTemplate.PersonType</c> y el CHECK de BD existente: <c>natural</c> | <c>juridical</c>.
/// </summary>
public static class ActorPersonTypes
{
    public const string Natural = "natural";
    public const string Juridical = "juridical";

    public static bool IsNatural(string? value) =>
        string.Equals(value?.Trim(), Natural, StringComparison.OrdinalIgnoreCase);

    public static bool IsJuridical(string? value) =>
        string.Equals(value?.Trim(), Juridical, StringComparison.OrdinalIgnoreCase);

    public static bool IsValid(string? value) => IsNatural(value) || IsJuridical(value);

    /// <summary>Normaliza al vocabulario canónico en minúsculas, o <c>null</c> si no aplica.</summary>
    public static string? Normalize(string? value) =>
        IsNatural(value) ? Natural : IsJuridical(value) ? Juridical : null;
}
