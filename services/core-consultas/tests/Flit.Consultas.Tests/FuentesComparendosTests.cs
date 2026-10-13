using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flit.Consultas.Api;
using Flit.Consultas.Api.Comparendos;
using Flit.Consultas.Api.Persistence;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Sdk.Authentication;
using FluentAssertions;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace Flit.Consultas.Tests;

/// <summary>
/// HU #13520 — <c>flit.consultas.v1.FuentesComparendosService</c>: el registro completo de cada comparendo por el
/// proveedor que pida Comparendos, vacío distinto de error, y los proveedores sin contrato como no conectados.
/// Verifik es falso (no sale a la red): responde según el documento consultado.
/// </summary>
public sealed class FuentesComparendosTests : IAsyncLifetime
{
    private const int GrpcPort = 5997;
    private const string Issuer = "https://hub.prueba/";
    private const string ClaveSmartCloud = "CLAVE123";
    private readonly SimitFalso _simit = new();
    private readonly MunicipalFalsa _municipal = new();
    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "prueba" };
    private readonly Guid _empresa = Guid.NewGuid();
    private string _database = string.Empty;
    private string? _skip;
    private WebApplication? _app;

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_svc_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _skip = $"Postgres no alcanzable: {ex.Message}";
            return;
        }

        _app = Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Servicio"] = _database,
                ["Platform:ServiceClient:ClientSecret"] = "secreto",
                ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity/connect/token",
                ["Platform:Auth:JwksUri"] = "http://core-identity/.well-known/jwks.json",
                ["Platform:Auth:Issuers:0"] = Issuer,
                ["Platform:Messaging:ConnectionString"] = "amqp://flit:x@127.0.0.1:1/",
                ["ImprontaRunt:ApiKey"] = "llave-de-prueba",
                ["Consultations:VerifikSimitMode"] = "real",
                ["Verifik:BearerToken"] = "llave-verifik-de-prueba",
                ["Verifik:BaseUrl"] = "https://verifik.prueba",
                ["Consultations:ComparendosSimulados"] = "true",
                ["SMARTCLOUD_ACCESS_KEY"] = ClaveSmartCloud,
                ["MUNICIPAL_INFRACCIONES_BASE_URL"] = "http://municipal.prueba",
                [ServicioSettings.GrpcPortKey] = GrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
            b.Services.AddHttpClient(PlatformAuthenticationExtensions.JwksHttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Jwks(_key));
        }, s =>
        {
            s.AddHttpClient(ProveedorVerifikSimit.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new VerifikFalsa());
            s.AddHttpClient(ProveedorSmartCloud.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _simit);
            s.AddHttpClient(ProveedorMunicipal.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _municipal);
        });
        await Program.MigrateAsync(_app);
        await _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
        if (_skip is null)
        {
            NpgsqlConnection.ClearAllPools();
            await using var db = new ConsultasDb(new DbContextOptionsBuilder<ConsultasDb>().UseNpgsql(_database).Options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    private Task<ConsultarComparendosResponse> Consultar(string proveedor, CanalComparendos canal, string numero = "", string placa = "", string scope = "platform.consultas", string tipo = "NIT") =>
        Cliente(scope).ConsultarComparendosAsync(new ConsultarComparendosRequest { Canal = canal, Proveedor = proveedor, TipoDocumento = tipo, NumeroDocumento = numero, Placa = placa },
            new Metadata { { "x-flit-tenant-id", _empresa.ToString() } }, cancellationToken: TestContext.Current.CancellationToken).ResponseAsync;

    [Fact]
    public async Task AC1_Verifik_DevuelveElRegistroCompleto_YMideElConsumo()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var r = await Consultar("verifik_simit", CanalComparendos.SimitIdentificacion, "900123456");
        r.Proveedor.Should().Be("verifik_simit");
        var parcial = r.Parciales.Should().ContainSingle().Subject;
        parcial.Resultado.Should().Be(ResultadoFuente.ExitosaConDatos);
        var c = parcial.Comparendos.Should().ContainSingle().Subject;
        (c.Numero, c.Placa, c.TipoDocumentoInfractor, c.NumeroDocumentoInfractor, c.NombreInfractor).Should().Be(("11001000000041234567", "ABC123", "NIT", "900123456", "EMPRESA DE PRUEBA S.A.S."));
        (c.CodigoInfraccion, c.FechaImposicion, c.Secretaria, c.ValorTotal, c.ValorCapital, c.EstadoFuente)
            .Should().Be(("C29", "2026-09-28", "BOGOTA D.C.", "604100.00", "580000.00", "Pendiente de pago"));
        c.HasFechaNotificacion.Should().BeFalse("lo que la fuente no trae va ausente, no vacío");
        r.RespuestaCruda.Should().Contain("multas");

        await using var scope = _app!.Services.CreateAsyncScope();
        var consumo = await scope.ServiceProvider.GetRequiredService<ConsultasDb>().Consumos.AsNoTracking().SingleAsync(x => x.Fuente == "comparendos", TestContext.Current.CancellationToken);
        (consumo.Proveedor, consumo.Resultado, consumo.TenantId).Should().Be(("verifik_simit", "con_datos", _empresa));
    }

    [Theory]
    [InlineData("800000404", ResultadoFuente.ExitosaVacia, TipoErrorFuente.Unspecified)]
    [InlineData("800000200", ResultadoFuente.ExitosaVacia, TipoErrorFuente.Unspecified)]
    [InlineData("800000500", ResultadoFuente.Error, TipoErrorFuente.HttpNoExitoso)]
    [InlineData("800000401", ResultadoFuente.Error, TipoErrorFuente.Autenticacion)]
    [InlineData("800000999", ResultadoFuente.Error, TipoErrorFuente.RespuestaInvalida)]
    public async Task AC2_VacioDistintoDeError(string numero, ResultadoFuente esperado, TipoErrorFuente tipo)
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var parcial = (await Consultar("verifik_simit", CanalComparendos.SimitIdentificacion, numero)).Parciales.Single();
        (parcial.Resultado, parcial.TipoError).Should().Be((esperado, tipo));
        parcial.Comparendos.Should().BeEmpty();
    }

    [Fact]
    public async Task AC3_Canales_YValidaciones()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var lista = await Cliente("platform.consultas").ListarProveedoresComparendosAsync(new ListarProveedoresComparendosRequest(),
            new Metadata { { "x-flit-tenant-id", _empresa.ToString() } }, cancellationToken: TestContext.Current.CancellationToken);
        lista.Proveedores.Single(p => p.Clave == "verifik_simit").Conectado.Should().BeTrue();
        lista.Proveedores.Single(p => p.Clave == "smartcloud").Conectado.Should().BeTrue("hay clave de acceso");
        lista.Proveedores.Single(p => p.Clave == "municipal").Canales.Should().Equal([CanalComparendos.MunicipalIdentificacion], "V1 nunca consultó la fuente municipal por placa");

        (await FluentActions.Awaiting(() => Consultar("municipal", CanalComparendos.MunicipalPlaca, placa: "ABC123")).Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.FailedPrecondition);
        (await FluentActions.Awaiting(() => Consultar("verifik_simit", CanalComparendos.MunicipalIdentificacion, "900123456")).Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.FailedPrecondition, "Verifik no atiende la fuente municipal");
        (await FluentActions.Awaiting(() => Consultar("verifik_simit", CanalComparendos.SimitPlaca)).Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.InvalidArgument);
        (await FluentActions.Awaiting(() => Consultar("verifik_simit", CanalComparendos.SimitIdentificacion, "900123456", scope: "platform.identidad.read")).Should().ThrowAsync<RpcException>())
            .Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task Simulado_PorPlaca_DevuelveDatosDePrueba()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var r = await Consultar("simulado", CanalComparendos.SimitPlaca, placa: "XYZ987");
        r.Parciales.Single().Comparendos.Should().OnlyContain(c => c.Placa == "XYZ987");
        (await Consultar("simulado", CanalComparendos.SimitPlaca, placa: "ABC000")).Parciales.Single().Resultado.Should().Be(ResultadoFuente.Error);
    }

    [Fact]
    public async Task HU13465_SmartCloud_PorIdentificacion_ArmaElSoapDeV1_YMapeaLaFila()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var r = await Consultar("smartcloud", CanalComparendos.SimitIdentificacion, "900123456");

        var pedido = _simit.Ultimo;
        pedido.Url.Should().Be("https://www3.simit.org.co/ServiciosSimit/EstadoCuentaIdentificacion");
        pedido.Cuerpo.Should().Contain("<Documento>CLAVE123900123456</Documento>").And.Contain("<TipoDocumento>4</TipoDocumento>").And.Contain("WsEstadoCuentaIdentificacion");

        var c = r.Parciales.Should().ContainSingle(p => p.Resultado == ResultadoFuente.ExitosaConDatos).Subject.Comparendos.Should().ContainSingle().Subject;
        (c.Numero, c.Placa, c.TipoDocumentoInfractor, c.NumeroDocumentoInfractor, c.NombreInfractor, c.CodigoInfraccion).Should()
            .Be(("11001000000041234567", "ABC123", "NIT", "900123456", "EMPRESA DE PRUEBA SAS", "C29"));
        (c.FechaImposicion, c.HoraImposicion, c.FechaNotificacion, c.EsFotomulta, c.Secretaria, c.ValorTotal, c.EstadoFuente).Should()
            .Be(("2026-09-28", "14:35", "2026-10-02", true, "BOGOTA D.C.", "604100.00", "Pendiente"));
        r.RespuestaCruda.Should().NotContain(ClaveSmartCloud, "la clave de acceso nunca queda en la evidencia").And.Contain("***900123456");
        r.ContentType.Should().Be("text/xml");
    }

    [Fact]
    public async Task HU13466_SmartCloud_PorPlaca_UsaElServicioDePlaca()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var r = await Consultar("smartcloud", CanalComparendos.SimitPlaca, placa: "abc123");
        _simit.Ultimo.Url.Should().Be("https://www3.simit.org.co/ServiciosSimit/EstadoCuentaPlaca");
        _simit.Ultimo.Cuerpo.Should().Contain("<Placa>CLAVE123ABC123</Placa>").And.Contain("WsEstadoCuentaPlaca");
        r.Parciales.Single().Comparendos.Should().ContainSingle(c => c.Placa == "ABC123");
    }

    [Theory]
    [InlineData("800000200", ResultadoFuente.ExitosaVacia, TipoErrorFuente.Unspecified)]
    [InlineData("800000777", ResultadoFuente.Error, TipoErrorFuente.EstadoNoOk)]
    [InlineData("800000401", ResultadoFuente.Error, TipoErrorFuente.Autenticacion)]
    [InlineData("800000666", ResultadoFuente.Error, TipoErrorFuente.SoapFault)]
    [InlineData("800000500", ResultadoFuente.Error, TipoErrorFuente.HttpNoExitoso)]
    [InlineData("800000999", ResultadoFuente.Error, TipoErrorFuente.RespuestaInvalida)]
    public async Task HU13465_SmartCloud_VacioDistintoDeError(string numero, ResultadoFuente esperado, TipoErrorFuente tipo)
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var parcial = (await Consultar("smartcloud", CanalComparendos.SimitIdentificacion, numero)).Parciales.Single();
        (parcial.Resultado, parcial.TipoError).Should().Be((esperado, tipo));
        if (parcial.HasMensajeError)
            parcial.MensajeError.Should().NotContain(ClaveSmartCloud);
    }

    [Fact]
    public async Task HU13465_SmartCloud_TipoDeDocumentoDesconocido_EsError_SinSalirALaRed()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var antes = _simit.Llamadas;
        var parcial = (await Consultar("smartcloud", CanalComparendos.SimitIdentificacion, "900123456", tipo: "XX")).Parciales.Single();
        parcial.Resultado.Should().Be(ResultadoFuente.Error);
        _simit.Llamadas.Should().Be(antes);
    }

    [Fact]
    public async Task HU13467_Municipal_UnParcialPorMunicipio_ConLasFechasEnEspanol()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var r = await Consultar("municipal", CanalComparendos.MunicipalIdentificacion, "900123456");
        _municipal.Ultima!.ToString().Should().Be("http://municipal.prueba/infraction/api/Infraccion/EntregarInfraccion?nit=900123456");

        r.Parciales.Select(p => (p.Municipio, p.Resultado)).Should().BeEquivalentTo(new[]
        {
            ("CHIA", ResultadoFuente.ExitosaConDatos), ("ZIPAQUIRA", ResultadoFuente.ExitosaVacia), ("SOACHA", ResultadoFuente.Error),
        });
        var c = r.Parciales.Single(p => p.Municipio == "CHIA").Comparendos.Should().ContainSingle().Subject;
        (c.Numero, c.Placa, c.CodigoInfraccion, c.FechaImposicion, c.FechaNotificacion, c.EsFotomulta, c.Secretaria, c.ValorTotal).Should()
            .Be(("25175000000012345", "XYZ987", "C14", "2025-03-05", "2025-03-20", false, "CHIA", "604100.00"));
        r.Parciales.Single(p => p.Municipio == "SOACHA").TipoError.Should().Be(TipoErrorFuente.EstadoNoOk);
        r.Parciales.Single(p => p.Municipio == "CHIA").Comparendos.Should().NotContain(x => x.Numero == "OTRODOC", "solo las filas del documento consultado");
    }

    [Fact]
    public async Task HU13467_Municipal_FiltraLosMunicipiosPedidos()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var r = await Cliente("platform.consultas").ConsultarComparendosAsync(new ConsultarComparendosRequest
        {
            Canal = CanalComparendos.MunicipalIdentificacion, Proveedor = "municipal", TipoDocumento = "NIT", NumeroDocumento = "900123456", Municipios = { "chia" },
        }, new Metadata { { "x-flit-tenant-id", _empresa.ToString() } }, cancellationToken: TestContext.Current.CancellationToken);
        r.Parciales.Should().ContainSingle().Which.Municipio.Should().Be("CHIA");
    }

    [Theory]
    [InlineData("800000500", TipoErrorFuente.HttpNoExitoso)]
    [InlineData("800000999", TipoErrorFuente.RespuestaInvalida)]
    [InlineData("800000888", TipoErrorFuente.RespuestaInvalida)]
    public async Task HU13467_Municipal_ErrorNuncaEsVacio(string numero, TipoErrorFuente tipo)
    {
        Assert.SkipWhen(_skip is not null, _skip ?? string.Empty);
        var parcial = (await Consultar("municipal", CanalComparendos.MunicipalIdentificacion, numero)).Parciales.Single();
        (parcial.Resultado, parcial.TipoError).Should().Be((ResultadoFuente.Error, tipo));
    }

    [Theory]
    [InlineData("5 de Marzo de 2025", "2025-03-05")]
    [InlineData("12 de setiembre del 2024", "2024-09-12")]
    [InlineData("2025-03-05T10:20:00", "2025-03-05")]
    [InlineData("05/03/2025", "2025-03-05")]
    [InlineData("2025-03-05 00:00:00.0", "2025-03-05")]
    [InlineData("31 de Febrero de 2025", null)]
    [InlineData("ayer", null)]
    public void S9_FechasDeLasFuentes(string texto, string? esperada) => Fuente.Fecha(texto).Should().Be(esperada);

    [Theory]
    [InlineData("604100", "604100.00")]
    [InlineData("604100.00", "604100.00")]
    [InlineData("604.100", "604100.00")]
    [InlineData("$ 1.208.200,50", "1208200.50")]
    [InlineData("abc", null)]
    public void ValoresEnPesos(string texto, string? esperado) => Fuente.PesosDeTexto(texto).Should().Be(esperado);

    private FuentesComparendosService.FuentesComparendosServiceClient Cliente(string scope)
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "consultas",
            Subject = new ClaimsIdentity([new Claim("sub", "svc-comparendos"), new Claim("scope", scope)]),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });
        var channel = GrpcChannel.ForAddress($"http://localhost:{GrpcPort}", new GrpcChannelOptions { HttpHandler = _app!.GetTestServer().CreateHandler() });
        return new(channel.CreateCallInvoker().Intercept(m =>
        {
            m.Add("authorization", $"Bearer {token}");
            return m;
        }));
    }

    /// <summary>Verifik falsa: el documento decide la respuesta (404, 500, 401, sin lista, vacía o una multa).</summary>
    private sealed class VerifikFalsa : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization!.Parameter.Should().Be("llave-verifik-de-prueba");
            var q = request.RequestUri!.Query;
            static HttpResponseMessage R(HttpStatusCode s, string body) => new(s) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            return Task.FromResult(q switch
            {
                _ when q.Contains("800000404", StringComparison.Ordinal) => R(HttpStatusCode.NotFound, "{}"),
                _ when q.Contains("800000500", StringComparison.Ordinal) => R(HttpStatusCode.InternalServerError, "caído"),
                _ when q.Contains("800000401", StringComparison.Ordinal) => R(HttpStatusCode.Unauthorized, "{}"),
                _ when q.Contains("800000999", StringComparison.Ordinal) => R(HttpStatusCode.OK, """{"data":{"otra":1}}"""),
                _ when q.Contains("800000200", StringComparison.Ordinal) => R(HttpStatusCode.OK, """{"data":{"multas":[]}}"""),
                _ => R(HttpStatusCode.OK, """
                    {"data":{"multas":[{"numeroComparendo":"11001000000041234567","placa":"ABC123","valor":580000,"valorPagar":604100,
                      "organismoTransito":"BOGOTA D.C.","fechaComparendo":"28/09/2026","estadoComparendo":null,"estadoCartera":"Pendiente de pago",
                      "infractor":{"tipoDocumento":"NIT","numeroDocumento":"900123456","nombre":"EMPRESA DE PRUEBA","apellido":"S.A.S."},
                      "infracciones":[{"codigoInfraccion":"C29","descripcionInfraccion":"Velocidad","valorInfraccion":604100}]}]}}
                    """),
            });
        }
    }

    /// <summary>SIMIT falso: responde según el documento, con el sobre SOAP que publica el XSD del SIMIT.</summary>
    private sealed class SimitFalso : HttpMessageHandler
    {
        public (string Url, string Cuerpo) Ultimo { get; private set; }

        public int Llamadas { get; private set; }

        private const string Fila = """
            <comparendo>11001000000041234567</comparendo><descInfraccion>Velocidad</descInfraccion><direccionComparendo>CALLE 80</direccionComparendo>
            <documentoIdentidad>900123456</documentoIdentidad><estado>Pendiente</estado><fechaComparendo>2026-09-28 14:35:00.0</fechaComparendo>
            <fechaNotificacion>02/10/2026</fechaNotificacion><fotodeteccion>S</fotodeteccion><infraccion>C29</infraccion><nombre>EMPRESA DE PRUEBA SAS</nombre>
            <placa>abc123</placa><secretaria>Bogota D.C.</secretaria><servicioVehiculo>PARTICULAR</servicioVehiculo><tipoDocumento>4</tipoDocumento>
            <tipoVehiculo>AUTOMOVIL</tipoVehiculo><valor>604100</valor>
            """;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Llamadas++;
            var cuerpo = await request.Content!.ReadAsStringAsync(cancellationToken);
            Ultimo = (request.RequestUri!.ToString(), cuerpo);
            var placa = cuerpo.Contains("WsEstadoCuentaPlaca", StringComparison.Ordinal);
            var op = placa ? "WsEstadoCuentaPlacaResponse" : "WsEstadoCuentaIdentificacionResponse";
            var lista = placa ? "estadoCuentaPlaca" : "estadoCuentaIdentificacion";
            string Sobre(string ret) => $"""<?xml version="1.0"?><S:Envelope xmlns:S="http://schemas.xmlsoap.org/soap/envelope/"><S:Body><ns2:{op} xmlns:ns2="http://Servicios/"><return>{ret}</return></ns2:{op}></S:Body></S:Envelope>""";
            static HttpResponseMessage R(HttpStatusCode s, string body) => new(s) { Content = new StringContent(body, Encoding.UTF8, "text/xml") };
            return cuerpo switch
            {
                _ when cuerpo.Contains("800000200", StringComparison.Ordinal) => R(HttpStatusCode.OK, Sobre("<error_Cod>0</error_Cod><error_Desc>SIN ERRORES</error_Desc><estadoCuentaSuspensionLicencia/>")),
                _ when cuerpo.Contains("800000401", StringComparison.Ordinal) => R(HttpStatusCode.OK, Sobre("<error_Cod>1</error_Cod><error_Desc>Usuario no permitido</error_Desc><estadoCuentaSuspensionLicencia/>")),
                _ when cuerpo.Contains("800000777", StringComparison.Ordinal) => R(HttpStatusCode.OK, Sobre("<error_Cod>99</error_Cod><error_Desc>Documento CLAVE123800000777 no autorizado</error_Desc>")),
                _ when cuerpo.Contains("800000666", StringComparison.Ordinal) => R(HttpStatusCode.InternalServerError,
                    """<S:Envelope xmlns:S="http://schemas.xmlsoap.org/soap/envelope/"><S:Body><S:Fault><faultcode>S:Server</faultcode><faultstring>Clave CLAVE123 inválida</faultstring></S:Fault></S:Body></S:Envelope>"""),
                _ when cuerpo.Contains("800000500", StringComparison.Ordinal) => R(HttpStatusCode.BadGateway, "caído"),
                _ when cuerpo.Contains("800000999", StringComparison.Ordinal) => R(HttpStatusCode.OK, "<html>mantenimiento</html>"),
                _ => R(HttpStatusCode.OK, Sobre($"<error_Cod>0</error_Cod><{lista}>{Fila}</{lista}><documento>CLAVE123900123456</documento>")),
            };
        }
    }

    /// <summary>API municipal falsa con el formato de V1: tres municipios (con datos, vacío y caído).</summary>
    private sealed class MunicipalFalsa : HttpMessageHandler
    {
        public Uri? Ultima { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Ultima = request.RequestUri;
            var q = request.RequestUri!.Query;
            static HttpResponseMessage R(HttpStatusCode s, string body) => new(s) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            return Task.FromResult(q switch
            {
                _ when q.Contains("800000500", StringComparison.Ordinal) => R(HttpStatusCode.InternalServerError, "caído"),
                _ when q.Contains("800000999", StringComparison.Ordinal) => R(HttpStatusCode.OK, """{"otra":1}"""),
                _ when q.Contains("800000888", StringComparison.Ordinal) => R(HttpStatusCode.OK, """{"base":[],"detalle":[]}"""),
                _ => R(HttpStatusCode.OK, """
                    {"base":[{"documento":"900123456","fuente":"CHIA","estadoRespuesta":"OK"},{"documento":"900123456","fuente":"ZIPAQUIRA","estadoRespuesta":"ok"},
                             {"documento":"900123456","fuente":"SOACHA","estadoRespuesta":"ERROR CONEXION"}],
                     "detalle":[{"documentoConsulta":"900123456","fuenteConsulta":"CHIA","comparendo":"25175000000012345","infraccion":"C14","descInfraccion":"Pico y placa",
                                 "direccionComparendo":"CRA 7","estado":"PENDIENTE","fechaComparendo":"5 de Marzo de 2025","fechaNotificacion":"2025-03-20T00:00:00",
                                 "fotodeteccion":"NO","placa":"xyz987","servicioVehiculo":"PARTICULAR","tipoVehiculo":"AUTOMOVIL","valor":604100},
                                {"documentoConsulta":"111","fuenteConsulta":"CHIA","comparendo":"OTRODOC"}]}
                    """),
            });
        }
    }

    private sealed class Jwks(RsaSecurityKey key) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(key.Rsa.ExportParameters(false)) { KeyId = key.KeyId });
            jwk.Use = "sig";
            jwk.Alg = SecurityAlgorithms.RsaSha256;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { keys = new[] { jwk } })) });
        }
    }
}
