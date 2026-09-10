namespace Flit.Ict.Domain.Trazabilidad;

/// <summary>
/// Un dato del trámite, ya listo para pintar: etiqueta en lenguaje de usuario y valor formateado.
/// </summary>
/// <remarks>
/// Se devuelve como lista de pares y no como un objeto tipado a propósito. La petición que envía el
/// cliente tiene decenas de campos que varían por tipo de trámite (las limitaciones solo aplican a
/// prenda, el blindaje solo a blindaje…), y un contrato tipado obligaría a la pantalla a saber cuál
/// aplica a cada caso. El servidor decide qué es relevante y con qué nombre se llama.
/// </remarks>
public sealed record DatoTramite(string Etiqueta, string? Valor, bool EsSensible = false);

/// <summary>Una sección de negocio del detalle: transacción, un actor, adjuntos.</summary>
public sealed record SeccionDatos(string Titulo, IReadOnlyList<DatoTramite> Datos);

/// <summary>Datos recibidos de un pre-trámite, agrupados por significado (HU #11819).</summary>
public sealed record DatosTramite(long Numero, IReadOnlyList<SeccionDatos> Secciones);

/// <summary>Lectura de los datos recibidos. Solo lectura, con la PII ya enmascarada.</summary>
public interface IDatosTramiteQuery
{
    /// <summary>Null cuando el trámite no existe o es de otro tenant.</summary>
    Task<DatosTramite?> ConsultarAsync(long numero, Guid? tenantId, CancellationToken ct = default);
}

/// <summary>
/// Una petición HTTP que tocó a este trámite.
/// </summary>
/// <param name="TramitesEnLaPeticion">
/// Cuántos pre-trámites viajaban en la MISMA petición. Es el dato que explica por qué el Log ICT
/// resulta ilegible: una llamada de registro trae hasta veinte, y sin decirlo la pantalla parece
/// estar enseñando peticiones repetidas.
/// </param>
public sealed record EventoLogTramite(
    Guid Id,
    DateTime Ocurrido,
    string Tipo,
    string Direccion,
    string Metodo,
    string Ruta,
    int Codigo,
    int DuracionMs,
    int TramitesEnLaPeticion);

/// <summary>Lectura del log HTTP acotado a un trámite. Solo lectura.</summary>
public interface ILogTramiteQuery
{
    /// <summary>Null cuando el trámite no existe o es de otro tenant.</summary>
    Task<IReadOnlyList<EventoLogTramite>?> ConsultarAsync(
        long numero, Guid? tenantId, CancellationToken ct = default);
}

/// <summary>Traduce el vocabulario de los actores y del log a lenguaje de usuario.</summary>
public static class EtiquetasDetalle
{
    public static string Actor(string? tipo) => tipo?.ToLowerInvariant() switch
    {
        "seller" => "Vendedor",
        "buyer" => "Comprador",
        "lessee" => "Locatario",
        null or "" => "Actor",
        _ => tipo!,
    };

    public static string TipoLog(string? tipo) => tipo?.ToLowerInvariant() switch
    {
        "auth" => "Autenticación",
        "transaction" => "Transacción",
        "webhook" => "Webhook",
        "external" => "Fuente externa",
        null or "" => "—",
        _ => tipo!,
    };

    public static string Direccion(string? direccion) => direccion?.ToLowerInvariant() switch
    {
        "inbound" => "Entrante",
        "outbound" => "Saliente",
        null or "" => "—",
        _ => direccion!,
    };
}
