using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flit.Platform.Sdk.Messaging;

/// <summary>
/// Sobre común de todo mensaje del bus (contrato de plataforma §7, <c>contracts/asyncapi/comun/sobre.v1.yaml</c>).
/// Se serializa en JSON camelCase; <see cref="EventId"/> (UUIDv7) es la llave de idempotencia de los consumidores.
/// </summary>
public sealed record EventEnvelope(
    Guid EventId,
    string Type,
    int Version,
    DateTimeOffset OccurredAt,
    Guid TenantId,
    string Producer,
    string CorrelationId,
    JsonElement Data)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static EventEnvelope FromJson(ReadOnlySpan<byte> utf8) =>
        JsonSerializer.Deserialize<EventEnvelope>(utf8, JsonOptions)
        ?? throw new JsonException("El mensaje no trae un sobre.");

    /// <summary>Lee <see cref="Data"/> como <typeparamref name="T"/>.</summary>
    public T DataAs<T>() => Data.Deserialize<T>(JsonOptions)
        ?? throw new JsonException($"data no es un {typeof(T).Name}.");

    /// <summary>Exchange del productor: <c>flit.&lt;productor&gt;</c> (contrato §7).</summary>
    public static string ExchangeFor(string producer) => $"flit.{producer}";
}
