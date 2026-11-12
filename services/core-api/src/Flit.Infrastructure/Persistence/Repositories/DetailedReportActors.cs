using System.Data.Common;
using System.Text.Json;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Tramites.Domain.Identity;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lectura de la columna <c>actors</c> (jsonb) de <c>analytics.v_procedure_detail_report</c> (DDL 189) y cálculo del
/// máximo de actores por rol sobre TODO el filtro. Compartido por el repositorio de la empresa y el de red/todas las
/// compañías para que ambos entreguen exactamente la misma forma.
/// </summary>
internal static class DetailedReportActors
{
    // Sin interpolación: el barrido SqlInterpolationArchitectureTests lee estos archivos. El filtro del repositorio
    // (FROM … WHERE …) entra por concatenación dentro de una subconsulta, así que es el MISMO del listado y el export.
    private const string MaxHead = """
        SELECT coalesce(max(c.comprador), 0)::int, coalesce(max(c.vendedor), 0)::int, coalesce(max(c.propietario), 0)::int
        FROM (
            SELECT
                count(*) FILTER (WHERE e ->> 'rol' = 'comprador')   AS comprador,
                count(*) FILTER (WHERE e ->> 'rol' = 'vendedor')    AS vendedor,
                count(*) FILTER (WHERE e ->> 'rol' = 'propietario') AS propietario
            FROM (
                SELECT v.id, v.actors
        """;

    private const string MaxTail = """
            ) s
            CROSS JOIN LATERAL jsonb_array_elements(s.actors) e
            GROUP BY s.id
        ) c;
        """;

    /// <summary>
    /// Máximo de actores por rol (comprador, vendedor, propietario) sobre TODO el filtro. Recibe el fragmento
    /// <c>FROM … WHERE …</c> del repositorio (que empieza y termina en salto de línea).
    /// </summary>
    internal static string MaxSql(string baseFrom) => MaxHead + baseFrom + MaxTail;

    internal static async Task<MaxActoresPorRolDto> ReadMaxAsync(DbCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new MaxActoresPorRolDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2))
            : default;
    }

    /// <summary>Interpreta el jsonb de la vista. Nulo o ilegible ⇒ lista vacía (nunca lanza: es un reporte).</summary>
    internal static IReadOnlyList<ActorValidacionDto> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var lista = new List<ActorValidacionDto>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var estado = Str(e, "estadoValidacion") ?? ActorValidacionEstados.SinValidacion;
                lista.Add(new ActorValidacionDto(
                    Rol: Str(e, "rol") ?? string.Empty,
                    Ordinal: e.TryGetProperty("ordinal", out var o) && o.TryGetInt32(out var n) ? n : 1,
                    Nombre: Str(e, "nombre") ?? string.Empty,
                    TipoDocumento: Str(e, "tipoDocumento"),
                    Documento: Str(e, "documento"),
                    EstadoValidacion: estado,
                    MotivoRechazo: Motivo(e),
                    Intentos: e.TryGetProperty("intentos", out var i) && i.TryGetInt32(out var k) ? k : 0));
            }

            return lista;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Motivo(JsonElement actor)
    {
        if (!actor.TryGetProperty("rechazo", out var r) || r.ValueKind != JsonValueKind.Object)
            return null;
        return IdentityRejectionReasonText.ForParts(
            Str(r, "codigo"), Str(r, "motivo"), Str(r, "subjectMotivo"), Bool(r, "documentoOk"), Bool(r, "nombreOk"));
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool? Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p)
            ? p.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
            : null;
}
