using System.Globalization;
using Flit.Analytics.Application.CompanyQueries;

namespace Flit.Infrastructure.Documents;

/// <summary>
/// Columnas de validación de identidad por persona para los Excel/CSV que arma el servidor (reporte detallado y sus
/// variantes, reportes programados de Consulta). Cada actor ocupa un BLOQUE de columnas adyacentes —Nombre,
/// Documento, Validación, Intentos, Motivo de rechazo— y los bloques van Comprador, Vendedor, Propietario, por
/// ordinal dentro de cada rol. Crece solo si hace falta: la cantidad de bloques por rol es el máximo de actores de ese
/// rol en TODO el filtro (<see cref="MaxActoresPorRolDto"/>), calculado antes de escribir, de modo que todas las
/// partes de un export tienen el mismo ancho. No se trunca. Van AL FINAL de la hoja (convención HU #14319).
/// </summary>
public static class ActoresExcelColumns
{
    /// <summary>Qué columnas del bloque se exportan (en la Consulta el usuario puede ocultar cada una).</summary>
    [Flags]
    public enum Campos
    {
        None = 0,
        /// <summary>Nombre + Documento (la columna «Persona» de pantalla).</summary>
        Persona = 1,
        Validacion = 2,
        Intentos = 4,
        Motivo = 8,
        Todos = Persona | Validacion | Intentos | Motivo,
    }

    /// <summary>Una celda del bloque: texto, o número cuando es el total de intentos.</summary>
    public readonly record struct ActorCell(string Text, int? Number = null);

    private static readonly (string Rol, string Etiqueta, string Minuscula)[] Roles =
    [
        (ActorValidacionRoles.Comprador, "Comprador", "comprador"),
        (ActorValidacionRoles.Vendedor, "Vendedor", "vendedor"),
        (ActorValidacionRoles.Propietario, "Propietario", "propietario"),
    ];

    /// <summary>Columnas que ocupa UN actor con los campos dados.</summary>
    public static int ColumnsPerActor(Campos campos) =>
        (campos.HasFlag(Campos.Persona) ? 2 : 0)
        + (campos.HasFlag(Campos.Validacion) ? 1 : 0)
        + (campos.HasFlag(Campos.Intentos) ? 1 : 0)
        + (campos.HasFlag(Campos.Motivo) ? 1 : 0);

    /// <summary>Cantidad de columnas que suma el bloque para este máximo.</summary>
    public static int Width(MaxActoresPorRolDto max, Campos campos = Campos.Todos) =>
        ColumnsPerActor(campos) * (max.Comprador + max.Vendedor + max.Propietario);

    /// <summary>Encabezados planos (sin celdas combinadas) con el rol en cada uno; desde el segundo actor, número.</summary>
    public static IReadOnlyList<string> Headers(MaxActoresPorRolDto max, Campos campos = Campos.Todos)
    {
        var headers = new List<string>(Width(max, campos));
        foreach (var (rol, etiqueta, minuscula) in Roles)
        {
            for (var n = 1; n <= Cantidad(max, rol); n++)
            {
                var sufijo = n == 1 ? string.Empty : $" {n}";
                if (campos.HasFlag(Campos.Persona))
                {
                    headers.Add($"{etiqueta}{sufijo}");
                    headers.Add($"Documento {minuscula}{sufijo}");
                }

                if (campos.HasFlag(Campos.Validacion))
                    headers.Add($"Validación {minuscula}{sufijo}");
                if (campos.HasFlag(Campos.Intentos))
                    headers.Add($"Intentos {minuscula}{sufijo}");
                if (campos.HasFlag(Campos.Motivo))
                    headers.Add($"Motivo rechazo {minuscula}{sufijo}");
            }
        }

        return headers;
    }

    /// <summary>
    /// Celdas del bloque para una fila. Actor que no existe en la fila: celdas vacías. Actor sin validación:
    /// «Sin validar», intentos 0 y motivo vacío.
    /// </summary>
    public static IReadOnlyList<ActorCell> CellsTyped(
        IReadOnlyList<ActorValidacionDto>? actores, MaxActoresPorRolDto max, Campos campos = Campos.Todos)
    {
        var cells = new List<ActorCell>(Width(max, campos));
        var vacias = Enumerable.Repeat(new ActorCell(string.Empty), ColumnsPerActor(campos)).ToList();

        foreach (var (rol, _, _) in Roles)
        {
            var delRol = (actores ?? [])
                .Where(a => a.Rol == rol)
                .OrderBy(a => a.Ordinal)
                .ToList();

            for (var n = 0; n < Cantidad(max, rol); n++)
            {
                if (n >= delRol.Count)
                {
                    cells.AddRange(vacias);
                    continue;
                }

                var a = delRol[n];
                if (campos.HasFlag(Campos.Persona))
                {
                    cells.Add(new ActorCell(a.Nombre));
                    cells.Add(new ActorCell(Documento(a)));
                }

                if (campos.HasFlag(Campos.Validacion))
                    cells.Add(new ActorCell(ActorValidacionEstados.Etiqueta(a.EstadoValidacion)));
                if (campos.HasFlag(Campos.Intentos))
                    cells.Add(new ActorCell(a.Intentos.ToString(CultureInfo.InvariantCulture), a.Intentos));
                if (campos.HasFlag(Campos.Motivo))
                    cells.Add(new ActorCell(a.MotivoRechazo ?? string.Empty));
            }
        }

        return cells;
    }

    /// <summary>Como <see cref="CellsTyped"/>, todo como texto (Excel del reporte detallado).</summary>
    public static IReadOnlyList<string> Cells(
        IReadOnlyList<ActorValidacionDto>? actores, MaxActoresPorRolDto max, Campos campos = Campos.Todos) =>
        CellsTyped(actores, max, campos).Select(c => c.Text).ToList();

    /// <summary>
    /// «tipo número» separados por un espacio (como la pantalla y el Excel del navegador); solo el número si no hay
    /// tipo; vacío si no hay documento.
    /// </summary>
    public static string Documento(ActorValidacionDto a)
    {
        var numero = a.Documento?.Trim();
        if (string.IsNullOrEmpty(numero))
            return string.Empty;
        var tipo = a.TipoDocumento?.Trim();
        return string.IsNullOrEmpty(tipo) ? numero : $"{tipo} {numero}";
    }

    /// <summary>
    /// Posiciones (base 0, ya desplazadas por <paramref name="offset"/> columnas previas) de las columnas «Intentos …»
    /// dentro del bloque, para escribirlas como NÚMERO y no como texto.
    /// </summary>
    public static IReadOnlySet<int> IntentosColumns(MaxActoresPorRolDto max, int offset, Campos campos = Campos.Todos)
    {
        var set = new HashSet<int>();
        if (!campos.HasFlag(Campos.Intentos))
            return set;

        var porActor = ColumnsPerActor(campos);
        var antes = (campos.HasFlag(Campos.Persona) ? 2 : 0) + (campos.HasFlag(Campos.Validacion) ? 1 : 0);
        for (var i = 0; i < max.Comprador + max.Vendedor + max.Propietario; i++)
            set.Add(offset + i * porActor + antes);
        return set;
    }

    private static int Cantidad(MaxActoresPorRolDto max, string rol) => rol switch
    {
        ActorValidacionRoles.Comprador => max.Comprador,
        ActorValidacionRoles.Vendedor => max.Vendedor,
        _ => max.Propietario,
    };
}
