using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Consultas.Api.RuntIntempo;
using Flit.Consultas.Grpc.V1;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// HU #13875 (F10-H3) — cliente HTTP nativo de Intempo (§2 y §3 de la especificación de migración). Intempo es falso
/// (un <see cref="HttpMessageHandler"/> que no sale a la red) y la configuración va en memoria, como en
/// <see cref="ContratosRentingTests"/>. Fixtures inventados (placas TST###, VIN 9ZZPRUEBA…, documentos 99999…).
/// <para>Uso de ejemplo:</para>
/// <code>
/// var p = new ProveedorRuntIntempo(fabrica, IntempoOpciones.Desde(config));
/// if (p.NoConectado is null) { var r = await p.ConsultarPorPlacaAsync("TST301", ct); }
/// </code>
/// </summary>
public sealed class RuntIntempoProveedorTests
{
    private const string Base = "https://intempo.prueba.local/pdn/Base";

    private const string CrudoTst301 = """
        {
          "noPlaca": "TST301", "noVin": "9ZZPRUEBA00000301", "marca": "MARCA PRUEBA", "linea": "LINEA PRUEBA", "modelo": "2024",
          "propietariosActuales": [
            { "id": 1, "idTipoDocumento": "N", "tipoDocumento": "NIT", "noDocumento": "999990301", "nombreCompleto": "EMPRESA PRUEBA UNO SAS",
              "tipoPropiedad": 1, "detallePropiedad": "PROPIO", "direcciones": [], "locatarios": [] }
          ]
        }
        """;

    private static IntempoOpciones Opciones(Dictionary<string, string?> valores) =>
        IntempoOpciones.Desde(new ConfigurationBuilder().AddInMemoryCollection(valores).Build());

