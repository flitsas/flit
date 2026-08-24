namespace Flit.Admin.Domain.RejectionReasons;

/// <summary>Fila del catálogo de causales de rechazo tal como la ve la administración y el modal del OT.</summary>
public sealed record RejectionReasonItem(
    Guid Id,
    string Code,
    string Description,
    string Family,
    int SortOrder,
    bool IsActive);

/// <summary>
/// Familias sobre las que se define una causal (ADR-0050). Espejo de <c>ProcedureFamily</c> — se
/// duplica aquí para no acoplar el módulo Admin al dominio de Trámites por una constante, igual que
/// hace el resto de catálogos administrativos.
/// <para>Antes eran las dos modalidades (<c>matricula_inicial</c> / <c>traspaso</c>), así que no
/// existía forma de parametrizar causales propias de prenda, blindaje o duplicados: los trámites de
/// OTROS mostraban al revisor los motivos de una matrícula.</para>
/// </summary>
public static class RejectionReasonFamilies
{
    public const string Matriculas = "MATRICULAS";
    public const string Traspaso = "TRASPASO";
    public const string Otros = "OTROS";

    public static readonly IReadOnlyList<string> Todas = [Matriculas, Traspaso, Otros];

    public static bool EsValida(string? family) =>
        family is not null && Todas.Contains(family.Trim().ToUpperInvariant(), StringComparer.Ordinal);
}
