using Flit.Api.Consultas;
using Flit.Consultas.Grpc.V1;
using Flit.Modules.Improntas.Domain;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.Avaluos;
using Flit.Tramites.Application.UseCases.Consultations;
using Flit.Tramites.Application.UseCases.RuntConfirmation;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Admin.Tests.Consultas;

/// <summary>
/// HU #13346/#13348 (Epic #13316) — Trámites consulta por core-consultas (AC1) y, si no responde, «consulta no
/// disponible» sin excepción: desde el corte core-api no tiene proveedores ni respaldo en proceso. Cubre también los
/// clientes del corte: avalúos, RUNT crudo de la Confirmación RUNT, impronta y certificado RUES. core-consultas se
/// reemplaza por un CallInvoker falso.
/// </summary>
public sealed class ConsultasRemotasTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ConsultationContext Ctx() => new(Guid.NewGuid(), Empresa, "vehiculo", new Dictionary<string, string?>
    {
        ["plate"] = "ABC123",
        ["owner_document_type"] = "CC",
        ["owner_document_number"] = "123",
    });

    private static readonly RpcException Caido = new(new Status(StatusCode.Unavailable, "caído"));

    [Fact]
    public async Task AC1_VaACoreConsultas_ConLosCamposYLaEmpresa_YVuelveElResultado()
    {
        var invoker = new InvokerFalso(_ => new ConsultarVehiculoResponse
        {
            Resultado = new ResultadoConsulta { Proveedor = "kyverum_runt", Semaforo = Semaforo.Verde, Chequeos = { new Chequeo { Clave = "soat", Etiqueta = "SOAT", Estado = EstadoChequeo.Ok } } },
        });

        var resultado = await Cadena(invoker).ConsultAsync(ConsultationKind.VehiclePlate, Ctx(), null, Ct);

        resultado.Provider.Should().Be("kyverum_runt");
        resultado.Overall.Should().Be("green");
        resultado.Checks.Should().ContainSingle(c => c.Key == "soat" && c.Status == "ok");
        var pedido = invoker.Pedidos.Should().ContainSingle().Which.Should().BeOfType<ConsultarVehiculoRequest>().Subject;
        pedido.Placa.Valor.Should().Be("ABC123");
        pedido.Propietario.Numero.Should().Be("123");
        pedido.Opciones.IncluirRespuestaCruda.Should().BeTrue();
        pedido.Opciones.Proveedor.Should().BeEmpty("la cadena la aplica Consultas con la configuración de la empresa");
        invoker.Empresas.Should().ContainSingle().Which.Should().Be(Empresa.ToString());
    }

    [Fact]
    public async Task AC1_UnProveedorPedido_VaConSuNombre()
    {
        var invoker = new InvokerFalso(_ => new ConsultarMultasResponse { Resultado = new ResultadoConsulta { Proveedor = "flit_fines", Semaforo = Semaforo.Verde } });

        var resultado = await Registro(invoker).Resolve("flit_fines")!.ConsultAsync(Ctx(), Ct);

        resultado.Provider.Should().Be("flit_fines");
        invoker.Pedidos.Single().Should().BeOfType<ConsultarMultasRequest>().Which.Opciones.Proveedor.Should().Be("flit_fines");
    }

    [Fact]
    public void ElRegistro_NoNecesitaProveedoresLocales_SoloElStubSeQuedaEnProceso()
    {
        var registry = Registro(new InvokerFalso(_ => throw new InvalidOperationException()));

        registry.Resolve("verifik")!.Key.Should().Be("verifik");
        registry.Resolve("kyverum_runt_conductor")!.Key.Should().Be("kyverum_runt_conductor");
        registry.Resolve("flit_integrations")!.Key.Should().Be("flit_integrations");
        registry.Resolve("no_existe").Should().BeNull();
    }

    [Fact]
    public void LaCadenaSeDescribeConLasReglasDeSiempre_SinConsultar()
    {
        var invoker = new InvokerFalso(_ => throw new InvalidOperationException());
        var cadena = Cadena(invoker);

        cadena.ResolveChain(ConsultationKind.VehiclePlate).Should().NotBeEmpty();
        invoker.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public async Task AC3_ConsultasCaido_ConsultaNoDisponible_SinExcepcion()
    {
        var resultado = await Cadena(new InvokerFalso(_ => throw Caido)).ConsultAsync(ConsultationKind.VehiclePlate, Ctx(), null, Ct);

        resultado.Overall.Should().Be("red");
        resultado.Checks.Should().ContainSingle(c => c.Status == "error" && c.Message!.StartsWith("Consulta no disponible", StringComparison.Ordinal));
    }

    // ── HU #13348: avalúos ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Avaluos_VanAConsultasConLosAtributosDelVehiculo_YVuelveElDesglose()
    {
        var invoker = new InvokerFalso(_ => new ConsultarAvaluosResponse
        {
            ValorSugerido = 105_600_000,
            FuentePrincipal = "fasecolda",
            Avaluos =
            {
                new Avaluo { Fuente = "fasecolda", Estado = EstadoAvaluo.Ok, Valor = 105_600_000 },
                new Avaluo { Fuente = "mercado_libre", Estado = EstadoAvaluo.SinDatos, Mensaje = "Sin datos" },
            },
        });
        var campos = new Dictionary<string, string?> { ["vin"] = "93Y9SR333RJ563653", ["vehicle_year"] = "2024", ["vehicle_fuel"] = "GASOLINA" };

        var valor = await new AvaluoSugeridorRemoto(new ConsultasService.ConsultasServiceClient(invoker), NullLogger<AvaluoSugeridorRemoto>.Instance)
            .SugerirAsync(Guid.NewGuid(), Empresa, campos, Ct);

        valor.Sugerido.Should().Be(105_600_000);
        valor.FuentePrincipal.Should().Be("fasecolda");
        valor.Sources.Should().BeEquivalentTo([
            new AvaluoResult("fasecolda", "ok", 105_600_000, "COP", null, null),
            new AvaluoResult("mercado_libre", "no_data", null, "COP", "Sin datos", null),
        ]);
        var pedido = invoker.Pedidos.Single().Should().BeOfType<ConsultarAvaluosRequest>().Subject;
        pedido.Vin.Valor.Should().Be("93Y9SR333RJ563653");
        pedido.Modelo.Should().Be(2024);
        pedido.Combustible.Should().Be("GASOLINA");
        invoker.Empresas.Single().Should().Be(Empresa.ToString());
    }

    [Fact]
    public async Task Avaluos_SinIdentificador_NoLlama_YConConsultasCaido_QuedaVacio()
    {
        var invoker = new InvokerFalso(_ => throw Caido);
        var sugeridor = new AvaluoSugeridorRemoto(new ConsultasService.ConsultasServiceClient(invoker), NullLogger<AvaluoSugeridorRemoto>.Instance);

        (await sugeridor.SugerirAsync(Guid.NewGuid(), Empresa, new Dictionary<string, string?>(), Ct)).Sources.Should().BeEmpty();
        invoker.Pedidos.Should().BeEmpty();

        var caido = await sugeridor.SugerirAsync(Guid.NewGuid(), Empresa, new Dictionary<string, string?> { ["plate"] = "ABC123" }, Ct);
        caido.Sugerido.Should().BeNull();
        caido.Sources.Should().BeEmpty("el FUR deja las filas en blanco y la tarjeta muestra «sin datos»");
    }

    // ── HU #13348: Confirmación RUNT, impronta y RUES ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RuntCrudo_LlevaProveedorPlacaDocumentoYEmpresa_YClasificaElResultado()
    {
        var invoker = new InvokerFalso(_ => new ConsultarVehiculoCrudoResponse { Resultado = ResultadoCrudo.NoEncontrado, RespuestaCruda = "{\"ok\":false}", Mensaje = "no existe" });
        var cliente = new RuntCrudoPorConsultas(new ConsultasService.ConsultasServiceClient(invoker));

        var r = await cliente.ConsultAsync("kyverum_runt", RuntRawQuery.ByPlate("ABC123", new RuntDocument("CC", "123")) with { TenantId = Empresa }, Ct);

        r.Should().Be(new RuntRawQueryResult(RuntRawOutcome.NotFound, "{\"ok\":false}", "no existe"));
        var pedido = invoker.Pedidos.Single().Should().BeOfType<ConsultarVehiculoCrudoRequest>().Subject;
        pedido.Proveedor.Should().Be("kyverum_runt");
        pedido.Placa.Valor.Should().Be("ABC123");
        pedido.Propietario.Numero.Should().Be("123");
        invoker.Empresas.Single().Should().Be(Empresa.ToString());
    }

    [Fact]
    public async Task RuntCrudo_ConConsultasCaido_EsUnIntentoError_NoUnaExcepcion()
    {
        var cliente = new RuntCrudoPorConsultas(new ConsultasService.ConsultasServiceClient(new InvokerFalso(_ => throw Caido)));

        var r = await cliente.ConsultAsync("verifik", RuntRawQuery.ByVin("VIN1"), Ct);

        r.Outcome.Should().Be(RuntRawOutcome.Error);
        r.RawJson.Should().BeNull();
    }

    [Fact]
    public async Task Impronta_ElErrorDelProveedor_LlegaComoImprontaRuntException_ConSuClasificacion()
    {
        var invoker = new InvokerFalso(_ => new GenerarImprontaResponse { Error = new ErrorProveedor { Mensaje = "VALIDATION_ERROR: placa", Transitorio = false } });
        var cliente = new ImprontaPorConsultas(new ConsultasService.ConsultasServiceClient(invoker));

        var generar = () => cliente.GenerarAsync(new ImprontaExternalRequest("ABC123", "123", null, null, null, null, null, null, "OT", null, null, "op", TenantId: Empresa), Ct);

        (await generar.Should().ThrowAsync<ImprontaRuntException>()).Which.IsTransient.Should().BeFalse();
        invoker.Pedidos.Single().Should().BeOfType<GenerarImprontaRequest>().Which.HasVin.Should().BeFalse("un opcional ausente no viaja vacío");
    }

    [Fact]
    public async Task Impronta_Exitosa_DevuelveElCertificado()
    {
        var invoker = new InvokerFalso(_ => new GenerarImprontaResponse { PdfDataUri = "data:application/pdf;base64,JVBERg==", Hash = "h", Radicado = "IMPR-1", FechaImpresa = "2026-10-07 10:00:00" });

        var r = await new ImprontaPorConsultas(new ConsultasService.ConsultasServiceClient(invoker))
            .GenerarAsync(new ImprontaExternalRequest(null, "123", null, null, null, null, null, null, "OT", null, null, "op", Vin: "VIN1"), Ct);

        r.Should().Be(new ImprontaExternalResult("data:application/pdf;base64,JVBERg==", "h", "IMPR-1", "2026-10-07 10:00:00"));
    }

    [Fact]
    public async Task Rues_NoHabilitadoEnConsultas_EsElRespaldoDeCargaManual()
    {
        var cliente = new RuesPorConsultas(new ConsultasService.ConsultasServiceClient(new InvokerFalso(_ => new GenerarCertificadoRuesResponse { Habilitado = false })));

        var generar = () => cliente.GenerarAsync(new RuesExternalRequest("900123456"), Ct);

        (await generar.Should().ThrowAsync<RuesExternalException>()).Which.AutogenDeshabilitada.Should().BeTrue();
    }

    [Fact]
    public async Task Rues_ConConsultasCaido_EsTransitorio()
    {
        var cliente = new RuesPorConsultas(new ConsultasService.ConsultasServiceClient(new InvokerFalso(_ => throw Caido)));

        var generar = () => cliente.GenerarAsync(new RuesExternalRequest("900123456"), Ct);

        var ex = (await generar.Should().ThrowAsync<RuesExternalException>()).Which;
        ex.IsTransient.Should().BeTrue();
        ex.AutogenDeshabilitada.Should().BeFalse();
    }

    [Fact]
    public void SinLaDireccionDeConsultas_NoArranca_YConElla_TodoVaPorConsultas()
    {
        var sinDireccion = () => new ServiceCollection().AddConsultasRemoto(Config(address: null));
        sinDireccion.Should().Throw<InvalidOperationException>().WithMessage("*Consultas:Remoto:Address*");

        var services = new ServiceCollection();
        services.AddLogging();
        var config = Config("http://core-consultas:8084");
        services.AddSingleton(config);
        services.AddConsultasRemoto(config);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<IConsultationProviderRegistry>().Should().BeOfType<ConsultasRemotasRegistry>();
        sp.GetRequiredService<IConsultationProviderChainResolver>().Should().BeOfType<ConsultasRemotasChainResolver>();
        sp.GetRequiredService<IAvaluoSugeridor>().Should().BeOfType<AvaluoSugeridorRemoto>();
        sp.GetRequiredService<IRuntVehicleRawClient>().Should().BeOfType<RuntCrudoPorConsultas>();
        sp.GetRequiredService<IImprontaExternalClient>().Should().BeOfType<ImprontaPorConsultas>();
        sp.GetRequiredService<IRuesExternalClient>().Should().BeOfType<RuesPorConsultas>();
    }

    private static IConfiguration Config(string? address) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Consultas:Remoto:Address"] = address,
            ["Tramites:Bus:Habilitado"] = "true",
            ["Platform:ServiceClient:TokenEndpoint"] = "http://gateway/connect/token",
            ["Platform:ServiceClient:ClientId"] = "svc-tramites",
            ["Platform:ServiceClient:ClientSecret"] = "secreto",
            ["Platform:Messaging:Producer"] = "tramites",
            ["Platform:Messaging:ConnectionString"] = "amqp://tramites:x@127.0.0.1:5672/flit",
        }).Build();

    private static ConsultasRemotasCliente Cliente(CallInvoker invoker) =>
        new(new ConsultasService.ConsultasServiceClient(invoker), NullLogger<ConsultasRemotasCliente>.Instance);

    private static ConsultasRemotasRegistry Registro(CallInvoker invoker) => new(Cliente(invoker));

    private static ConsultasRemotasChainResolver Cadena(CallInvoker invoker) =>
        new(Registro(invoker), Options.Create(new ConsultationChainOptions()), Cliente(invoker));

    /// <summary>Hace de core-consultas: guarda lo que le piden y responde (o falla) con lo que le indiquen.</summary>
    private sealed class InvokerFalso(Func<object, object> responder) : CallInvoker
    {
        public List<object> Pedidos { get; } = [];

        public List<string?> Empresas { get; } = [];

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Pedidos.Add(request);
            Empresas.Add(options.Headers?.GetValue("x-flit-tenant-id"));
            var respuesta = Task.Run(() => (TResponse)responder(request));
            return new AsyncUnaryCall<TResponse>(respuesta, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
