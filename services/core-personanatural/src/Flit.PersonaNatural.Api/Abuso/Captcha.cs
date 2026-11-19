using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Abuso;

/// <summary>Valida el token que el navegador obtuvo al resolver el captcha.</summary>
public interface IProveedorCaptcha
{
    Task<bool> VerificarAsync(string token, string? ip, CancellationToken ct);
}

/// <summary>Turnstile y reCAPTCHA hablan el mismo protocolo (<c>secret</c>, <c>response</c>, <c>remoteip</c> → <c>success</c>); solo cambia la URL.</summary>
public sealed partial class ProveedorCaptchaHttp(HttpClient http, IOptions<CaptchaOptions> options, ILogger<ProveedorCaptchaHttp> logger) : IProveedorCaptcha
{
    private readonly CaptchaOptions _opciones = options.Value;

    public async Task<bool> VerificarAsync(string token, string? ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var url = string.IsNullOrWhiteSpace(_opciones.UrlVerificacion) ? CaptchaOptions.UrlPorDefecto(_opciones.Proveedor) : _opciones.UrlVerificacion;
        var campos = new Dictionary<string, string> { ["secret"] = _opciones.SecretKey ?? string.Empty, ["response"] = token };
        if (!string.IsNullOrWhiteSpace(ip))
            campos["remoteip"] = ip;
        try
        {
            using var respuesta = await http.PostAsync(url, new FormUrlEncodedContent(campos), ct).ConfigureAwait(false);
            if (!respuesta.IsSuccessStatusCode)
                return false;
            var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaCaptcha>(ct).ConfigureAwait(false);
            return cuerpo?.Success == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Sin respuesta del proveedor se niega: abrir el portal cuando el captcha cae es justo lo que buscaría un abusador.
            SinRespuesta(logger, ex, _opciones.Proveedor);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo verificar el captcha con el proveedor {Proveedor}")]
    private static partial void SinRespuesta(ILogger logger, Exception ex, string proveedor);

    private sealed record RespuestaCaptcha([property: JsonPropertyName("success")] bool Success);
}

/// <summary>Solo para el equipo del desarrollador: acepta cualquier token no vacío. <see cref="CaptchaOptions.Validar"/> exige Development y Captcha__PermitirDeshabilitado=true.</summary>
public sealed class ProveedorCaptchaDeshabilitado : IProveedorCaptcha
{
    public Task<bool> VerificarAsync(string token, string? ip, CancellationToken ct) => Task.FromResult(!string.IsNullOrWhiteSpace(token));
}
