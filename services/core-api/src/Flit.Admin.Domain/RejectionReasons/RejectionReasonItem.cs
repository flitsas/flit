namespace Flit.Admin.Domain.RejectionReasons;

/// <summary>Fila del catálogo de causales de rechazo tal como la ve la administración y el modal del OT.</summary>
public sealed record RejectionReasonItem(
    Guid Id,
    string Code,
    string Description,
    string Modalidad,
    int SortOrder,
    bool IsActive);

/// <summary>
/// Modalidades sobre las que se define una causal. Espejo de
/// <c>TramiteModalidadEntrada</c> — se duplica aquí para no acoplar el módulo Admin al dominio de
/// Trámites por una constante, igual que hace el resto de catálogos administrativos.
/// </summary>
public static class RejectionReasonModalidades
{
    public const string MatriculaInicial = "matricula_inicial";
    public const string Traspaso = "traspaso";

    public static readonly IReadOnlyList<string> Todas = [MatriculaInicial, Traspaso];

    public static bool EsValida(string? modalidad) =>
        modalidad is not null && Todas.Contains(modalidad, StringComparer.Ordinal);
}
