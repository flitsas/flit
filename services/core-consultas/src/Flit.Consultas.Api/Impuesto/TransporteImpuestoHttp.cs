using System.Net;
using System.Text;
using Flit.Tramites.Application.UseCases.Consultations;

namespace Flit.Consultas.Api.Impuesto;

/// <summary>Lo que devolvió el transporte. <see cref="Fallo"/> lleno significa que no hay cuerpo que interpretar.</summary>
internal sealed record ResultadoTransporteImpuesto(string? Cuerpo, FalloProveedor? Fallo);

internal interface ITransporteImpuesto
{
    bool Listo { get; }

    string NombreProveedor { get; }

    Task<ResultadoTransporteImpuesto> EnviarAsync(string cuerpo, CancellationToken ct);
}

/// <summary>
/// POST real. No escribe en el log la URL, la placa, el documento, el nombre ni <c>ex.Message</c>.
/// </summary>
internal sealed class TransporteImpuestoHttp(IHttpClientFactory http, OpcionesImpuesto opciones, ILogger<TransporteImpuestoHttp> logger) : ITransporteImpuesto
{
    public const string HttpClientName = "impuesto";

    public bool Listo => opciones.TieneRuta;

    public string NombreProveedor => ProveedorImpuesto.ClaveProveedor;

    public async Task<ResultadoTransporteImpuesto> EnviarAsync(string cuerpo, CancellationToken ct)
    {
        if (!Listo)
            throw new InvalidOperationException("El transporte de impuesto no está configurado.");
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post, OpcionesImpuesto.Unir(opciones.BaseUrl!.AbsoluteUri, opciones.Ruta))
            {
                Content = new StringContent(cuerpo, Encoding.UTF8, "application/json"),
            };
            using var respuesta = await http.CreateClient(HttpClientName).SendAsync(pedido, ct).ConfigureAwait(false);
            var codigo = (int)respuesta.StatusCode;
            if (!respuesta.IsSuccessStatusCode)
                return new ResultadoTransporteImpuesto(null, Fallo(respuesta.StatusCode, codigo));
            return new ResultadoTransporteImpuesto(await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            TransporteImpuestoLog.Tiempo(logger);
            return new ResultadoTransporteImpuesto(null, new FalloProveedor(ClaseFalloProveedor.Timeout));
        }
        catch (HttpRequestException)
        {
            TransporteImpuestoLog.Red(logger);
            return new ResultadoTransporteImpuesto(null, new FalloProveedor(ClaseFalloProveedor.Red));
        }
    }

    private static FalloProveedor Fallo(HttpStatusCode estado, int codigo) => estado switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new FalloProveedor(ClaseFalloProveedor.Credenciales, codigo),
        HttpStatusCode.TooManyRequests => new FalloProveedor(ClaseFalloProveedor.Limite, codigo),
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => new FalloProveedor(ClaseFalloProveedor.Rechazo, codigo),
        _ => new FalloProveedor(ClaseFalloProveedor.Caida, codigo),
    };
}

internal static partial class TransporteImpuestoLog
{
    [LoggerMessage(EventId = 7431, Level = LogLevel.Warning, Message = "Transporte de impuesto agotó el tiempo.")]
    public static partial void Tiempo(ILogger logger);

    [LoggerMessage(EventId = 7432, Level = LogLevel.Warning, Message = "Transporte de impuesto no alcanzó la red.")]
    public static partial void Red(ILogger logger);
}
