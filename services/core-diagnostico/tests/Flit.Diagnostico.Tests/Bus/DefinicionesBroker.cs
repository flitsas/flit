using System.Text.Json;
using System.Text.RegularExpressions;

namespace Flit.Diagnostico.Tests.Bus;

/// <summary>Una política de <c>deploy/rabbitmq/definitions.json</c>: nombre, patrón, prioridad y su definición tal cual.</summary>
public sealed record PoliticaBroker(string Nombre, string Patron, int Prioridad, string AplicaA, JsonElement Definicion)
{
    /// <summary>El <c>message-ttl</c> de la política (null si no tiene).</summary>
    public long? TtlMs => Definicion.TryGetProperty("message-ttl", out var ttl) ? ttl.GetInt64() : null;

    /// <summary>Un valor de texto de la definición (null si no está).</summary>
    public string? Texto(string clave) => Definicion.TryGetProperty(clave, out var valor) ? valor.GetString() : null;
}

/// <summary>
/// La topología de <c>deploy/rabbitmq/definitions.json</c> (ADR-0075, H5), copiada a la salida de las pruebas: la única fuente de
/// los exchanges, las colas, sus bindings y las políticas que declara <see cref="BrokerDePrueba"/> y que verifican las pruebas de las
/// opciones del reconciliador. Así una prueba no copia a mano lo que el broker de los ambientes carga de ese archivo.
/// <see cref="PoliticaDe"/> resuelve como RabbitMQ: de las políticas cuyo patrón alcanza la cola, la de mayor prioridad.
/// </summary>
/// <example>
/// <code>
/// var ttl = DefinicionesBroker.PoliticaDe("consultas.diagnostico")?.TtlMs; // 86400000
/// </code>
/// </example>
public static class DefinicionesBroker
{
    private static readonly Lazy<JsonDocument> Documento = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rabbitmq", "definitions.json"))));

    private static JsonElement Raiz => Documento.Value.RootElement;

    /// <summary>Los exchanges declarados (todos topic).</summary>
    public static IReadOnlyList<string> Exchanges => [.. Raiz.GetProperty("exchanges").EnumerateArray().Select(e => e.GetProperty("name").GetString()!)];

    /// <summary>Las colas declaradas.</summary>
    public static IReadOnlyList<string> Colas => [.. Raiz.GetProperty("queues").EnumerateArray().Select(q => q.GetProperty("name").GetString()!)];

    /// <summary>Los bindings exchange → cola con su llave.</summary>
    public static IReadOnlyList<(string Origen, string Cola, string Llave)> Bindings =>
        [.. Raiz.GetProperty("bindings").EnumerateArray().Select(b =>
            (b.GetProperty("source").GetString()!, b.GetProperty("destination").GetString()!, b.GetProperty("routing_key").GetString()!))];

    /// <summary>Las políticas, con su definición tal cual.</summary>
    public static IReadOnlyList<PoliticaBroker> Politicas =>
        [.. Raiz.GetProperty("policies").EnumerateArray().Select(p => new PoliticaBroker(
            p.GetProperty("name").GetString()!, p.GetProperty("pattern").GetString()!, p.GetProperty("priority").GetInt32(),
            p.GetProperty("apply-to").GetString()!, p.GetProperty("definition").Clone()))];

    /// <summary>La política que RabbitMQ aplica a <paramref name="cola"/> (una sola: la de mayor prioridad); null si ninguna la alcanza.</summary>
    public static PoliticaBroker? PoliticaDe(string cola) =>
        Politicas.Where(p => p.AplicaA is "queues" or "all" && Regex.IsMatch(cola, p.Patron)).OrderByDescending(p => p.Prioridad).FirstOrDefault();

    /// <summary>Las políticas cuyo patrón alcanza <paramref name="cola"/> (para detectar choques).</summary>
    public static IReadOnlyList<PoliticaBroker> PoliticasQueAlcanzan(string cola) => [.. Politicas.Where(p => Regex.IsMatch(cola, p.Patron))];
}
