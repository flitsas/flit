using Flit.Consultas.Grpc.V1;
using Flit.Ict.Infrastructure;
using Flit.Ict.Infrastructure.ExternalClients;
using FluentAssertions;
using Grpc.Core;
using GrpcStatus = Grpc.Core.Status;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Ict.Application.Tests.Consultas;

/// <summary>
/// HU #13346/#13348 (Epic #13316) — ICT consulta directo a core-consultas con su cliente svc-ict (producto ict), sin
/// respaldo por core-api. Los hechos que ven los validadores son los mismos que daba core-api.
/// </summary>
public sealed class ConsultasConsultationClientTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    [Fact]
    public async Task Vehiculo_TraduceSoatRtmModeloYOrganismo_ComoCoreApi()
    {
        var consultas = new Invoker(_ => new ConsultarVehiculoResponse
        {
            Resultado = new ResultadoConsulta
            {
                Proveedor = "kyverum_runt",
                Chequeos = { new Chequeo { Clave = "soat", Estado = EstadoChequeo.Ok }, new Chequeo { Clave = "tecnomecanica", Estado = EstadoChequeo.Fail } },
                Campos = { new Campo { Clave = "vehicle_year", ValorTexto = "2019" }, new Campo { Clave = "transit_office_name", ValorTexto = "STRIA BOGOTA" } },
            },
        });

        var r = await Cliente(consultas)
            .QueryAsync(Empresa, "VEHICLE", "ABC123", "", "CC", "123", TestContext.Current.CancellationToken);

        r.SoatStatus.Should().Be("VIGENTE");
        r.RtmStatus.Should().Be("NO_VIGENTE");
        r.VehicleModelYear.Should().Be(2019);
        r.TransitOfficeName.Should().Be("STRIA BOGOTA");
        var pedido = consultas.Pedidos.Single().Should().BeOfType<ConsultarVehiculoRequest>().Subject;
        pedido.Placa.Valor.Should().Be("ABC123");
        pedido.Vin.Should().BeNull();
        pedido.Propietario.Numero.Should().Be("123");
        consultas.Empresas.Single().Should().Be(Empresa.ToString());
    }

    [Fact]
    public async Task RnmcYConductor_SancionesYPazYSalvo()
    {
        var consultas = new Invoker(req => req switch
        {
            ConsultarRnmcRequest => new ConsultarRnmcResponse { Resultado = new ResultadoConsulta { Chequeos = { new Chequeo { Clave = "medidas_correctivas", Estado = EstadoChequeo.Warn } } } },
            _ => new ConsultarConductorResponse { Resultado = new ResultadoConsulta { Campos = { new Campo { Clave = "person_has_pending_fines", ValorTexto = "true" } } } },
        });
        var cliente = Cliente(consultas);
        var ct = TestContext.Current.CancellationToken;

        (await cliente.QueryAsync(Empresa, "RNMC", "", "", "CC", "123", ct)).HasActiveSanctions.Should().BeTrue();
        (await cliente.QueryAsync(Empresa, "DRIVER", "", "", "CC", "123", ct)).PazYSalvo.Should().BeFalse();
    }

    [Fact]
    public async Task ConsultasCaido_ElErrorSePropaga_YElOrquestadorReintenta()
    {
        var cliente = Cliente(new Invoker(_ => throw new RpcException(new GrpcStatus(StatusCode.Unavailable, "caído"))));

        var act = () => cliente.QueryAsync(Empresa, "VEHICLE", "ABC123", "", "", "", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<RpcException>();
    }

    [Fact]
    public async Task UnTipoDeConsultaDesconocido_Falla_SinLlamar()
    {
        var consultas = new Invoker(_ => throw new InvalidOperationException("no debía llamar"));

        var act = () => Cliente(consultas).QueryAsync(Empresa, "OTRA", "ABC123", "", "", "", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unknown_query_type*");
        consultas.Pedidos.Should().BeEmpty();
    }

    [Fact]
    public void SinDireccionOSinTokenDeIdentidad_NoArranca()
    {
        var sinDireccion = () => IctInfrastructureExtensions.AddConsultasRemoto(new ServiceCollection(), Config(), useIdentityToken: true);
        sinDireccion.Should().Throw<InvalidOperationException>().WithMessage("*CoreConsultas:Address*");

        var sinToken = () => IctInfrastructureExtensions.AddConsultasRemoto(new ServiceCollection(), Config(("CoreConsultas:Address", "http://core-consultas:8084")), useIdentityToken: false);
        sinToken.Should().Throw<InvalidOperationException>().WithMessage("*UseIdentity*");
    }

    private static ConsultasConsultationClient Cliente(CallInvoker consultas) => new(new ConsultasService.ConsultasServiceClient(consultas));

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private sealed class Invoker(Func<object, object> responder) : CallInvoker
    {
        public List<object> Pedidos { get; } = [];

        public List<string?> Empresas { get; } = [];

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            Pedidos.Add(request);
            Empresas.Add(options.Headers?.GetValue("x-flit-tenant-id"));
            var respuesta = Task.Run(() => (TResponse)responder(request));
            return new AsyncUnaryCall<TResponse>(respuesta, Task.FromResult(new Metadata()), () => GrpcStatus.DefaultSuccess, () => [], () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) => throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) => throw new NotSupportedException();
    }
}
