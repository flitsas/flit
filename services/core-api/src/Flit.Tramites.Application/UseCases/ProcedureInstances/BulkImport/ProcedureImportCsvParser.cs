using System.Text;

namespace Flit.Tramites.Application.UseCases.ProcedureInstances.BulkImport;

/// <summary>Resultado del parseo del archivo: filas + error fatal de formato (encabezado/vacío/límite).</summary>
public sealed record ProcedureImportParseOutcome(
    IReadOnlyList<ProcedureImportRow> Rows,
    string? FatalError);

/// <summary>
/// Parser CSV puro (sin dependencias) para la importación masiva de trámites. Un Excel exportado
/// como CSV UTF-8 encaja aquí. Acepta separador coma y comillas dobles con escape <c>""</c>;
/// tokeniza a una grilla y delega el mapeo de columnas/reglas en <see cref="ProcedureImportRowMapper"/>.
/// </summary>
public static class ProcedureImportCsvParser
{
    public static ProcedureImportParseOutcome Parse(
        string? content,
        int maxRows = ProcedureImportRowMapper.DefaultMaxRows)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new ProcedureImportParseOutcome([], "El archivo está vacío.");

        var grid = SplitLines(content)
            .Select(line => (IReadOnlyList<string>)SplitCsvLine(line))
            .ToList();

        return ProcedureImportRowMapper.Map(grid, maxRows);
    }

    private static List<string> SplitLines(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal)
               .Replace('\r', '\n')
               .Split('\n')
               .ToList();

    /// <summary>Divide una línea CSV respetando comillas dobles y el escape <c>""</c>.</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        fields.Add(sb.ToString());
        return fields;
    }
}
