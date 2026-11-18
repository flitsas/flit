using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.Dtos;

namespace Flit.Analytics.Application.Queries.GestorDashboard;

/// <summary>Códigos de error de las lecturas del Inicio del Gestor (el endpoint los traduce a 400).</summary>
public static class GestorDashboardErrors
{
    public const string InvalidEstado = "invalid_estado";
    public const string InvalidFamilia = "invalid_familia";
    /// <summary>Sin tenant concreto (p. ej. el centinela global): estas lecturas no tienen vista de todas las compañías.</summary>
    public const string TenantRequired = "tenant_required";
}

/// <summary>
/// Vocabulario compartido de estados y familias. Este proyecto no referencia <c>Flit.Tramites.Domain</c>: los valores
/// se copian de <c>TramiteEstado</c> y <c>ProcedureFamilyCodes</c>, y los tests fijan que sigan siendo esos.
/// </summary>
public static class GestorDashboardVocabulario
{
    /// <summary>
    /// Estados que admite el filtro: <c>TramiteEstado.Todos</c> más el <c>subsanacion</c> legado, que la tira de estados
    /// pinta (estado-counts lo cuenta por <c>status</c> crudo, y la cola filtra igual para que las cifras cuadren).
    /// </summary>
    public static readonly IReadOnlyList<string> Estados =
    [
        "borrador", "anulado", "preparado", "preasignacion", "asignado", "entregado", "aprobado", "rechazado", "revocado",
        "subsanacion",
    ];

    /// <summary>
    /// Sin estado elegido, la cola deja fuera los finales (<c>TramiteEstado.Finales</c>): un aprobado de hace un año es
    /// el «más quieto» de todos, pero no hay nada que gestionar en él.
    /// </summary>
    public static readonly IReadOnlyList<string> EstadosSinFinales =
        [.. Estados.Where(e => e is not ("aprobado" or "anulado" or "revocado"))];

    /// <summary>Categoría del front → código de familia del tipo (ADR-0050).</summary>
    public static readonly IReadOnlyDictionary<string, string> FamiliaPorCategoria =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["matriculas"] = "MATRICULAS",
            ["traspasos"] = "TRASPASO",
            ["otros"] = "OTROS",
        };
}

/// <summary>
/// Cola de gestión: los trámites con más días sin movimiento. <see cref="Estado"/> vacío = todos menos los finales;
/// <see cref="Familia"/> (<c>matriculas</c>/<c>traspasos</c>/<c>otros</c>) vacía = todas las que el usuario ve.
/// </summary>
public sealed record GestorDashboardColaInactividadQuery(Guid TenantId, string? Estado, string? Familia, int Limit)
{
    /// <summary>HU #13664 — Ver por familia; <c>null</c> = sin restricción.</summary>
    public IReadOnlyList<string>? AllowedFamilies { get; init; }
}

public sealed class GestorDashboardColaInactividadHandler(IGestorDashboardReadRepository repo)
{
    public const int DefaultLimit = 5;
    public const int MaxLimit = 20;

    private static readonly GestorDashboardColaDto Vacia = new([], 0);

    public async Task<(GestorDashboardColaDto? Result, string? Error)> HandleAsync(
        GestorDashboardColaInactividadQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TenantId == Guid.Empty)
            return (null, GestorDashboardErrors.TenantRequired);

        IReadOnlyList<string> estados;
        if (string.IsNullOrWhiteSpace(query.Estado))
        {
            estados = GestorDashboardVocabulario.EstadosSinFinales;
        }
        else
        {
            var estado = query.Estado.Trim().ToLowerInvariant();
            if (!GestorDashboardVocabulario.Estados.Contains(estado))
                return (null, GestorDashboardErrors.InvalidEstado);
            estados = [estado];
        }

        // La familia pedida se cruza con las legibles: pedir una sin Ver no la abre, devuelve la cola vacía.
        var familias = query.AllowedFamilies;
        if (!string.IsNullOrWhiteSpace(query.Familia))
        {
            if (!GestorDashboardVocabulario.FamiliaPorCategoria.TryGetValue(query.Familia.Trim(), out var codigo))
                return (null, GestorDashboardErrors.InvalidFamilia);
            if (familias is not null && !familias.Contains(codigo, StringComparer.OrdinalIgnoreCase))
                return (Vacia, null);
            familias = [codigo];
        }

        var limit = Math.Clamp(query.Limit <= 0 ? DefaultLimit : query.Limit, 1, MaxLimit);
        var dto = await repo.GetColaInactividadAsync(query.TenantId, estados, familias, limit, ct).ConfigureAwait(false);
        return (dto, null);
    }
}
