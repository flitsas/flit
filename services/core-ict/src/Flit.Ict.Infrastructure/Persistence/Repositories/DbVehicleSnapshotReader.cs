using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Validation;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>
/// Bug #13304 — lee y purga el resultado completo de la consulta RUNT de vehículo que el orquestador
/// guardó en <c>ict.external_integration_source_response.vehicle_snapshot</c> (DDL 26). La columna tiene
/// PII (titular, acreedor): ningún lector de trazabilidad la selecciona y se vacía al materializar.
/// Corre desde los jobs (RLS saltada por el rol de sistema).
/// </summary>
public sealed class DbVehicleSnapshotReader(IctDbContext db) : IIctVehicleSnapshotReader
{
    /// <summary>
    /// ¿El master tiene consulta de vehículo (VEHICLE/VIN)? y la respuesta más reciente con resultado
    /// completo de una consulta ya hecha. Un reintento deja varias respuestas: manda la última.
    /// </summary>
    internal const string LatestSql = """
        SELECT EXISTS (
                   SELECT 1 FROM ict.external_integration_source_query sq
                   WHERE sq.eim_id = @master AND sq.query_type IN ('VEHICLE', 'VIN')) AS requires_vehicle,
               (SELECT sr.vehicle_snapshot::text
                FROM ict.external_integration_source_response sr
                JOIN ict.external_integration_source_query sq ON sq.id = sr.eisq_id
                WHERE sq.eim_id = @master AND sq.query_type IN ('VEHICLE', 'VIN')
                  AND sq.is_data_queried = true AND sr.vehicle_snapshot IS NOT NULL
                ORDER BY sr.created_at DESC
                LIMIT 1) AS vehicle_snapshot
        """;

    /// <summary>Minimización de PII: tras materializar no queda resultado completo de ese master.</summary>
    internal const string PurgeSql = """
        UPDATE ict.external_integration_source_response sr
        SET vehicle_snapshot = NULL
        FROM ict.external_integration_source_query sq
        WHERE sq.id = sr.eisq_id AND sq.eim_id = @master AND sr.vehicle_snapshot IS NOT NULL
        """;

    public async Task<VehicleSnapshotLookup> GetLatestAsync(Guid masterId, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = LatestSql;
            AddParam(cmd, "master", masterId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return new VehicleSnapshotLookup(false, null);
            }

            var requires = reader.GetBoolean(0);
            var json = await reader.IsDBNullAsync(1, ct) ? null : reader.GetString(1);
            return new VehicleSnapshotLookup(requires, VehicleSnapshotColumn.Parse(json));
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<int> PurgeAsync(Guid masterId, CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = PurgeSql;
            AddParam(cmd, "master", masterId);
            return await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}

/// <summary>
/// Bug #13304 — formato de la columna <c>vehicle_snapshot</c>:
/// <c>{ "snapshot_json", "consulted_at", "provider", "kind", "plate", "vin" }</c>. Lo escribe el
/// orquestador y lo lee el envío a core-api; <c>snapshot_json</c> se guarda como texto opaco.
/// </summary>
internal static class VehicleSnapshotColumn
{
    private sealed record Column(
        [property: JsonPropertyName("snapshot_json")] string SnapshotJson,
        [property: JsonPropertyName("consulted_at")] string ConsultedAt,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("plate")] string Plate,
        [property: JsonPropertyName("vin")] string Vin);

    /// <summary>
    /// JSON de la columna para la respuesta de una source_query, o null si no aplica: solo VEHICLE/VIN y
    /// solo si core-api devolvió el resultado completo. Placa o VIN según la kind con que se consultó.
    /// </summary>
    internal static string? ForQuery(string queryType, string plate, string vin, VehicleConsultationSnapshot? vehicle)
    {
        if (vehicle is null || string.IsNullOrWhiteSpace(vehicle.SnapshotJson))
        {
            return null;
        }

        var kind = vehicle.Kind;
        if (string.IsNullOrWhiteSpace(kind))
        {
            kind = queryType?.ToUpperInvariant() switch
            {
                "VEHICLE" => VehicleConsultationSnapshot.KindPlate,
                "VIN" => VehicleConsultationSnapshot.KindVin,
                _ => string.Empty,
            };
        }

        var porVin = string.Equals(kind, VehicleConsultationSnapshot.KindVin, StringComparison.Ordinal);
        var porPlaca = string.Equals(kind, VehicleConsultationSnapshot.KindPlate, StringComparison.Ordinal);
        if (!porVin && !porPlaca)
        {
            return null;
        }

        return JsonSerializer.Serialize(new Column(
            vehicle.SnapshotJson,
            vehicle.ConsultedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            vehicle.Provider ?? string.Empty,
            kind,
            porPlaca ? (plate ?? string.Empty).Trim() : string.Empty,
            porVin ? (vin ?? string.Empty).Trim() : string.Empty));
    }

    /// <summary>Lee la columna; null si está vacía o es ilegible (equivale a «sin consulta RUNT»).</summary>
    internal static VehicleConsultationSnapshot? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var column = JsonSerializer.Deserialize<Column>(json);
            if (column is null
                || string.IsNullOrWhiteSpace(column.SnapshotJson)
                || !DateTimeOffset.TryParse(column.ConsultedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var consultedAt))
            {
                return null;
            }

            return new VehicleConsultationSnapshot(
                column.SnapshotJson, consultedAt, column.Provider ?? string.Empty, column.Kind ?? string.Empty,
                column.Plate ?? string.Empty, column.Vin ?? string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
