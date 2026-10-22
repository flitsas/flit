using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Flit.Consultas.Grpc.V1;
using Flit.Diagnostico.Api.Modulos;
using Flit.Diagnostico.Api.Modulos.Configuracion;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Platform.Sdk.Grpc;
using FluentAssertions;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;
using static Flit.Diagnostico.Tests.Configuracion.ConfiguracionOperativaTests;
using static Flit.Diagnostico.Tests.Configuracion.ContratoBackofficeTests;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13791 (F05-H2, F05-C1, F05-C2) — selección del adaptador de las fuentes de core-consultas: con la simulación (solo
/// LOCAL o DEV) todas son simuladas y no se registra el cliente gRPC; con <c>Diagnostico:Consultas:Address</c>, reales, por
/// <see cref="ClienteConsultas"/> con el scope <c>platform.consultas</c>, la empresa de la celda en la metadata y el deadline
/// configurado; sin nada, no conectadas, y entonces la fuente no se ofrece (422 <c>FUENTE_NO_DISPONIBLE</c>). RUNT conserva
/// su regla (#13725): nunca simulado junto con fuentes reales. Los puertos de prueba hacen las veces de los de F05-H3..H6.
/// </summary>
/// <example>
/// <code>
/// dotnet test tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --no-build --filter "FullyQualifiedName~SeleccionAdaptadoresTests"
/// </code>
/// </example>
public sealed class SeleccionAdaptadoresTests
{
    private const string Direccion = "http://core-consultas.prueba:8084";
    private static readonly Guid Empresa = Guid.Parse("00000000-0000-0000-0000-00000000d3f2");

    // AC1 (F05-C2) — DEV con simulación: todas las fuentes simuladas, también con dirección; ningún cliente hacia core-consultas.
    [Fact]
    public void Simulacion_en_DEV_registra_todas_las_fuentes_simuladas()
    {
        var configuracion = Configuracion(simulacion: true, ambiente: "DEV", direccion: Direccion);
        var servicios = Servicios(configuracion);
        using var proveedor = servicios.BuildServiceProvider();
        var seleccion = proveedor.GetRequiredService<SeleccionAdaptadores>();

        seleccion.Modo.Should().Be(ModoAdaptador.Simulado, "la simulación manda sobre la dirección: nunca se mezcla con proveedores reales");
        SeleccionAdaptadores.FuentesPorConsultas.Should().BeEquivalentTo(
            [Fuente.RuntPropietario, Fuente.RuntLocatario, Fuente.SimitVehiculo, Fuente.SimitPropietario, Fuente.SimitLocatario, Fuente.Avaluo]);
        SeleccionAdaptadores.FuentesPorConsultas.Should().OnlyContain(f => seleccion.ModoDe(f) == ModoAdaptador.Simulado);
        SeleccionAdaptadores.FuentesPorConsultas.Should().NotContain(f => seleccion.EsNoConectada(f));
        proveedor.GetRequiredService<IFuenteRunt>().Should().BeOfType<FuenteRuntIntempoSimulada>();
        proveedor.GetRequiredService<IPuertoDePrueba>().Should().BeOfType<PuertoSimulado>();
        servicios.Should().NotContain(d => d.ServiceType == typeof(ConsultasService.ConsultasServiceClient) || d.ServiceType == typeof(ClienteConsultas),
            "ninguna llamada sale a core-consultas con la simulación");
    }

    // AC4 (negativo, F05-C2) — QA con la simulación encendida no arranca, aunque haya dirección: la simulación es solo de LOCAL o DEV.
    [Theory]
    [InlineData("QA", null)]
    [InlineData("QA", Direccion)]
    [InlineData("PDN", Direccion)]
    [InlineData(null, null)]
    public void Simulacion_en_QA_impide_arrancar(string? ambiente, string? direccion)
    {
        var arrancar = () => Servicios(Configuracion(simulacion: true, ambiente: ambiente, direccion: direccion));

        var error = arrancar.Should().Throw<InvalidOperationException>().Which;
        error.Message.Should().Contain("solo se admite en LOCAL o DEV")
            .And.Contain("Diagnostico__Simulacion__Habilitada").And.Contain("Diagnostico__Ambiente");
    }

    // AC2 (F05-C1, F05-C2) — con dirección y sin simulación: adaptadores reales por el cliente gRPC con scope platform.consultas,
    // la compañía de la celda en x-flit-tenant-id y el deadline por defecto de 90 s. RUNT sigue no conectado (su regla).
    [Fact]
    public async Task Con_direccion_registra_adaptadores_reales()
    {
        var ct = TestContext.Current.CancellationToken;
        var servicios = Servicios(Configuracion(simulacion: false, ambiente: "QA", direccion: Direccion));
        var token = new IdentidadFalsa();
        var consultas = new ConsultasFalso();
        servicios.AddHttpClient(ServiceTokenProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => token);
        servicios.AddHttpClient(NombreClienteGrpc(servicios)).ConfigurePrimaryHttpMessageHandler(() => consultas);
        await using var proveedor = servicios.BuildServiceProvider();
        var seleccion = proveedor.GetRequiredService<SeleccionAdaptadores>();

        seleccion.Modo.Should().Be(ModoAdaptador.Real);
        seleccion.Direccion.Should().Be(new Uri(Direccion));
        seleccion.Deadline.Should().Be(TimeSpan.FromSeconds(ClienteConsultas.DeadlinePorDefectoSegundos));
        SeleccionAdaptadores.FuentesPorConsultas.Should().OnlyContain(f => seleccion.ModoDe(f) == ModoAdaptador.Real && !seleccion.EsNoConectada(f));
        seleccion.ModoDe(Fuente.Runt).Should().NotBe(ModoAdaptador.Simulado, "RUNT nunca simulado con fuentes reales");
        proveedor.GetRequiredService<IFuenteRunt>().Should().BeOfType<FuenteRuntNoConectada>("RUNT conserva su regla hasta C-PER");
        await using var scope = proveedor.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPuertoDePrueba>().Should().BeOfType<PuertoReal>();

        var respuesta = await scope.ServiceProvider.GetRequiredService<ClienteConsultas>().LlamarAsync(Empresa,
            (c, o) => c.ConsultarMultasAsync(new ConsultarMultasRequest { TipoPersona = TipoPersona.Natural }, o), ct);

        token.Scopes.Should().Equal(ClienteConsultas.Scope);
        consultas.Rutas.Should().Equal("/flit.consultas.v1.ConsultasService/ConsultarMultas");
        consultas.Empresas.Should().Equal(Empresa.ToString());
        consultas.Autorizaciones.Should().Equal("Bearer token-de-prueba");
        consultas.Deadlines.Should().ContainSingle().Which.Should().BeCloseTo(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(5));
        respuesta.Exitosa.Should().BeFalse();
        respuesta.Error!.TipoError.Should().Be("AUTENTICACION", "PermissionDenied es definitivo");
        respuesta.Error.EsTransitorio.Should().BeFalse();
    }

    // AC2 — el deadline sale de Diagnostico:Consultas:DeadlineSegundos; una dirección que no es URL impide arrancar.
    [Fact]
    public void Deadline_configurable_y_direccion_invalida_impide_arrancar()
    {
        var configuracion = Configuracion(simulacion: false, ambiente: "QA", direccion: Direccion, deadline: "30");
        SeleccionAdaptadores.Desde(configuracion).Deadline.Should().Be(TimeSpan.FromSeconds(30));

        var invalida = () => SeleccionAdaptadores.Desde(Configuracion(simulacion: false, ambiente: "QA", direccion: "core-consultas sin esquema"));
        invalida.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Diagnostico__Consultas__Address");
        var sinDeadline = () => SeleccionAdaptadores.Desde(Configuracion(simulacion: false, ambiente: "QA", direccion: Direccion, deadline: "0"));
        sinDeadline.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Diagnostico__Consultas__DeadlineSegundos");
    }

    // AC3 (negativo, F05-C2) — QA sin simulación ni dirección: las fuentes nuevas son no conectadas; aunque su ejecutor exista
    // (el de F05-H4, aquí un doble), habilitar SIMIT_VEHICULO en el contrato responde 422 FUENTE_NO_DISPONIBLE y no guarda nada.
    [Fact]
    public async Task Sin_direccion_ni_simulacion_las_fuentes_nuevas_no_se_ofrecen()
    {
        var seleccion = SeleccionAdaptadores.Desde(Configuracion(simulacion: false, ambiente: "QA", direccion: null));
        seleccion.Modo.Should().Be(ModoAdaptador.NoConectado);
        SeleccionAdaptadores.FuentesPorConsultas.Should().OnlyContain(f => seleccion.EsNoConectada(f));
        seleccion.EsNoConectada(Fuente.Runt).Should().BeFalse("RUNT conserva su regla de disponibilidad (#13733)");
        using (var soloServicios = Servicios(Configuracion(simulacion: false, ambiente: "QA", direccion: null)).BuildServiceProvider())
        {
            soloServicios.GetRequiredService<IPuertoDePrueba>().Should().BeOfType<PuertoNoConectado>();
        }

        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync(configurar: b =>
        {
            b.Services.RemoveAll<IRegistroAdaptadores>();
            b.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [Modulos.ClaveAmbiente] = "QA" });
            new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, new ConcurrentQueue<string>()).Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresas = DosEmpresas.En(api);
        await SembrarAsync(empresas.B, new Flit.Diagnostico.Api.Modulos.Configuracion.ConfiguracionEmpresa(), ct);
        var disponibles = api.App.Services.GetRequiredService<FuentesDisponibles>();
        disponibles.Disponible(Fuente.Runt).Should().BeTrue("RUNT conserva su regla");

        var respuesta = await api.Cliente(TokenBackoffice(api, Guid.NewGuid()))
            .PutAsync(Url(empresas.B.Id), Cuerpo("""{ "fuentes_habilitadas": ["RUNT", "SIMIT_VEHICULO"], "limite_vehiculos_lote": 100 }"""), ct);

        respuesta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await respuesta.Content.ReadAsStringAsync(ct));
        (await Codigo(respuesta, ct)).Should().Be(ServicioConfiguracion.CodigoFuenteNoDisponible);
        var deB = await LeerAsync(api, empresas.B.Id, ct);
        deB.FuentesHabilitadas.Should().Equal(Fuente.Runt);
        deB.LimiteVehiculosLote.Should().Be(500, "un 422 no guarda ningún campo");
        (await Motor.EscalarAsync(api, "SELECT count(*) FROM diagnostico.cambios_contrato", ct)).Should().Be(0);
    }

    private static IConfiguration Configuracion(bool simulacion, string? ambiente, string? direccion, string? deadline = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Modulos.ClaveSimulacionHabilitada] = simulacion ? "true" : "false",
            [Modulos.ClaveAmbiente] = ambiente,
            [ClienteConsultas.ClaveDireccion] = direccion,
            [ClienteConsultas.ClaveDeadline] = deadline,
            ["Platform:ServiceClient:TokenEndpoint"] = "http://core-identity.prueba/connect/token",
            ["Platform:ServiceClient:ClientId"] = "svc-diagnostico",
            ["Platform:ServiceClient:ClientSecret"] = "secreto-de-prueba",
        }).Build();

    /// <summary>Lo que hace el arranque (AddFuentesConsultas) más el registro de un puerto como lo harán F05-H3..H6.</summary>
    private static ServiceCollection Servicios(IConfiguration configuracion)
    {
        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddFuentesConsultas(configuracion);
        var seleccion = (SeleccionAdaptadores)servicios.Single(d => d.ServiceType == typeof(SeleccionAdaptadores)).ImplementationInstance!;
        servicios.AddAdaptador<IPuertoDePrueba, PuertoReal, PuertoSimulado, PuertoNoConectado>(seleccion);
        return servicios;
    }

    private static string NombreClienteGrpc(ServiceCollection servicios) =>
        servicios.Select(d => d.ImplementationInstance).OfType<ConfigureNamedOptions<GrpcClientFactoryOptions>>()
            .Select(o => o.Name!).Distinct().Single();

    public interface IPuertoDePrueba;

    public sealed class PuertoReal(ClienteConsultas cliente) : IPuertoDePrueba
    {
        public ClienteConsultas Cliente { get; } = cliente;
    }

    public sealed class PuertoSimulado : IPuertoDePrueba;

    public sealed class PuertoNoConectado : IPuertoDePrueba;

    /// <summary>Endpoint de token de Identidad: registra el scope pedido y entrega un token fijo.</summary>
    private sealed class IdentidadFalsa : HttpMessageHandler
    {
        public ConcurrentQueue<string> Scopes { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            var scope = form.Split('&').Select(p => p.Split('=')).Single(p => p[0] == "scope")[1];
            Scopes.Enqueue(Uri.UnescapeDataString(scope));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"token-de-prueba","expires_in":300}""", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>core-consultas: registra ruta, empresa, autorización y deadline; responde PermissionDenied (solo trailers).</summary>
    private sealed class ConsultasFalso : HttpMessageHandler
    {
        public ConcurrentQueue<string> Rutas { get; } = new();
        public ConcurrentQueue<string> Empresas { get; } = new();
        public ConcurrentQueue<string> Autorizaciones { get; } = new();
        public ConcurrentQueue<TimeSpan> Deadlines { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Rutas.Enqueue(request.RequestUri!.AbsolutePath);
            Empresas.Enqueue(string.Join(",", request.Headers.TryGetValues(PlatformServiceCallInterceptor.TenantMetadata, out var e) ? e : []));
            Autorizaciones.Enqueue(string.Join(",", request.Headers.TryGetValues("Authorization", out var a) ? a : []));
            if (request.Headers.TryGetValues("grpc-timeout", out var t))
                Deadlines.Enqueue(Timeout(t.Single()));
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Version = HttpVersion.Version20, Content = new ByteArrayContent([]) };
            respuesta.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            respuesta.Headers.Add("grpc-status", ((int)global::Grpc.Core.StatusCode.PermissionDenied).ToString(CultureInfo.InvariantCulture));
            respuesta.Headers.Add("grpc-message", "sin permiso");
            return Task.FromResult(respuesta);
        }

        private static TimeSpan Timeout(string valor)
        {
            var numero = long.Parse(valor[..^1], CultureInfo.InvariantCulture);
            return valor[^1] switch
            {
                'H' => TimeSpan.FromHours(numero),
                'M' => TimeSpan.FromMinutes(numero),
                'S' => TimeSpan.FromSeconds(numero),
                'm' => TimeSpan.FromMilliseconds(numero),
                'u' => TimeSpan.FromMicroseconds(numero),
                _ => TimeSpan.FromTicks(numero / 100),
            };
        }
    }
}
