namespace Flit.Impuestos.Api.Modulos.ApiLiquidacion;

/// <summary>
/// Qué se sabe de una llamada al API de liquidación (plan §5.6, D-ALTA-REINTENTOS). Decide si hay reintento automático: solo
/// <see cref="NoSalio"/> lo admite.
/// </summary>
public enum ClaseEnvio
{
    /// <summary>Hay certeza de que la petición no llegó al API (DNS, conexión rechazada, TLS en el handshake).</summary>
    NoSalio,

    /// <summary>La petición pudo llegar y no se sabe qué pasó (timeout, corte tras enviar, 5xx, 2xx ilegible).</summary>
    Incierto,

    /// <summary>El API respondió un estado definitivo (2xx con cuerpo leído, o 4xx con su <c>error.code</c>).</summary>
    Respuesta,
}

/// <summary>
/// Resultado de una llamada del <see cref="ClienteApiLiquidacion"/>: la <see cref="Clase"/>, el estado HTTP y el
/// <c>error.code</c> si los hubo, el valor leído si fue 2xx y un <see cref="Motivo"/> técnico para el log (sin secretos ni PII,
/// nunca el cuerpo). <see cref="ToString"/> no imprime <see cref="Valor"/>, que puede traer credenciales.
/// </summary>
/// <example>
/// <code>
/// var r = await api.ObtenerFuentesAsync(apiKey, ct);
/// if (r.Exitoso) { var fuentes = r.Valor!; }
/// else if (r.Clase == ClaseEnvio.Respuesta &amp;&amp; r.Status == 401) { /* credencial rechazada */ }
/// </code>
/// </example>
public sealed record ResultadoEnvio<T>
{
    private ResultadoEnvio(ClaseEnvio clase, int? status, string? codigoError, string? motivo, T? valor)
    {
        Clase = clase;
        Status = status;
        CodigoError = codigoError;
        Motivo = motivo;
        Valor = valor;
    }

    public ClaseEnvio Clase { get; }

    public int? Status { get; }

    /// <summary><c>error.code</c> del cuerpo de error del API (<c>tax_id_taken</c>, <c>unauthorized</c>…); null si no vino.</summary>
    public string? CodigoError { get; }

    public string? Motivo { get; }

    public T? Valor { get; }

    /// <summary>2xx con el cuerpo leído.</summary>
    public bool Exitoso => Clase == ClaseEnvio.Respuesta && Status is >= 200 and < 300 && Valor is not null;

    public static ResultadoEnvio<T> NoSalio(string motivo) => new(ClaseEnvio.NoSalio, null, null, motivo, default);

    public static ResultadoEnvio<T> Incierto(string motivo, int? status = null, string? codigoError = null) =>
        new(ClaseEnvio.Incierto, status, codigoError, motivo, default);

    public static ResultadoEnvio<T> Respuesta(int status, string? codigoError) => new(ClaseEnvio.Respuesta, status, codigoError, null, default);

    public static ResultadoEnvio<T> Exito(int status, T valor) => new(ClaseEnvio.Respuesta, status, null, null, valor);

    /// <summary>La misma clasificación con otro tipo de valor (para un resultado sin valor).</summary>
    public ResultadoEnvio<TOtro> Como<TOtro>() => new(Clase, Status, CodigoError, Motivo, default);

    public override string ToString() =>
        $"ResultadoEnvio {{ Clase = {Clase}, Status = {Status}, CodigoError = {CodigoError}, Motivo = {Motivo}, Valor = {(Valor is null ? "null" : "[REDACTADO]")} }}";
}
