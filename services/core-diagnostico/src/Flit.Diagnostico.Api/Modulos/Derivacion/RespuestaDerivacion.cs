using System.Globalization;
using System.Text.Json.Serialization;
using Flit.Diagnostico.Domain.Derivacion;

namespace Flit.Diagnostico.Api.Modulos.Derivacion;

/// <summary>Respuesta JSON del individual con su status y, si aplica, <c>Retry-After</c>.</summary>
public sealed class DerivacionHttpResult(int statusCode, object value, int? retryAfterSegundos) : IResult, IStatusCodeHttpResult, IValueHttpResult
{
    public int StatusCode { get; } = statusCode;

    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    public object Value { get; } = value;

    object? IValueHttpResult.Value => Value;

    public int? RetryAfterSegundos { get; } = retryAfterSegundos;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.StatusCode = StatusCode;
        if (RetryAfterSegundos is { } segundos)
            httpContext.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
        return httpContext.Response.WriteAsJsonAsync(Value, Value.GetType(), httpContext.RequestAborted);
    }
}

/// <summary>
/// Cuerpos del individual <c>POST …/vehiculos/{vid}/tramite</c> (Punto 4, diseño §5.1):
/// <list type="bullet">
/// <item>201 <c>{ tramite_id, numero, url, ya_existia }</c> (<c>ya_existia=true</c> también es éxito).</item>
/// <item>409 <c>{ code: YA_TIENE_TRAMITE, error, tramite_id, numero }</c>.</item>
/// <item>422 <c>{ code: NO_ELEGIBLE, error, motivo_codigo, motivo }</c>: NO_TERMINADO, NO_VIABLE, EN_CURSO o un rechazo de Trámites.</item>
/// <item>503 <c>{ code: TRAMITES_NO_DISPONIBLE, error }</c>, con <c>Retry-After: 30</c> solo si es transitorio.</item>
/// </list>
/// </summary>
public static class RespuestaDerivacion
{
    public const string CodigoNoElegible = "NO_ELEGIBLE";

    public const int RetryAfterSegundos = 30;

    public static DerivacionHttpResult Desde(ResultadoBorrador resultado, string url)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        return resultado switch
        {
            ResultadoBorrador.Creado c => new(StatusCodes.Status201Created, new CreadoDto(c.TramiteId, c.Numero, url, c.YaExistia), null),
            ResultadoBorrador.Rechazado r => NoElegible(r.Codigo, r.Motivo),
            ResultadoBorrador.NoDisponible n => new(
                StatusCodes.Status503ServiceUnavailable,
                new ErrorDto(MotivoDerivacion.TramitesNoDisponible, MotivoDerivacion.Mensaje(MotivoDerivacion.TramitesNoDisponible)),
                n.Transitorio ? RetryAfterSegundos : null),
            _ => throw new ArgumentOutOfRangeException(nameof(resultado), resultado.GetType().Name, "Resultado de derivación desconocido."),
        };
    }

    /// <summary>503 TRAMITES_NO_DISPONIBLE con <c>Retry-After</c> (freno abierto, incremento 3).</summary>
    public static DerivacionHttpResult NoDisponible(int retryAfterSegundos) =>
        new(StatusCodes.Status503ServiceUnavailable,
            new ErrorDto(MotivoDerivacion.TramitesNoDisponible, MotivoDerivacion.Mensaje(MotivoDerivacion.TramitesNoDisponible)),
            Math.Max(1, retryAfterSegundos));

    public static DerivacionHttpResult YaTieneTramite(Guid tramite, string numero) =>
        new(StatusCodes.Status409Conflict, new ConflictoDto(MotivoDerivacion.YaTieneTramite, MotivoDerivacion.Mensaje(MotivoDerivacion.YaTieneTramite), tramite, numero), null);

    public static DerivacionHttpResult NoElegible(string motivoCodigo, string? motivo = null)
    {
        var texto = string.IsNullOrWhiteSpace(motivo) ? MotivoDerivacion.Mensaje(motivoCodigo) : motivo;
        return new(StatusCodes.Status422UnprocessableEntity, new NoElegibleDto(CodigoNoElegible, texto, motivoCodigo, texto), null);
    }
}

internal sealed record CreadoDto(
    [property: JsonPropertyName("tramite_id")] Guid TramiteId,
    [property: JsonPropertyName("numero")] string Numero,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("ya_existia")] bool YaExistia);

internal sealed record ConflictoDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("tramite_id")] Guid TramiteId,
    [property: JsonPropertyName("numero")] string Numero);

internal sealed record NoElegibleDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("motivo_codigo")] string MotivoCodigo,
    [property: JsonPropertyName("motivo")] string Motivo);

internal sealed record ErrorDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("error")] string Error);
