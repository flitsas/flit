using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Flit.PersonaNatural.Api.Cotizaciones;

/// <summary>Resultado del envío de una cotización por correo.</summary>
public enum ResultadoEnvioCotizacion
{
    Enviado,

    /// <summary>La misma <c>Idempotency-Key</c> ya envió esta cotización: no se reenvía.</summary>
    Repetido,

    /// <summary>El correo superó el límite de envíos de la ventana.</summary>
    LimiteAlcanzado,

    /// <summary>El transporte no aceptó el correo.</summary>
    Fallido,
}

/// <summary>
/// Catálogo y cotización del portal (HU #14043). La cotización NUNCA se calcula aquí ni se cachea: sale de la fachada de
/// core-api (único cálculo, también para el paso de pago). Desde la HU #14391 el catálogo de organismos y tipos tampoco se
/// cachea: lo que se ofrece depende de los precios y un precio recién registrado o cerrado se ve en la siguiente consulta.
/// <para>
/// Habeas Data: el correo solo existe en memoria durante la petición. El límite y la idempotencia usan una huella HMAC
/// del correo y de la clave, en memoria de la réplica, con vencimiento; el correo nunca se escribe en base ni en logs.
/// </para>
/// </summary>
public sealed class CotizacionesService(
    IFachadaTramites fachada,
    IEnviadorCotizacion enviador,
    IOptions<CotizacionesOptions> options,
    IConfiguration configuration,
    TimeProvider reloj)
{
    private readonly CotizacionesOptions _o = options.Value;
    private readonly byte[] _clave = Encoding.UTF8.GetBytes(configuration["Pii:ClaveHmac"] ?? string.Empty);
    private readonly Lock _candado = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _envios = [];
    private readonly Dictionary<string, DateTimeOffset> _idempotencia = [];

    /// <summary>HU #14391 — sin caché: un organismo aparece o desaparece apenas cambian sus precios.</summary>
    public Task<IReadOnlyList<OrganismoDto>> OrganismosAsync(CancellationToken ct) => fachada.ListarOrganismosAsync(ct);

    /// <summary>HU #14391 — sin caché: un tipo con precio nuevo (o sin precio) se refleja en la siguiente consulta.</summary>
    public Task<IReadOnlyList<TipoTramiteDto>> TiposAsync(string? organismoId, CancellationToken ct) => fachada.ListarTiposAsync(organismoId, ct);

    public Task<CotizacionDto> CotizarAsync(string tipoCodigo, string organismoId, IReadOnlyCollection<string> adicionales, CancellationToken ct) =>
        fachada.CotizarAsync(tipoCodigo, organismoId, adicionales, ct);

    public async Task<ResultadoEnvioCotizacion> EnviarPorCorreoAsync(
        string correo, string idempotencyKey, string tipoCodigo, string organismoId, IReadOnlyCollection<string> adicionales, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow();
        var huellaCorreo = Huella("correo:" + correo.Trim().ToLowerInvariant());
        var huellaClave = Huella("clave:" + idempotencyKey);

        lock (_candado)
        {
            Purgar(ahora);
            if (_idempotencia.ContainsKey(huellaClave))
                return ResultadoEnvioCotizacion.Repetido;

            var ventana = TimeSpan.FromMinutes(_o.VentanaCorreoMinutos);
            if (!_envios.TryGetValue(huellaCorreo, out var cola))
                _envios[huellaCorreo] = cola = new Queue<DateTimeOffset>();
            while (cola.Count > 0 && ahora - cola.Peek() >= ventana)
                cola.Dequeue();
            if (cola.Count >= _o.LimiteCorreoPorVentana)
                return ResultadoEnvioCotizacion.LimiteAlcanzado;
            cola.Enqueue(ahora);
            _idempotencia[huellaClave] = ahora.AddMinutes(_o.IdempotenciaMinutos);
        }

        try
        {
            // El valor sale de la fachada en este momento: lo que el ciudadano recibe es lo que hoy se cobraría.
            var cotizacion = await fachada.CotizarAsync(tipoCodigo, organismoId, adicionales, ct).ConfigureAwait(false);
            var enviado = await enviador.EnviarAsync(correo, "Tu cotización de trámite FLIT", ArmarHtml(cotizacion), ct).ConfigureAwait(false);
            if (enviado)
                return ResultadoEnvioCotizacion.Enviado;
        }
        catch
        {
            LiberarClave(huellaClave);
            throw;
        }

        LiberarClave(huellaClave);
        return ResultadoEnvioCotizacion.Fallido;
    }

    /// <summary>Cuerpo del correo: solo la cotización, sin ningún dato personal. Todo texto se codifica.</summary>
    internal static string ArmarHtml(CotizacionDto c)
    {
        var filas = new StringBuilder();
        foreach (var l in c.Lineas)
        {
            var subtitulo = string.IsNullOrWhiteSpace(l.Subtitulo) ? string.Empty : $"<br><small style=\"color:#555\">{WebUtility.HtmlEncode(l.Subtitulo)}</small>";
            filas.Append($"<tr><td style=\"padding:6px 0\">{WebUtility.HtmlEncode(l.Nombre)}{subtitulo}</td><td style=\"padding:6px 0;text-align:right\">{Pesos(l.ValorCop)}</td></tr>");
        }

        return "<div style=\"font-family:Arial,sans-serif;max-width:560px\"><h2>Tu cotización</h2>"
            + $"<table style=\"width:100%;border-collapse:collapse\">{filas}"
            + $"<tr><td style=\"padding:8px 0;border-top:1px solid #ccc\"><b>Total</b></td><td style=\"padding:8px 0;border-top:1px solid #ccc;text-align:right\"><b>{Pesos(c.TotalCop)}</b></td></tr></table>"
            + "<p style=\"color:#555\">Valores vigentes al momento de enviar este correo; pueden cambiar. No guardamos tu correo.</p></div>";
    }

    /// <summary>Pesos con punto de miles. A mano: la imagen alpine corre sin ICU (invariant globalization) y no tiene la cultura es-CO.</summary>
    internal static string Pesos(long valor) =>
        "$ " + valor.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.') + " COP";

    /// <summary>Correo bien formado y de longitud razonable (la validación definitiva es la entrega).</summary>
    public static bool CorreoValido(string? correo)
    {
        if (string.IsNullOrWhiteSpace(correo) || correo.Length > 254)
            return false;
        var limpio = correo.Trim();
        return MailAddress.TryCreate(limpio, out var m) && m.Address == limpio && m.Host.Contains('.', StringComparison.Ordinal);
    }

    private void LiberarClave(string huellaClave)
    {
        lock (_candado)
            _idempotencia.Remove(huellaClave);
    }

    private void Purgar(DateTimeOffset ahora)
    {
        foreach (var vencida in _idempotencia.Where(p => p.Value <= ahora).Select(p => p.Key).ToList())
            _idempotencia.Remove(vencida);
        var ventana = TimeSpan.FromMinutes(_o.VentanaCorreoMinutos);
        foreach (var vacio in _envios.Where(p => p.Value.Count == 0 || ahora - p.Value.Peek() >= ventana && ahora - p.Value.Last() >= ventana).Select(p => p.Key).ToList())
            _envios.Remove(vacio);
    }

    private string Huella(string texto) => Convert.ToHexString(HMACSHA256.HashData(_clave, Encoding.UTF8.GetBytes(texto)));
}
