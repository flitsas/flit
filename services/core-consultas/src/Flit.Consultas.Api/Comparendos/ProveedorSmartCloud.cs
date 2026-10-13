using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Flit.Consultas.Grpc.V1;
using Microsoft.Extensions.Configuration;

namespace Flit.Consultas.Api.Comparendos;

/// <summary>
/// Credenciales y direcciones de SmartCloud. Mismas variables de entorno que el BackCrudSmartCloud de V1, renombradas
/// sin el prefijo de V1: <c>SMARTCLOUD_ACCESS_KEY</c>, <c>SMARTCLOUD_URL_IDENTIFICACION</c>, <c>SMARTCLOUD_URL_PLACA</c>
/// y <c>SMARTCLOUD_TIMEOUT_SECONDS</c>.
/// </summary>
internal sealed record SmartCloudOpciones(string ClaveAcceso, Uri UrlIdentificacion, Uri UrlPlaca, int TimeoutSegundos)
{
    public static SmartCloudOpciones Desde(IConfiguration c) => new(
        c["SMARTCLOUD_ACCESS_KEY"]?.Trim() ?? string.Empty,
        new Uri(c["SMARTCLOUD_URL_IDENTIFICACION"] is { Length: > 0 } i ? i : "https://www3.simit.org.co/ServiciosSimit/EstadoCuentaIdentificacion"),
        new Uri(c["SMARTCLOUD_URL_PLACA"] is { Length: > 0 } p ? p : "https://www3.simit.org.co/ServiciosSimit/EstadoCuentaPlaca"),
        int.TryParse(c["SMARTCLOUD_TIMEOUT_SECONDS"], CultureInfo.InvariantCulture, out var t) && t > 0 ? t : 60);
}

/// <summary>
/// SmartCloud: los servicios SOAP del SIMIT (<c>ServiciosSimit/EstadoCuentaIdentificacion</c> y
/// <c>EstadoCuentaPlaca</c>, HU #13465 y #13466). Contrato tomado del BackCrudSmartCloud de V1
/// (<c>smartCloudIdentificationService.ts</c>) y del XSD público del SIMIT: la clave de acceso va pegada antes del
/// documento (<c>Documento = clave + número</c>), <c>TipoDocumento</c> es numérico y <c>Usuario</c> va vacío.
/// <para>
/// Visto en el SIMIT real (2026-10-13): la placa también lleva la clave delante; «sin comparendos» es
/// <c>error_Cod</c> 0 «SIN ERRORES» sin filas; sin la clave, o con una clave que no sirve, responde <c>error_Cod</c> 1
/// «Usuario no permitido» (es autenticación). Cualquier otro código sin filas es error y no vacío: nunca se borra con
/// una respuesta dudosa. Su firewall rechaza con 403 algunos User-Agent (el de Python); el HttpClient de .NET pasa.
/// </para>
/// </summary>
internal sealed class ProveedorSmartCloud(IHttpClientFactory http, SmartCloudOpciones opciones) : IProveedorComparendos
{
    public const string HttpClientName = "comparendos-smartcloud";
    private static readonly XNamespace Servicios = "http://Servicios/";

