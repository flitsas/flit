using System.Globalization;
using Flit.Analytics.Application.CompanyQueries;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Columnas de validación de identidad POR ROL para los Excel que arma el servidor (reporte detallado y sus variantes,
/// reportes programados de Consulta). Contrato v3.
///
/// <para><b>Dos grupos.</b> COMPRADOR agrupa a los actores con rol <c>comprador</c> o <c>propietario</c> (en matrícula y
/// «otros» el propietario va en las columnas de comprador) y VENDEDOR a los de rol <c>vendedor</c>. Ancho del grupo
/// comprador = <c>max(comprador, propietario)</c>; vendedor = <c>vendedor</c>; mínimo 1 bloque por rol aunque el máximo
/// sea 0 (la columna Comprador siempre existió).</para>
///
/// <para><b>Agrupado por persona.</b> Por cada actor <c>i</c> del rol salen, juntas: <c>Comprador</c>,
/// <c>Validación comprador</c>, <c>Intentos comprador</c>, <c>Motivo rechazo comprador</c>; desde el segundo, con número
/// (<c>Comprador 2</c>, <c>Validación comprador 2</c>…). Sin celdas combinadas y sin columna de documento. Solo se
/// escriben los campos pedidos. El máximo se calcula antes de escribir sobre TODO el filtro, así que todas las partes de
/// un export por lotes tienen el mismo ancho. No se trunca.</para>
/// </summary>
public static class ActoresExcelColumns
{
    /// <summary>Qué columnas del bloque de un rol se escriben (cada una es una columna que el usuario puede ocultar).</summary>
    [Flags]
    public enum Campos
    {
        None = 0,
        /// <summary>El nombre de la persona (la columna «Comprador» / «Vendedor»).</summary>
        Nombre = 1,
        Validacion = 2,
        Intentos = 4,
        Motivo = 8,
        Todos = Nombre | Validacion | Intentos | Motivo,
    }

    public enum Grupo
    {
        Comprador,
        Vendedor,
    }

    /// <summary>Una celda del bloque: texto, o número cuando es el total de intentos.</summary>
    public readonly record struct ActorCell(string Text, int? Number = null);

    /// <summary>Anchos de columna sugeridos (caracteres) para quien necesite dimensionar la hoja.</summary>
    public const int AnchoNombre = 26;
    public const int AnchoValidacion = 24;
    public const int AnchoIntentos = 12;
    public const int AnchoMotivo = 34;

    /// <summary>Cantidad de bloques (actores) del grupo: el máximo del filtro, nunca menos de 1.</summary>
    public static int Ancho(MaxActoresPorRolDto max, Grupo grupo) => grupo == Grupo.Comprador
        ? Math.Max(1, Math.Max(max.Comprador, max.Propietario))
        : Math.Max(1, max.Vendedor);

    /// <summary>Columnas que ocupa un actor con los campos dados.</summary>
    public static int ColumnsPerActor(Campos campos) =>
        (campos.HasFlag(Campos.Nombre) ? 1 : 0)
        + (campos.HasFlag(Campos.Validacion) ? 1 : 0)
        + (campos.HasFlag(Campos.Intentos) ? 1 : 0)
        + (campos.HasFlag(Campos.Motivo) ? 1 : 0);

    /// <summary>Encabezados planos del grupo; desde el segundo actor, número.</summary>
    public static IReadOnlyList<string> Headers(MaxActoresPorRolDto max, Grupo grupo, Campos campos = Campos.Todos)
    {
        var (etiqueta, minuscula) = Nombres(grupo);
        var headers = new List<string>();
        for (var n = 1; n <= Ancho(max, grupo); n++)
        {
            var sufijo = n == 1 ? string.Empty : $" {n}";
            if (campos.HasFlag(Campos.Nombre))
                headers.Add($"{etiqueta}{sufijo}");
            if (campos.HasFlag(Campos.Validacion))
                headers.Add($"Validación {minuscula}{sufijo}");
            if (campos.HasFlag(Campos.Intentos))
                headers.Add($"Intentos {minuscula}{sufijo}");
            if (campos.HasFlag(Campos.Motivo))
                headers.Add($"Motivo rechazo {minuscula}{sufijo}");
        }

        return headers;
    }

    /// <summary>
    /// Ancho de columna sugerido de cada encabezado de <see cref="Headers"/>, en el mismo orden.
    /// </summary>
    public static IReadOnlyList<int> Widths(MaxActoresPorRolDto max, Grupo grupo, Campos campos = Campos.Todos)
    {
        var widths = new List<int>();
        for (var n = 1; n <= Ancho(max, grupo); n++)
        {
            if (campos.HasFlag(Campos.Nombre)) widths.Add(AnchoNombre);
            if (campos.HasFlag(Campos.Validacion)) widths.Add(AnchoValidacion);
            if (campos.HasFlag(Campos.Intentos)) widths.Add(AnchoIntentos);
            if (campos.HasFlag(Campos.Motivo)) widths.Add(AnchoMotivo);
        }

        return widths;
    }

    /// <summary>
    /// Celdas del grupo para una fila. Actor que no existe en la fila: celdas vacías. Actor sin validación: «Sin validar»,
    /// intentos 0 y motivo vacío. <paramref name="nombreSinActores"/> es el texto viejo de la columna
    /// (<c>comprador</c>/<c>vendedor</c> del trámite) cuando la fila no trae actores: la columna Comprador siempre existió.
    /// </summary>
    public static IReadOnlyList<ActorCell> CellsTyped(
        IReadOnlyList<ActorValidacionDto>? actores,
        MaxActoresPorRolDto max,
        Grupo grupo,
        Campos campos = Campos.Todos,
        string? nombreSinActores = null)
    {
        var delGrupo = (actores ?? [])
            .Where(a => PerteneceAlGrupo(a.Rol, grupo))
            .OrderBy(a => a.Ordinal)
            .ToList();

        var cells = new List<ActorCell>();
        for (var n = 0; n < Ancho(max, grupo); n++)
        {
            if (n >= delGrupo.Count)
            {
                var fallback = n == 0 && (actores is null || actores.Count == 0) ? nombreSinActores : null;
                if (campos.HasFlag(Campos.Nombre)) cells.Add(new ActorCell(fallback ?? string.Empty));
                if (campos.HasFlag(Campos.Validacion)) cells.Add(new ActorCell(string.Empty));
                if (campos.HasFlag(Campos.Intentos)) cells.Add(new ActorCell(string.Empty));
                if (campos.HasFlag(Campos.Motivo)) cells.Add(new ActorCell(string.Empty));
                continue;
            }

            var a = delGrupo[n];
            if (campos.HasFlag(Campos.Nombre))
                cells.Add(new ActorCell(a.Nombre));
            if (campos.HasFlag(Campos.Validacion))
                cells.Add(new ActorCell(ActorValidacionEstados.Etiqueta(a.EstadoValidacion)));
            if (campos.HasFlag(Campos.Intentos))
                cells.Add(new ActorCell(a.Intentos.ToString(CultureInfo.InvariantCulture), a.Intentos));
            if (campos.HasFlag(Campos.Motivo))
                cells.Add(new ActorCell(a.MotivoRechazo ?? string.Empty));
        }

        return cells;
    }

    /// <summary>Como <see cref="CellsTyped"/>, todo como texto.</summary>
    public static IReadOnlyList<string> Cells(
        IReadOnlyList<ActorValidacionDto>? actores,
        MaxActoresPorRolDto max,
        Grupo grupo,
        Campos campos = Campos.Todos,
        string? nombreSinActores = null) =>
        CellsTyped(actores, max, grupo, campos, nombreSinActores).Select(c => c.Text).ToList();

    private static bool PerteneceAlGrupo(string rol, Grupo grupo) => grupo == Grupo.Comprador
        ? rol is ActorValidacionRoles.Comprador or ActorValidacionRoles.Propietario
        : rol == ActorValidacionRoles.Vendedor;

    private static (string Etiqueta, string Minuscula) Nombres(Grupo grupo) =>
        grupo == Grupo.Comprador ? ("Comprador", "comprador") : ("Vendedor", "vendedor");
}
