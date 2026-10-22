using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Flit.Tramites.Domain.ExternalSync;

/// <summary>
/// HU #13081 — cursor opaco del feed (contrato v3.1 §3): base64url de un JSON versionado. Lleva la
/// posición (transacción y versión, ADR-0066 punto 7) o, si una página que arrancó por fecha vino vacía,
/// esa fecha, para que la siguiente corrida siga desde ahí sin perder nada.
/// <code>
/// {"v":1,"x":"900","sv":48213}              posición
/// {"v":1,"since":"2026-09-21T10:15:00-05:00"}  arranque por fecha aún sin cambios
/// </code>
/// La transacción va como texto: es un entero de 64 bits sin signo que no cabe en un número de JSON
/// seguro para todos los clientes.
/// </summary>
public static class ExternalSyncCursor
{
    private const int Version = 1;

    public static string Encode(ProcedureSyncPosition position) =>
        Pack(new CursorBody(Version, position.Transaction.ToString(CultureInfo.InvariantCulture), position.Version, null));

    public static string EncodeSince(DateTimeOffset since) =>
        Pack(new CursorBody(Version, null, null, since.ToString("O", CultureInfo.InvariantCulture)));

    /// <summary>Posición inicial (inicio del histórico): transacción 0, versión 0.</summary>
    public static ProcedureSyncPosition Start => new(0, 0);

    /// <summary>
    /// Decodifica el cursor. Exactamente uno de <paramref name="position"/> o <paramref name="since"/> sale con
    /// valor. <c>false</c> si el cursor no es base64url, no es JSON, tiene otra versión o le faltan campos.
    /// </summary>
    public static bool TryDecode(string? cursor, out ProcedureSyncPosition? position, out DateTimeOffset? since)
    {
        position = null;
        since = null;
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 512)
        {
            return false;
        }

        try
        {
            var bytes = Base64Url.DecodeFromChars(cursor);
            var body = JsonSerializer.Deserialize<CursorBody>(bytes, JsonOptions);
            if (body is null || body.V != Version)
            {
                return false;
            }

            if (body.Since is not null && body.X is null && body.Sv is null)
            {
                if (!DateTimeOffset.TryParseExact(body.Since, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s))
                {
                    return false;
                }

                since = s;
                return true;
            }

            if (body.Since is null && body.X is not null && body.Sv is { } sv && sv >= 0
                && ulong.TryParse(body.X, NumberStyles.None, CultureInfo.InvariantCulture, out var x))
            {
                position = new ProcedureSyncPosition(x, sv);
                return true;
            }

            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static string Pack(CursorBody body) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, JsonOptions)));

    private sealed record CursorBody(int V, string? X, long? Sv, string? Since);
}