    /// <summary>Tipos de documento del SIMIT (V1, smartCloudIdentificationService.ts).</summary>
    private static readonly Dictionary<string, int> Tipos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CC"] = 1, ["TI"] = 2, ["CE"] = 3, ["NIT"] = 4, ["PA"] = 6, ["CD"] = 7, ["RC"] = 8, ["CV"] = 9,
    };

    public string Clave => "smartcloud";

    public string Nombre => "SmartCloud (SIMIT)";

    public IReadOnlyList<CanalComparendos> Canales => Fuente.Simit;

    public string? NoConectado => opciones.ClaveAcceso.Length == 0 ? "Falta la clave de acceso de SmartCloud (SMARTCLOUD_ACCESS_KEY)." : null;

    public async Task<ConsultarComparendosResponse> ConsultarAsync(ConsultarComparendosRequest pedido, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var reloj = Stopwatch.StartNew();
        var porPlaca = Fuente.PorPlaca(pedido.Canal);
        int tipo = 0;
        if (!porPlaca && !Tipos.TryGetValue(pedido.TipoDocumento.Trim(), out tipo))
            return Fuente.Error(Clave, TipoErrorFuente.Excepcion, $"SmartCloud no consulta el tipo de documento «{pedido.TipoDocumento}».", null, 0);

        var operacion = porPlaca ? "WsEstadoCuentaPlaca" : "WsEstadoCuentaIdentificacion";
        var cuerpoPedido = porPlaca
            ? new XElement(Servicios + operacion,
                new XElement("Placa", opciones.ClaveAcceso + pedido.Placa.Trim().ToUpperInvariant()),
                new XElement("Usuario", string.Empty))
            : new XElement(Servicios + operacion,
                new XElement("Documento", opciones.ClaveAcceso + pedido.NumeroDocumento.Trim()),
                new XElement("TipoDocumento", tipo),
                new XElement("Usuario", string.Empty));
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        var sobre = new XDocument(new XElement(soap + "Envelope", new XAttribute(XNamespace.Xmlns + "soap", soap.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "tns", Servicios.NamespaceName), new XElement(soap + "Body", cuerpoPedido)));

        try
        {
            using var mensaje = new HttpRequestMessage(HttpMethod.Post, porPlaca ? opciones.UrlPlaca : opciones.UrlIdentificacion)
            {
                Content = new StringContent(sobre.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml"),
            };
            mensaje.Headers.Add("SOAPAction", "\"\"");
            using var respuesta = await http.CreateClient(HttpClientName).SendAsync(mensaje, ct).ConfigureAwait(false);
            var cuerpo = SinClave(await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var codigo = (int)respuesta.StatusCode;

            XDocument xml;
            try
            {
                xml = XDocument.Parse(cuerpo);
            }
            catch (XmlException)
            {
                return respuesta.IsSuccessStatusCode
                    ? Fuente.Error(Clave, TipoErrorFuente.RespuestaInvalida, "La respuesta de SmartCloud no es XML válido.", codigo, reloj.ElapsedMilliseconds, cuerpo)
                    : Fuente.Error(Clave, TipoErrorFuente.HttpNoExitoso, $"SmartCloud respondió {codigo}.", codigo, reloj.ElapsedMilliseconds, cuerpo);
            }

            if (xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault") is { } falla)
            {
                var texto = Hijo(falla, "faultstring") ?? "sin detalle";
                return Fuente.Error(Clave, TipoErrorFuente.SoapFault, $"SmartCloud devolvió un SOAP Fault: {texto}", codigo, reloj.ElapsedMilliseconds, cuerpo);
            }

            if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Fuente.Error(Clave, TipoErrorFuente.Autenticacion, $"SmartCloud rechazó la autenticación ({codigo}).", codigo, reloj.ElapsedMilliseconds, cuerpo);
            if (!respuesta.IsSuccessStatusCode)
                return Fuente.Error(Clave, TipoErrorFuente.HttpNoExitoso, $"SmartCloud respondió {codigo}.", codigo, reloj.ElapsedMilliseconds, cuerpo);

            var salida = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "return");
            if (salida is null)
                return Fuente.Error(Clave, TipoErrorFuente.RespuestaInvalida, "La respuesta de SmartCloud no trae el elemento «return».", codigo, reloj.ElapsedMilliseconds, cuerpo);

            var filas = salida.Elements().Where(e => e.Name.LocalName is "estadoCuentaIdentificacion" or "estadoCuentaPlaca")
                .Where(e => e.HasElements).ToList();
            var errorCod = Hijo(salida, "error_Cod");
            if (filas.Count == 0 && errorCod is not null && errorCod.Trim('0').Length > 0)
            {
                var desc = Hijo(salida, "error_Desc") ?? "sin descripción";
                return errorCod.Trim() == "1"
                    ? Fuente.Error(Clave, TipoErrorFuente.Autenticacion, $"SmartCloud no reconoce la clave de acceso: {desc}", codigo, reloj.ElapsedMilliseconds, cuerpo)
                    : Fuente.Error(Clave, TipoErrorFuente.EstadoNoOk, $"SmartCloud respondió el código {errorCod}: {desc}", codigo, reloj.ElapsedMilliseconds, cuerpo);
            }

            var parcial = new ResultadoParcialFuente();
            parcial.Comparendos.AddRange(filas.Select(Mapear).OfType<ComparendoDeFuente>());
            if (parcial.Comparendos.Count == 0 && filas.Count > 0)
                return Fuente.Error(Clave, TipoErrorFuente.RespuestaInvalida, "SmartCloud devolvió filas sin número de comparendo.", codigo, reloj.ElapsedMilliseconds, cuerpo);
            parcial.Resultado = parcial.Comparendos.Count == 0 ? ResultadoFuente.ExitosaVacia : ResultadoFuente.ExitosaConDatos;
            return new ConsultarComparendosResponse
            {
                Proveedor = Clave, HttpStatus = codigo, DuracionMs = (int)reloj.ElapsedMilliseconds, RespuestaCruda = cuerpo, ContentType = "text/xml",
                Parciales = { parcial },
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            return Fuente.Error(Clave, TipoErrorFuente.Timeout, "SmartCloud no respondió a tiempo.", null, reloj.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            return Fuente.Error(Clave, TipoErrorFuente.Red, $"Error de red con SmartCloud: {SinClave(ex.Message)}", null, reloj.ElapsedMilliseconds);
        }
    }

    /// <summary>La clave de acceso nunca sale en la evidencia ni en los mensajes (el SIMIT podría devolver el documento tal cual se envió).</summary>
    private string SinClave(string texto) => opciones.ClaveAcceso.Length == 0 ? texto : texto.Replace(opciones.ClaveAcceso, "***", StringComparison.Ordinal);

    private static string? Hijo(XElement padre, string nombre) =>
        padre.Elements().FirstOrDefault(e => e.Name.LocalName == nombre)?.Value is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    private static ComparendoDeFuente? Mapear(XElement f)
    {
        if (Hijo(f, "comparendo") is not { } numero)
            return null;
        var c = new ComparendoDeFuente { Numero = numero };
        if (Hijo(f, "placa") is { } placa) c.Placa = placa.ToUpperInvariant();
        if (Hijo(f, "tipoDocumento") is { } td) c.TipoDocumentoInfractor = TipoDocumento(td);
        if (Hijo(f, "documentoIdentidad") is { } doc) c.NumeroDocumentoInfractor = doc;
        if (Hijo(f, "nombre") is { } nombre) c.NombreInfractor = nombre;
        if (Hijo(f, "infraccion") is { } inf) c.CodigoInfraccion = inf;
        if (Hijo(f, "descInfraccion") is { } desc) c.DescripcionInfraccion = desc;
        if (Hijo(f, "direccionComparendo") is { } dir) c.DireccionInfraccion = dir;
        if (Fuente.Fecha(Hijo(f, "fechaComparendo")) is { } fecha) c.FechaImposicion = fecha;
        if (Fuente.Hora(Hijo(f, "fechaComparendo")) is { } hora) c.HoraImposicion = hora;
        if (Fuente.Fecha(Hijo(f, "fechaNotificacion")) is { } notif) c.FechaNotificacion = notif;
        if (Fuente.SiNo(Hijo(f, "fotodeteccion")) is { } foto) c.EsFotomulta = foto;
        if (Hijo(f, "secretaria") is { } sec) c.Secretaria = sec.ToUpperInvariant();
        if (Hijo(f, "tipoVehiculo") is { } tv) c.TipoVehiculo = tv;
        if (Hijo(f, "servicioVehiculo") is { } sv) c.ServicioVehiculo = sv;
        if (Fuente.PesosDeTexto(Hijo(f, "valor")) is { } valor) c.ValorTotal = valor;
        if (Hijo(f, "estado") is { } estado) c.EstadoFuente = estado;
        return c;
    }

    /// <summary>El SIMIT puede devolver el tipo como código numérico: se traduce a la sigla.</summary>
    private static string TipoDocumento(string t) =>
        int.TryParse(t, CultureInfo.InvariantCulture, out var n) && Tipos.FirstOrDefault(x => x.Value == n).Key is { } sigla ? sigla : t.ToUpperInvariant();
}