    private static (ProveedorRuntIntempo Proveedor, IntempoFalso Intempo) Crear(
        Func<HttpRequestMessage, HttpResponseMessage> responder, string baseUrl = Base, Dictionary<string, string?>? extra = null)
    {
        var valores = new Dictionary<string, string?> { ["INTEMPO_API_BASE_URL"] = baseUrl };
        foreach (var (k, v) in extra ?? [])
            valores[k] = v;
        var intempo = new IntempoFalso(responder);
        return (new ProveedorRuntIntempo(new Fabrica(intempo), Opciones(valores)), intempo);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Responde(HttpStatusCode codigo, string cuerpo = "") =>
        _ => new HttpResponseMessage(codigo) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sin_base_url_queda_no_conectado_y_nombra_solo_la_variable()
    {
        var intempo = new IntempoFalso(Responde(HttpStatusCode.OK, CrudoTst301));
        var valores = new Dictionary<string, string?>
        {
            ["INTEMPO_API_VEHICLE_PLATE_PATH"] = "/ruta-placa-valor-prueba",
            ["INTEMPO_API_VEHICLE_VIN_PATH"] = "/ruta-vin-valor-prueba",
            ["INTEMPO_API_SECONDS_TIMEOUT"] = "37",
        };

        var construir = () => new ProveedorRuntIntempo(new Fabrica(intempo), Opciones(valores));
        var p = construir.Should().NotThrow("sin la URL base el servicio arranca y la fuente queda no conectada").Subject;

        p.NoConectado.Should().NotBeNull().And.Contain("INTEMPO_API_BASE_URL").And.StartWith("Falta configurar Intempo");
        p.NoConectado.Should().NotContainAny("ruta-placa-valor-prueba", "ruta-vin-valor-prueba", "37");
        Opciones(valores).Faltan().Should().Equal("INTEMPO_API_BASE_URL");

        // Una URL que no es absoluta tampoco conecta, y su valor no se repite en el motivo.
        var invalida = new ProveedorRuntIntempo(new Fabrica(intempo), Opciones(new() { ["INTEMPO_API_BASE_URL"] = "no-es-url-valor-prueba" }));
        invalida.NoConectado.Should().Contain("INTEMPO_API_BASE_URL").And.NotContain("no-es-url-valor-prueba");

        // Llamar sin configuración es un error de programación (el servicio gRPC revisa NoConectado antes): no sale a la red.
        var llamar = () => p.ConsultarPorPlacaAsync("TST301", Ct);
        await llamar.Should().ThrowAsync<InvalidOperationException>();
        intempo.Pedidos.Should().BeEmpty("sin configuración no se hace ninguna petición HTTP");

        Crear(Responde(HttpStatusCode.OK)).Proveedor.NoConectado.Should().BeNull("con la URL base basta: los paths y el timeout tienen defecto");
    }

    [Fact]
    public void Paths_y_timeout_toman_el_defecto_de_la_especificacion()
    {
        var o = Opciones(new() { ["INTEMPO_API_BASE_URL"] = Base });
        o.BaseUrl.Should().Be(new Uri(Base));
        (o.RutaPlaca, o.RutaVin, o.TimeoutSegundos).Should().Be(("/api/RuntConsulta/consultar-vehiculo", "/api/RuntConsulta/consultar-vin", 20));
        o.Faltan().Should().BeEmpty();

        foreach (var invalido in new[] { "0", "-3", "abc", " " })
            Opciones(new() { ["INTEMPO_API_SECONDS_TIMEOUT"] = invalido }).TimeoutSegundos.Should().Be(20, $"«{invalido}» no es un timeout positivo");

        var propio = Opciones(new()
        {
            ["INTEMPO_API_BASE_URL"] = Base,
            ["INTEMPO_API_VEHICLE_PLATE_PATH"] = "/otra/placa",
            ["INTEMPO_API_VEHICLE_VIN_PATH"] = "/otra/vin",
            ["INTEMPO_API_SECONDS_TIMEOUT"] = "45",
        });
        (propio.RutaPlaca, propio.RutaVin, propio.TimeoutSegundos).Should().Be(("/otra/placa", "/otra/vin", 45));
        ProveedorRuntIntempo.HttpClientName.Should().Be("runt-intempo");
        ProveedorRuntIntempo.ClaveProveedor.Should().Be("intempo");
    }

    [Fact]
    public async Task Por_placa_hace_POST_con_Plate_en_query_y_body_vacio()
    {
        var (p, intempo) = Crear(Responde(HttpStatusCode.OK, CrudoTst301));

        await p.ConsultarPorPlacaAsync("TST301", Ct);

        var pedido = intempo.Pedidos.Should().ContainSingle().Subject;
        pedido.Metodo.Should().Be(HttpMethod.Post);
        pedido.Url.Should().Be("https://intempo.prueba.local/pdn/Base/api/RuntConsulta/consultar-vehiculo?Plate=TST301");
        pedido.Cuerpo.Should().BeEmpty();
        pedido.TipoContenido.Should().Be("application/json");
        pedido.Autenticacion.Should().BeFalse("Intempo no lleva usuario, API key ni token (§2)");

        // El valor va sin recortar ni cambiar mayúsculas (el proxy no normaliza), solo escapado.
        await p.ConsultarPorPlacaAsync("tst 301", Ct);
        intempo.Pedidos[1].Url.Should().EndWith("/consultar-vehiculo?Plate=tst%20301");
    }

    [Fact]
    public async Task Por_VIN_usa_Vin_con_V_mayuscula()
    {
        var (p, intempo) = Crear(Responde(HttpStatusCode.OK, CrudoTst301));

        await p.ConsultarPorVinAsync("9ZZPRUEBA00000301", Ct);

        var pedido = intempo.Pedidos.Should().ContainSingle().Subject;
        pedido.Metodo.Should().Be(HttpMethod.Post);
        pedido.Url.Should().Be("https://intempo.prueba.local/pdn/Base/api/RuntConsulta/consultar-vin?Vin=9ZZPRUEBA00000301");
        pedido.Url.Should().Contain("?Vin=").And.NotContain("?vin=");
        (pedido.Cuerpo, pedido.TipoContenido).Should().Be(("", "application/json"));
    }

    [Fact]
    public async Task La_base_con_ruta_se_conserva_al_armar_la_URL()
    {
        var (p, intempo) = Crear(Responde(HttpStatusCode.OK, CrudoTst301), baseUrl: "https://intempo.prueba.local/pdn/Base/",
            extra: new() { ["INTEMPO_API_VEHICLE_PLATE_PATH"] = "api/RuntConsulta/consultar-vehiculo" });

        await p.ConsultarPorPlacaAsync("TST301", Ct);
        await p.ConsultarPorVinAsync("9ZZPRUEBA00000301", Ct);

        intempo.Pedidos.Select(x => x.Url).Should().Equal(
            "https://intempo.prueba.local/pdn/Base/api/RuntConsulta/consultar-vehiculo?Plate=TST301",
            "https://intempo.prueba.local/pdn/Base/api/RuntConsulta/consultar-vin?Vin=9ZZPRUEBA00000301");
    }

    [Fact]
    public async Task Respuesta_valida_es_ENCONTRADO_con_el_vehiculo_interpretado()
    {
        var (p, _) = Crear(Responde(HttpStatusCode.OK, CrudoTst301));

        var r = await p.ConsultarPorPlacaAsync("TST301", Ct);

        r.Resultado.Should().Be(ResultadoRuntIntempo.Encontrado);
        (r.Proveedor, r.HasHttpStatus, r.HttpStatus).Should().Be(("intempo", true, 200));
        r.DuracionMs.Should().BeGreaterThanOrEqualTo(0);
        (r.TipoError, r.HasMensajeError).Should().Be((TipoErrorFuente.Unspecified, false));
        r.Errores.Should().BeEmpty();
        (r.Vehiculo.Plate, r.Vehiculo.Vin).Should().Be(("TST301", "9ZZPRUEBA00000301"));
        using var doc = JsonDocument.Parse(CrudoTst301);
        r.Vehiculo.Should().Be(InterpreteIntempo.Interpretar(doc.RootElement), "el vehículo es el del intérprete de F10-H2");

        // Un objeto con solo el VIN también es un vehículo.
        var (porVin, _) = Crear(Responde(HttpStatusCode.OK, """{"noVin":"9ZZPRUEBA00000302"}"""));
        (await porVin.ConsultarPorVinAsync("9ZZPRUEBA00000302", Ct)).Resultado.Should().Be(ResultadoRuntIntempo.Encontrado);
    }

    [Fact]
    public async Task Http404_es_NO_ENCONTRADO()
    {
        var (p, _) = Crear(Responde(HttpStatusCode.NotFound, """{"message":"no existe"}"""));

        var r = await p.ConsultarPorPlacaAsync("TST404", Ct);

        r.Resultado.Should().Be(ResultadoRuntIntempo.NoEncontrado, "G3=a: vehículo no registrado");
        (r.Proveedor, r.HttpStatus, r.TipoError).Should().Be(("intempo", 404, TipoErrorFuente.Unspecified));
        r.Vehiculo.Should().BeNull();
        r.DuracionMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Http401_y_403_son_AUTENTICACION(HttpStatusCode codigo)
    {
        var (p, _) = Crear(Responde(codigo, "rechazado"));

        var r = await p.ConsultarPorPlacaAsync("TST301", Ct);

        (r.Resultado, r.TipoError, r.HttpStatus).Should().Be((ResultadoRuntIntempo.Error, TipoErrorFuente.Autenticacion, (int)codigo));
        r.Errores.Should().ContainSingle().Which.Code.Should().Be((int)codigo);
        r.Vehiculo.Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Http500_es_HTTP_NO_EXITOSO_con_cuerpo_recortado_a_300(HttpStatusCode codigo)
    {
        var cuerpo = new string('x', 1000);
        var (p, _) = Crear(Responde(codigo, cuerpo));

        var r = await p.ConsultarPorPlacaAsync("TST301", Ct);

        (r.Resultado, r.TipoError, r.HttpStatus).Should().Be((ResultadoRuntIntempo.Error, TipoErrorFuente.HttpNoExitoso, (int)codigo));
        var prefijo = $"Intempo respondió {(int)codigo}: ";
        r.MensajeError.Should().StartWith(prefijo);
        r.MensajeError[prefijo.Length..].Should().Be(new string('x', 300), "del cuerpo van como mucho 300 caracteres");
        var error = r.Errores.Should().ContainSingle().Subject;
        (error.Code, error.Description).Should().Be(((int)codigo, r.MensajeError));
    }

    [Fact]
    public async Task Timeout_y_red_son_TIMEOUT_y_RED_sin_mensaje_de_excepcion()
    {
        const string conUrl = "https://intempo.prueba.local/pdn/Base/api/RuntConsulta/consultar-vehiculo?Plate=TST301";
        var (timeout, _) = Crear(_ => throw new TaskCanceledException($"Se agotó el tiempo con {conUrl}"));
        var (red, _) = Crear(_ => throw new HttpRequestException($"No se pudo conectar a {conUrl}"));

        var t = await timeout.ConsultarPorPlacaAsync("TST301", Ct);
        var n = await red.ConsultarPorVinAsync("9ZZPRUEBA00000301", Ct);

        (t.Resultado, t.TipoError, t.HasHttpStatus).Should().Be((ResultadoRuntIntempo.Error, TipoErrorFuente.Timeout, false));
        (n.Resultado, n.TipoError, n.HasHttpStatus).Should().Be((ResultadoRuntIntempo.Error, TipoErrorFuente.Red, false));
        foreach (var r in new[] { t, n })
        {
            r.Errores.Should().ContainSingle().Which.Code.Should().Be(404, "§5.6: 404 si no hay status");
            foreach (var texto in new[] { r.MensajeError, r.Errores[0].Description })
                texto.Should().NotBeNullOrWhiteSpace().And.NotContainAny("intempo.prueba.local", "TST301", "9ZZPRUEBA00000301", "Plate", "Se agotó", "No se pudo conectar");
        }

        // La cancelación del llamador no es un timeout: se propaga.
        using var cancelado = new CancellationTokenSource();
        await cancelado.CancelAsync();
        var (p, _) = Crear(Responde(HttpStatusCode.OK, CrudoTst301));
        var llamar = () => p.ConsultarPorPlacaAsync("TST301", cancelado.Token);
        await llamar.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("{no es json")]
    [InlineData("")]
    [InlineData("""{"marca":"MARCA PRUEBA","noPlaca":""}""")]
    [InlineData("null")]
    [InlineData("\"texto\"")]
    [InlineData("[]")]
    [InlineData("[1,2]")]
    public async Task Json_invalido_u_objeto_sin_placa_ni_vin_es_RESPUESTA_INVALIDA(string cuerpo)
    {
        var (p, _) = Crear(Responde(HttpStatusCode.OK, cuerpo));

        var r = await p.ConsultarPorPlacaAsync("TST301", Ct);

        (r.Resultado, r.TipoError, r.HttpStatus).Should().Be((ResultadoRuntIntempo.Error, TipoErrorFuente.RespuestaInvalida, 200));
        r.Vehiculo.Should().BeNull();
        r.Errores.Should().ContainSingle().Which.Code.Should().Be(200);
    }

    [Fact]
    public async Task Arreglo_code_description_es_ESTADO_NO_OK_con_errores()
    {
        var (p, _) = Crear(Responde(HttpStatusCode.OK, """[{"code":500,"description":"falla"}]"""));

        var r = await p.ConsultarPorPlacaAsync("TST301", Ct);

        (r.Resultado, r.TipoError, r.HttpStatus).Should().Be((ResultadoRuntIntempo.Error, TipoErrorFuente.EstadoNoOk, 200));
        r.Errores.Select(e => (e.Code, e.Description)).Should().Equal((500, "falla"));
        r.MensajeError.Should().Contain("500").And.Contain("falla");
        r.Vehiculo.Should().BeNull();

        var larga = new string('d', 1000);
        var (p2, _) = Crear(Responde(HttpStatusCode.OK, $$"""[{"code":404,"description":"{{larga}}"},{"code":502,"description":"otra"}]"""));
        var r2 = await p2.ConsultarPorVinAsync("9ZZPRUEBA00000301", Ct);
        r2.TipoError.Should().Be(TipoErrorFuente.EstadoNoOk);
        r2.Errores.Select(e => (e.Code, e.Description)).Should().Equal((404, new string('d', 300)), (502, "otra"));
    }

    [Fact]
    public async Task Todo_error_lleva_errores_seccion_5_6_y_duracion()
    {
        var escenarios = new (string Nombre, Func<HttpRequestMessage, HttpResponseMessage> Responder, int Codigo)[]
        {
            ("403", Responde(HttpStatusCode.Forbidden), 403),
            ("502", Responde(HttpStatusCode.BadGateway, new string('y', 1000)), 502),
            ("json inválido", Responde(HttpStatusCode.OK, "{"), 200),
            ("objeto sin placa ni vin", Responde(HttpStatusCode.OK, """{"marca":"X"}"""), 200),
            ("timeout", _ => throw new TaskCanceledException(), 404),
            ("red", _ => throw new HttpRequestException(), 404),
        };

        foreach (var (nombre, responder, codigo) in escenarios)
        {
            var (p, _) = Crear(responder);
            var r = await p.ConsultarPorPlacaAsync("TST301", Ct);

            r.Resultado.Should().Be(ResultadoRuntIntempo.Error, nombre);
            r.Proveedor.Should().Be("intempo", nombre);
            r.DuracionMs.Should().BeGreaterThanOrEqualTo(0, nombre);
            r.TipoError.Should().NotBe(TipoErrorFuente.Unspecified, nombre);
            r.MensajeError.Should().NotBeNullOrWhiteSpace(nombre);
            var error = r.Errores.Should().ContainSingle(nombre).Subject;
            (error.Code, error.Description).Should().Be((codigo, r.MensajeError), nombre);
            (r.HasHttpStatus ? r.HttpStatus : 404).Should().Be(codigo, $"{nombre}: errores[0].code es el status, o 404 si no lo hubo");
        }
    }

    private sealed class Fabrica(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => name == ProveedorRuntIntempo.HttpClientName
            ? new(handler, disposeHandler: false)
            : throw new InvalidOperationException($"Cliente inesperado: {name}");
    }

    internal sealed record Pedido(HttpMethod Metodo, string Url, string? Cuerpo, string? TipoContenido, bool Autenticacion);

    /// <summary>Intempo falso: registra cada petición y responde con la función dada (o lanza).</summary>
    private sealed class IntempoFalso(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<Pedido> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cuerpo = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Pedidos.Add(new(request.Method, request.RequestUri!.AbsoluteUri, cuerpo, request.Content?.Headers.ContentType?.MediaType,
                request.Headers.Authorization is not null || request.Headers.Contains("x-api-key")));
            return responder(request);
        }
    }
}
