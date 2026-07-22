namespace Flit.Tramites.Application.UseCases.ProcedureInstances.BulkImport;

/// <summary>
/// Mapea una grilla de celdas (primera fila con contenido = encabezado) a filas de importación de
/// trámites. Compartido por el parser CSV y el lector XLSX para que ambos apliquen las MISMAS reglas
/// de columnas, saltos de fila vacía y límite de filas. Columnas reconocidas (case-insensitive, en
/// cualquier orden): <c>modalidad, tipo_codigo, oficina_transito_codigo, vin, placa</c>.
/// </summary>
public static class ProcedureImportRowMapper
{
    /// <summary>Máximo de filas de datos admitidas por archivo (rechazo explícito si se supera).</summary>
    public const int DefaultMaxRows = 500;

    private const string ColModalidad = "modalidad";
    private const string ColTipoCodigo = "tipo_codigo";
    private const string ColOficina = "oficina_transito_codigo";
    private const string ColVin = "vin";
    private const string ColPlaca = "placa";

    public static ProcedureImportParseOutcome Map(
        IReadOnlyList<IReadOnlyList<string>> grid,
        int maxRows = DefaultMaxRows)
    {
        var headerIndex = -1;
        for (var i = 0; i < grid.Count; i++)
        {
            if (grid[i].Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                headerIndex = i;
                break;
            }
        }

        if (headerIndex < 0)
            return new ProcedureImportParseOutcome([], "El archivo está vacío.");

        var header = grid[headerIndex];
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var key = header[i].Trim();
            if (key.Length > 0 && !columns.ContainsKey(key))
                columns[key] = i;
        }

        if (!columns.ContainsKey(ColModalidad) && !columns.ContainsKey(ColTipoCodigo))
            return new ProcedureImportParseOutcome([],
                "El encabezado debe incluir al menos la columna 'modalidad' o 'tipo_codigo'.");

        var rows = new List<ProcedureImportRow>();
        var dataRow = 0;
        for (var i = headerIndex + 1; i < grid.Count; i++)
        {
            var cells = grid[i];
            if (cells.All(string.IsNullOrWhiteSpace))
                continue;

            dataRow++;
            if (dataRow > maxRows)
                return new ProcedureImportParseOutcome([],
                    $"El archivo supera el máximo de {maxRows} filas de datos permitidas por importación.");

            rows.Add(new ProcedureImportRow(
                RowNumber: dataRow,
                Modalidad: Cell(cells, columns, ColModalidad),
                TipoCodigo: Cell(cells, columns, ColTipoCodigo),
                TransitOfficeCode: Cell(cells, columns, ColOficina),
                Vin: Cell(cells, columns, ColVin),
                Placa: Cell(cells, columns, ColPlaca)));
        }

        return rows.Count == 0
            ? new ProcedureImportParseOutcome([], "El archivo no tiene filas de datos.")
            : new ProcedureImportParseOutcome(rows, null);
    }

    private static string? Cell(IReadOnlyList<string> cells, Dictionary<string, int> columns, string name)
    {
        if (!columns.TryGetValue(name, out var idx) || idx >= cells.Count)
            return null;
        var value = cells[idx].Trim();
        return value.Length == 0 ? null : value;
    }
}
