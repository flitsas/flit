namespace Flit.Queries.Domain;

/// <summary>
/// Una consulta guardada del usuario.
///
/// <para><b>Van a la base y no al navegador.</b> Si se pierden al cambiar de equipo no llegan a
/// usarse, y compartirlas o programarlas —lo que viene después— necesita que ya vivan ahí.</para>
/// </summary>
public sealed record SavedQueryDto(
    Guid Id,
    string Nombre,
    string? Descripcion,
    bool DeFabrica,
    QueryDefinition Definition,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>Alta o edición de una consulta guardada.</summary>
public sealed record SavedQueryInput(
    string Nombre,
    string? Descripcion,
    QueryDefinition Definition);

/// <summary>Reglas de alta que no dependen del módulo.</summary>
public static class SavedQuery
{
    private const int MaxNombre = 120;

    /// <summary>
    /// Un nombre vacío no se rechaza con un error: se pone uno. Que la consulta se guarde es más
    /// importante que cómo se llama, y renombrarla es un clic.
    /// </summary>
    public static SavedQueryInput BuildInput(
        IQueryFieldCatalog catalog, string? nombre, string? descripcion, QueryDefinition? definition)
    {
        var limpio = nombre?.Trim();

        return new SavedQueryInput(
            string.IsNullOrWhiteSpace(limpio)
                ? "Consulta sin nombre"
                : limpio[..Math.Min(limpio.Length, MaxNombre)],
            descripcion,
            QueryNormalizer.Normalize(catalog, definition));
    }
}
