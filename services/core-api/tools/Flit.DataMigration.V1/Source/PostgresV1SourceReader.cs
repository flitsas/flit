using System.Globalization;
using Npgsql;

namespace Flit.DataMigration.V1.Source;

/// <summary>
/// Par de tablas de V1 que definen un tipo de trámite: el master y su historial de estados.
/// <para>
/// Traspaso y matrícula tienen el MISMO shape de historial —mismas columnas, incluida
/// <c>id_vtmas</c> como FK al master (herencia de haber copiado la tabla en V1)—, así que el
/// mismo lector sirve para ambos con solo cambiarle los nombres.
/// </para>
/// </summary>
public sealed record V1SourceTables(string Master, string History)
{
    public static readonly V1SourceTables Transfer =
        new("vehicle_transfer_master", "vehicle_transfer_process_status");

    public static readonly V1SourceTables Registration =
        new("vehicle_registration_master", "vehicle_registration_process_status");
}

/// <summary>
/// Lee trámites desde una COPIA de la base de V1 (nunca de la V1 viva).
/// Solo hace SELECT: el origen jamás se modifica.
/// </summary>
public sealed class PostgresV1SourceReader(string connectionString, V1SourceTables? tables = null)
    : IV1SourceReader
{
    private readonly V1SourceTables _tables = tables ?? V1SourceTables.Transfer;

    private string MasterTable => _tables.Master;

    private string HistoryTable => _tables.History;

    public async Task<IReadOnlyList<V1SourceRecord>> ReadAsync(
        IReadOnlyList<long> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var idArray = ids.ToArray();
        var history = await ReadHistoryAsync(connection, idArray, cancellationToken);
        var records = new List<V1SourceRecord>(ids.Count);

        // SELECT * a propósito: el mapeo es data-driven (ver TransferFieldMap) y así una
        // columna nueva en V1 no obliga a tocar este lector.
        await using var command = new NpgsqlCommand(
            $"SELECT * FROM {MasterTable} WHERE id = ANY(@ids)", connection);
        command.Parameters.AddWithValue("ids", idArray);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var columns = new Dictionary<string, string?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns[reader.GetName(i)] = reader.IsDBNull(i) ? null : Stringify(reader.GetValue(i));
            }

            var id = long.Parse(columns["id"]!, CultureInfo.InvariantCulture);
            records.Add(new V1SourceRecord
            {
                Id = id,
                SourceTable = MasterTable,
                CompanyNit = columns.GetValueOrDefault("company_registered"),
                ProcessStatus = int.Parse(columns["process_status"]!, CultureInfo.InvariantCulture),
                Columns = columns,
                StatusHistory = history.GetValueOrDefault(id, []),
            });
        }

        // Se devuelve en el mismo orden en que los pidieron: el operador ve el reporte
        // en el orden que entregó, no en el que la base quiso.
        var byId = records.ToDictionary(r => r.Id);
        return [.. ids.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    private async Task<Dictionary<long, List<V1StatusEvent>>> ReadHistoryAsync(
        NpgsqlConnection connection,
        long[] ids,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<long, List<V1StatusEvent>>();

        // La fecha del evento se toma de `created_at`, NO de `registrationdate`.
        //
        // `registrationdate` mezcla zonas horarias: V1 guarda el evento de creación (Draft) en UTC y
        // las transiciones posteriores en hora local de Colombia (UTC-5). `created_at` es siempre el
        // instante UTC de inserción. Medido sobre pdn el 2026-07-28:
        //
        //                      cronología incorrecta al ordenar por…
        //                      registrationdate      created_at      total
        //   traspasos              20.893                 2          32.870
        //   matrículas             21.028                31          26.176
        //
        //   y la diferencia entre ambas columnas es de exactamente 0 h o 5 h en 259.692 de las
        //   259.694 filas de traspaso (las 2 restantes son un retraso de inserción del mismo
        //   trámite, no otra zona) y en las 195.220 de matrícula sin una sola excepción.
        //
        // Importa porque V2 REORDENA el historial por `changed_at` al pintarlo: migrar la fecha sin
        // normalizar hacía que el Borrador apareciera en mitad de la secuencia y se vieran pares sin
        // sentido ("Preparado desde Preparado"). `registrationdate` no se pierde: viaja en metadata.
        //
        // ORDER BY id (serial) porque el orden de inserción es el cronológico real.
        await using var command = new NpgsqlCommand(
            $"""
             SELECT id_vtmas, id_parprosta, created_at, userregistered,
                    mail_userregistered, role_userregistered, observation, id, registrationdate
             FROM {HistoryTable}
             WHERE id_vtmas = ANY(@ids) AND deleted_at IS NULL
             ORDER BY id_vtmas, id
             """, connection);
        command.Parameters.AddWithValue("ids", ids);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var masterId = reader.GetInt64(0);
            if (!result.TryGetValue(masterId, out var events))
            {
                events = [];
                result[masterId] = events;
            }

            events.Add(new V1StatusEvent
            {
                StatusId = reader.GetInt32(1),
                ChangedAt = reader.GetDateTime(2),
                UserName = NullIfBlank(reader, 3),
                UserEmail = NullIfBlank(reader, 4),
                UserRole = NullIfBlank(reader, 5),
                Observation = NullIfBlank(reader, 6),
                SourceId = reader.GetInt64(7),
                LegacyRegistrationDate = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            });
        }

        return result;
    }

    private static string? NullIfBlank(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Blank(reader.GetString(ordinal));

    /// <summary>
    /// V1 usa cadena vacía en vez de NULL. Sin esta normalización migraríamos ~30 adjuntos
    /// vacíos y decenas de campos en blanco por cada trámite.
    /// </summary>
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Convierte cualquier valor de Postgres a texto estable e invariante. Importa que sea
    /// determinístico: el mismo dato de V1 debe producir siempre el mismo texto en V2,
    /// o la reconciliación y la idempotencia dejan de cuadrar.
    /// </summary>
    private static string? Stringify(object value) => value switch
    {
        null or DBNull => null,
        string s => Blank(s),
        bool b => b ? "true" : "false",
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString(CultureInfo.InvariantCulture),
        float f => f.ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => Blank(formattable.ToString(null, CultureInfo.InvariantCulture)),
        _ => Blank(value.ToString()),
    };
}
