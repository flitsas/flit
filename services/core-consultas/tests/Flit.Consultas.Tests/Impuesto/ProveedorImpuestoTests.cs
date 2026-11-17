using System.Net;
using System.Text;
using System.Text.Json;
using Flit.Consultas.Api.Bus;
using Flit.Consultas.Api.Impuesto;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto;

public sealed class ProveedorImpuestoTests
{
    [Fact]
    public async Task Departamento_fuera_de_la_lista_no_llama()
    {
        var transporte = new TransporteFalso();
        var proveedor = Proveedor(transporte, "13");
        var accion = async () => await proveedor.ConsultarAsync(Pedido("05", "CC", "999999"), CancellationToken.None);
        await accion.Should().ThrowAsync<RpcException>().Where(e => e.StatusCode == StatusCode.FailedPrecondition);
        transporte.Llamadas.Should().Be(0);
    }

    [Fact]
    public async Task Un_documento_sin_homologar_no_llama()
    {
        var transporte = new TransporteFalso();
        var proveedor = Proveedor(transporte, "76");
        var respuesta = await proveedor.ConsultarAsync(Pedido("76", "RC", "999999"), CancellationToken.None);
        transporte.Llamadas.Should().Be(0);
        respuesta.Resultado.Chequeos.Should().ContainSingle().Which.Clave.Should().Be("impuesto_documento_no_homologado");
        respuesta.Resultado.Chequeos[0].Estado.Should().Be(EstadoChequeo.Error);
    }

    [Fact]
    public async Task El_cuerpo_lleva_el_paso_3_el_contacto_y_no_lleva_credenciales()
    {
        var transporte = new TransporteFalso { Cuerpo = """{"pendingDebtTaxes":false,"observationError":{"typeError":0}}""" };
        var proveedor = Proveedor(transporte, "76");
        await proveedor.ConsultarAsync(Pedido("76", "CC", "999999", "PROPIETARIO DE PRUEBA"), CancellationToken.None);
        using var json = JsonDocument.Parse(transporte.UltimoCuerpo);
        json.RootElement.GetProperty("processStep").GetInt32().Should().Be(3);
        json.RootElement.GetProperty("documentType").GetString().Should().Be("Cédula Ciudadanía");
        json.RootElement.GetProperty("detail").GetProperty("vehicleServiceType").GetString().Should().Be("");
        json.RootElement.GetProperty("detail").GetProperty("OwnerEmail").GetString().Should().Be("prueba@example.com");
        transporte.UltimoCuerpo.Should().NotContain("credentials");
    }

    [Fact]
    public async Task Sin_ruta_la_lista_de_departamentos_queda_vacia()
    {
        var opciones = OpcionesImpuesto.Desde(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());
        var proveedor = new ProveedorImpuesto(new TransporteFalso { Listo = false }, opciones, new CatalogoImpuestoFijo("13", "41"));
        (await proveedor.DepartamentosAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task La_lista_efectiva_sale_del_catalogo_y_no_de_una_variable()
    {
        var opciones = OpcionesImpuesto.Desde(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IMPUESTO_DEPARTAMENTOS"] = "13,41",
                ["IMPUESTO_API_BASE_URL"] = "http://host",
                ["IMPUESTO_API_PATH"] = "api/v1/tax/detail",
            }).Build());
        var proveedor = new ProveedorImpuesto(new TransporteFalso { Listo = true }, opciones, new CatalogoImpuestoFijo("50", "52"));
        (await proveedor.DepartamentosAsync(CancellationToken.None)).Should().Equal("50", "52");
    }

