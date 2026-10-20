using System.Net;
using System.Net.Sockets;
using Flit.Comparendos.Api.Modulos.Correo;
using Flit.Comparendos.Api.Persistence;
using Flit.Comparendos.Api.Persistence.Modelo;
using Microsoft.EntityFrameworkCore;

namespace Flit.Comparendos.Api.Modulos.Avisos;

/// <summary>Trae la foto de una fotomulta desde la URL que reporta la fuente.</summary>
public interface IDescargaFoto
{
    /// <summary>La foto como adjunto, o null si no se pudo (el aviso sale igual, sin ella).</summary>
    Task<AdjuntoCorreo?> DescargarAsync(string url, CancellationToken ct);
}

/// <summary>
/// La foto de la fotomulta va adjunta al aviso al conductor (D7, 2026-10-13: lo pide la Epic). Si el comparendo tiene
/// archivo de evidencia (OCR) se adjunta ese; si no, la imagen de la URL de la fuente. Nada de esto detiene el aviso.
/// </summary>
public sealed partial class FotoFotomulta(ComparendosDb db, IDescargaFoto descarga, ILogger<FotoFotomulta> logger)
{
    public async Task<AdjuntoCorreo?> ParaAvisoAsync(Comparendo c, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (c.EsFotomulta != true)
            return null;
        if (c.ArchivoEvidenciaId is { } id && await db.Archivos.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false) is { } archivo)
            return new AdjuntoCorreo(archivo.NombreOriginal, archivo.ContentType, [], archivo.Id);
        if (string.IsNullOrWhiteSpace(c.FotoEvidenciaUrl))
            return null;

        var foto = await descarga.DescargarAsync(c.FotoEvidenciaUrl, ct).ConfigureAwait(false);
        if (foto is null)
            LogSinFoto(logger, c.Numero);
        return foto is null ? null : foto with { Nombre = $"fotomulta-{c.Numero}{Path.GetExtension(foto.Nombre)}" };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo traer la foto de la fotomulta {Numero}; el aviso sale sin ella.")]
    private static partial void LogSinFoto(ILogger logger, string numero);
}

/// <summary>
/// Descarga por HTTPS, con tope de tamaño y solo imágenes o PDF. La URL viene de un tercero: no se siguen redirecciones
/// y no se conecta a direcciones internas (loopback, redes privadas, link-local), para que no sirva de puente a la red
/// del servidor.
/// </summary>
public sealed partial class DescargaFotoHttp(IHttpClientFactory fabrica, ILogger<DescargaFotoHttp> logger) : IDescargaFoto
{
    public const string HttpClientName = "comparendos-foto-evidencia";
    private const int TopeBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> Tipos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp", ["application/pdf"] = ".pdf",
    };

    public async Task<AdjuntoCorreo?> DescargarAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;
        try
        {
            using var r = await fabrica.CreateClient(HttpClientName).GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var tipo = r.Content.Headers.ContentType?.MediaType;
            if (!r.IsSuccessStatusCode || tipo is null || !Tipos.TryGetValue(tipo, out var extension) || r.Content.Headers.ContentLength > TopeBytes)
                return null;

            await using var flujo = await r.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var memoria = new MemoryStream();
            var buffer = new byte[81920];
            int leidos;
            while ((leidos = await flujo.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                if (memoria.Length + leidos > TopeBytes)
                    return null;
                await memoria.WriteAsync(buffer.AsMemory(0, leidos), ct).ConfigureAwait(false);
            }

            return new AdjuntoCorreo($"fotomulta{extension}", tipo, memoria.ToArray());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            LogFallo(logger, uri.Host, ex.Message);
            return null;
        }
    }

    /// <summary>Manejador que solo conecta a direcciones públicas.</summary>
    public static SocketsHttpHandler Manejador() => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        ConnectCallback = async (contexto, ct) =>
        {
            var direcciones = await Dns.GetHostAddressesAsync(contexto.DnsEndPoint.Host, ct).ConfigureAwait(false);
            var ip = direcciones.FirstOrDefault(EsPublica) ?? throw new HttpRequestException("La foto apunta a una dirección interna.");
            var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(ip, contexto.DnsEndPoint.Port), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    public static bool EsPublica(IPAddress ip)
    {
        ArgumentNullException.ThrowIfNull(ip);
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC);
        var b = ip.GetAddressBytes();
        return !(b[0] == 0 || b[0] == 10 || b[0] >= 224
            || (b[0] == 100 && b[1] is >= 64 and <= 127)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 172 && b[1] is >= 16 and <= 31)
            || (b[0] == 192 && b[1] == 168));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No se pudo descargar la foto de evidencia de {Host}: {Motivo}")]
    private static partial void LogFallo(ILogger logger, string host, string motivo);
}
