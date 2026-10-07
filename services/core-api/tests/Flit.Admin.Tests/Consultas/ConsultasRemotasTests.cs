using Flit.Api.Consultas;
using Flit.Consultas.Grpc.V1;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Admin.Tests.Consultas;

/// <summary>
/// HU #13346 (Epic #13316) — Trámites consulta por core-consultas (AC1), con respaldo en proceso si no responde (AC2) y
/// «consulta no disponible» sin respaldo (AC3). core-consultas se reemplaza por un CallInvoker falso.
/// </summary>
public sealed class ConsultasRemotasTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private static ConsultationContext Ctx() => new(Guid.NewGuid(), Empresa, "vehiculo", new Dictionary<string, string?>
    {
        ["plate"] = "ABC123",
        ["owner_document_type"] = "CC",
        ["owner_document_number"] = "123",
    });

    private static readonly ConsultationResult EnProceso = new("en_proceso", "green", [], []);

    [Fact]
    public async Task AC1_VaACoreConsultas_ConLosCamposYLaEmpresa_YVuelveElResultado()
    {
        var invoker = new InvokerFalso(_ => new ConsultarVehiculoResponse
        {
            Resultado = new ResultadoConsulta { Proveedor = "kyverum_runt", Semaforo = Semaforo.Verde, Chequeos = { new Chequeo { Clave = "soat", Etiqueta = "SOAT", Estado = EstadoChequeo.Ok } } },
        });
        var resolver = new ConsultasRemotasChainResolver(new CadenaFalsa(), Cliente(invoker, respaldo: true));

        var resultado = await resolver.ConsultAsync(ConsultationKind.VehiclePlate, Ctx(), null, TestContext.Current.CancellationToken);

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
        var registry = new ConsultasRemotasRegistry(new RegistroFalso(), Cliente(invoker, respaldo: true));

        var resultado = await registry.Resolve("flit_fines")!.ConsultAsync(Ctx(), TestContext.Current.CancellationToken);

        resultado.Provider.Should().Be("flit_fines");
        invoker.Pedidos.Single().Should().BeOfType<ConsultarMultasRequest>().Which.Opciones.Proveedor.Should().Be("flit_fines");
    }

    [Fact]
    public void UnProveedorQueConsultasNoAtiende_SeQuedaEnProceso()
    {
        var registry = new ConsultasRemotasRegistry(new RegistroFalso(), Cliente(new InvokerFalso(_ => throw new InvalidOperationException()), respaldo: true));
        registry.Resolve("flit_integrations").Should().BeOfType<ProveedorFalso>();
        registry.Resolve("no_existe").Should().BeNull();
    }

    [Fact]
    public async Task AC2_ConsultasCaido_ConRespaldo_ConsultaEnProceso()
    {
        var invoker = new InvokerFalso(_ => throw new RpcException(new Status(StatusCode.Unavailable, "caído")));
        var resolver = new ConsultasRemotasChainResolver(new CadenaFalsa(), Cliente(invoker, respaldo: true));

        var resultado = await resolver.ConsultAsync(ConsultationKind.VehiclePlate, Ctx(), null, TestContext.Current.CancellationToken);

        resultado.Should().BeSameAs(EnProceso);
    }

    [Fact]
    public async Task AC3_ConsultasCaido_SinRespaldo_ConsultaNoDisponible_SinExcepcion()
    {
        var invoker = new InvokerFalso(_ => throw new RpcException(new Status(StatusCode.Unavailable, "caído")));
        var resolver = new ConsultasRemotasChainResolver(new CadenaFalsa(), Cliente(invoker, respaldo: false));

        var resultado = await resolver.ConsultAsync(ConsultationKind.VehiclePlate, Ctx(), null, TestContext.Current.CancellationToken);

        resultado.Overall.Should().Be("red");
        resultado.Checks.Should().ContainSingle(c => c.Status == "error" && c.Message!.StartsWith("Consulta no disponible", StringComparison.Ordinal));
    }

    [Fact]
    public void ConLaBandera_ElRegistroYLaCadenaQuedanDecorados_YSinElla_Intactos()
    {
        static ServiceProvider Proveedor(bool encendida)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddScoped<IConsultationProviderRegistry, RegistroFalso>();
            services.AddScoped<IConsultationProviderChainResolver, CadenaFalsa>();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Consultas:Remoto:Habilitado"] = encendida ? "true" : "false",
                ["Consultas:Remoto:Address"] = "http://core-consultas:8084",
                ["Platform:ServiceClient:TokenEndpoint"] = "http://gateway/connect/token",
                ["Platform:ServiceClient:ClientId"] = "svc-tramites",
                ["Platform:ServiceClient:ClientSecret"] = "secreto",
            }).Build();
            services.AddSingleton<IConfiguration>(config);
            services.AddConsultasRemoto(config);
            return services.BuildServiceProvider();
        }

        using (var encendido = Proveedor(true))
        using (var scope = encendido.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IConsultationProviderRegistry>().Should().BeOfType<ConsultasRemotasRegistry>();
            scope.ServiceProvider.GetRequiredService<IConsultationProviderChainResolver>().Should().BeOfType<ConsultasRemotasChainResolver>();
        }

        using var apagado = Proveedor(false);
        using var scope2 = apagado.CreateScope();
        scope2.ServiceProvider.GetRequiredService<IConsultationProviderRegistry>().Should().BeOfType<RegistroFalso>();
    }

    private static ConsultasRemotasCliente Cliente(CallInvoker invoker, bool respaldo) =>
        new(new ConsultasService.ConsultasServiceClient(invoker),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [ConsultasRemotasCliente.RespaldoKey] = respaldo ? "true" : "false" }).Build(),
            NullLogger<ConsultasRemotasCliente>.Instance);

    private sealed class CadenaFalsa : IConsultationProviderChainResolver
    {
        public IReadOnlyList<string> ResolveChain(ConsultationKind kind, ConsultationTenantOverride? tenantOverride = null) => ["en_proceso"];

        public Task<ConsultationResult> ConsultAsync(ConsultationKind kind, ConsultationContext ctx, ConsultationTenantOverride? tenantOverride, CancellationToken ct) =>
            Task.FromResult(EnProceso);
    }

    private sealed class RegistroFalso : IConsultationProviderRegistry
    {
        public IConsultationProvider? Resolve(string providerKey) => providerKey == "no_existe" ? null : new ProveedorFalso(providerKey);
    }

    private sealed class ProveedorFalso(string key) : IConsultationProvider
    {
        public string Key => key;

        public Task<ConsultationResult> ConsultAsync(ConsultationContext ctx, CancellationToken ct) => Task.FromResult(EnProceso);
    }

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