    [Fact]
    public async Task Un_401_anota_credenciales_y_deja_el_chequeo_en_error()
    {
        using var _ = RegistroFallosProveedor.Abrir();
        var transporte = new TransporteFalso
        {
            Fallo = new Flit.Tramites.Application.UseCases.Consultations.FalloProveedor(
                Flit.Tramites.Application.UseCases.Consultations.ClaseFalloProveedor.Credenciales, 401),
        };
        var respuesta = await Proveedor(transporte, "13").ConsultarAsync(Pedido("13", "CC", "999999"), CancellationToken.None);
        respuesta.Resultado.Chequeos[0].Clave.Should().Be("impuesto_autenticacion");
        RegistroFallosProveedor.Motivo().Should().Be("CREDENCIALES");
    }

    [Fact]
    public async Task El_transporte_http_no_manda_autorizacion_ni_escribe_el_mensaje_de_la_excepcion()
    {
        var logger = new ListaLogger();
        var handler = new Handler { Estado = HttpStatusCode.OK, Cuerpo = "{}" };
        var fabrica = new Fabrica(handler);
        var opciones = OpcionesImpuesto.Desde(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["IMPUESTO_API_BASE_URL"] = "http://host",
                ["IMPUESTO_API_PATH"] = "api/v1/tax/detail",
            }).Build());
        var transporte = new TransporteImpuestoHttp(fabrica, opciones, logger);
        await transporte.EnviarAsync("""{"plateNumber":"TST999","documentNumber":"999999","OwnerName":"PROPIETARIO DE PRUEBA"}""", CancellationToken.None);
        handler.Pedido!.Headers.Authorization.Should().BeNull();
        handler.Pedido.RequestUri!.ToString().Should().Be("http://host/api/v1/tax/detail");
        logger.Textos.Should().NotContain(t => t.Contains("TST999", StringComparison.Ordinal) || t.Contains("999999", StringComparison.Ordinal));
    }

    private static ProveedorImpuesto Proveedor(TransporteFalso transporte, string departamentos)
    {
        var opciones = OpcionesImpuesto.Desde(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["IMPUESTO_API_BASE_URL"] = "http://host",
                ["IMPUESTO_API_PATH"] = "api/v1/tax/detail",
                ["IMPUESTO_CONTACTO_CORREO"] = "prueba@example.com",
            }).Build());
        return new ProveedorImpuesto(transporte, opciones, new CatalogoImpuestoFijo(departamentos));
    }

    private static ConsultarImpuestoVehicularRequest Pedido(string departamento, string tipo, string numero, string? nombre = null)
    {
        var pedido = new ConsultarImpuestoVehicularRequest
        {
            Placa = new Placa { Valor = "TST999" },
            DepartamentoCodigo = departamento,
            Documento = new DocumentoIdentidad { Tipo = tipo, Numero = numero },
        };
        if (nombre is not null)
            pedido.PropietarioNombre = nombre;
        return pedido;
    }

    private sealed class TransporteFalso : ITransporteImpuesto
    {
        public bool Listo { get; init; } = true;
        public string NombreProveedor => "impuesto";
        public string Cuerpo { get; init; } = "{}";
        public Flit.Tramites.Application.UseCases.Consultations.FalloProveedor? Fallo { get; init; }
        public int Llamadas { get; private set; }
        public string UltimoCuerpo { get; private set; } = "";

        public Task<ResultadoTransporteImpuesto> EnviarAsync(string cuerpo, CancellationToken ct)
        {
            Llamadas++;
            UltimoCuerpo = cuerpo;
            return Task.FromResult(new ResultadoTransporteImpuesto(Fallo is null ? Cuerpo : null, Fallo));
        }
    }

    private sealed class ListaLogger : ILogger<TransporteImpuestoHttp>
    {
        public List<string> Textos { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Textos.Add(formatter(state, exception));
            if (exception?.Message is { } mensaje)
                Textos.Add(mensaje);
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public HttpStatusCode Estado { get; init; }
        public string Cuerpo { get; init; } = "";
        public HttpRequestMessage? Pedido { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pedido = request;
            return Task.FromResult(new HttpResponseMessage(Estado) { Content = new StringContent(Cuerpo, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Fabrica(Handler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
