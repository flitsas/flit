using Flit.DataMigration.V1.Mapping;
using Flit.DataMigration.V1.Source;

namespace Flit.DataMigration.V1.Orchestration;

/// <summary>
/// Columnas <c>id_attach*</c> que la tabla de V1 tiene y el mapa de adjuntos no declara — ni
/// mapeadas ni excluidas.
/// <para>
/// El aviso por trámite solo salta cuando la columna trae valor, así que una columna nueva de V1
/// puede pasar meses invisible y aparecer el día de la migración real con datos. Esto la detecta
/// mirando el ESQUEMA, no los datos: basta con leer un trámite para verla.
/// </para>
/// </summary>
public static class UndeclaredAttachmentColumns
{
    public static IReadOnlyList<string> Detect(V1ProcedureKind kind, IReadOnlyList<V1SourceRecord> records)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return [];
        }

        // OrdinalIgnoreCase: los nombres de columna de V1 no son consistentes en mayúsculas y una
        // comparación sensible reportaría como "sin declarar" algo que sí está mapeado.
        var declaradas = kind.AttachmentMap.DeclaredColumns().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. records[0].Columns.Keys
            .Where(V1ProcedureKind.IsAttachmentColumn)
            .Where(c => !declaradas.Contains(c))
            .OrderBy(c => c, StringComparer.Ordinal)];
    }
}
