using Flit.Comparendos.Api.Modulos.Contraventor;
using Flit.Comparendos.Domain.Modelo;
using Flit.Consultas.Grpc.V1;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>HU #13712 — la API de contratos de Renting por core-consultas, traducida a lo que asigna el conductor.</summary>
public sealed class ApiContratosCoreConsultasTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateOnly Fecha = new(2026, 10, 18);

    private static Task<RespuestaContrato> Consultar(ConsultarContratoConductorResponse? respuesta, RpcException? falla = null, bool simulada = false, ClienteFalso? cliente = null)
    {
        cliente ??= new ClienteFalso(respuesta, falla);
        return new ApiContratosCoreConsultas(cliente, simulada ? new ApiContratosSimulada() : null).ConsultarAsync(Empresa, "LZO228", Fecha, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AC1_Encontrado_AsignaConElDocumento_YPideConLaEmpresaYLaFecha()
    {
        var cliente = new ClienteFalso(new ConsultarContratoConductorResponse
        {
            Proveedor = "renting", Resultado = ResultadoContratoConductor.Encontrado, ContactoDesviado = true,
            Conductor = new ConductorDeContrato { TipoDocumento = "CC", NumeroDocumento = "1020304050", Nombres = "ANA PEREZ", Correo = "buzon.pruebas@flitsas.com", Ciudad = "MEDELLIN" },
        }, null);
        var r = await Consultar(null, cliente: cliente);

        r.Resultado.Should().Be(ResultadoContrato.Encontrado);
        r.Conductor.Should().Be(new ConductorEncontrado(TipoDocumento.CC, "1020304050", "ANA PEREZ", "buzon.pruebas@flitsas.com", null, null, "MEDELLIN", null, null));
        (cliente.Pedido!.Proveedor, cliente.Pedido.Placa, cliente.Pedido.Fecha).Should().Be(("renting", "LZO228", "2026-10-18"));
        cliente.Empresa.Should().Be(Empresa.ToString());
    }

    /// <summary>Renting real (2026-10-20) no devuelve el documento: se asigna con nombre y correo, documento pendiente.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "1020304050")]
    [InlineData("CC", null)]
    [InlineData("XX", "1020304050")]
    public async Task AC1_SinDocumentoValido_SeAsignaConDocumentoPendiente(string? tipo, string? numero)
    {
        var conductor = new ConductorDeContrato { Nombres = "ANA PEREZ", Correo = "buzon.pruebas@flitsas.com" };
        if (tipo is not null) conductor.TipoDocumento = tipo;
        if (numero is not null) conductor.NumeroDocumento = numero;
        var r = await Consultar(new ConsultarContratoConductorResponse { Resultado = ResultadoContratoConductor.Encontrado, Conductor = conductor });
        r.Resultado.Should().Be(ResultadoContrato.Encontrado);
        (r.Conductor!.TipoDocumento, r.Conductor.NumeroDocumento, r.Conductor.Nombres, r.Conductor.Correo).Should().Be(((TipoDocumento?)null, (string?)null, "ANA PEREZ", "buzon.pruebas@flitsas.com"));
    }

    [Fact]
    public async Task AC1_SinNombre_SeAsignaAMano()
    {
        var r = await Consultar(new ConsultarContratoConductorResponse { Resultado = ResultadoContratoConductor.Encontrado, Conductor = new ConductorDeContrato { Correo = "x@flitsas.com" } });
        (r.Resultado, r.Conductor).Should().Be((ResultadoContrato.NoEncontrado, (ConductorEncontrado?)null));
        r.Mensaje.Should().Contain("asígnalo a mano");
    }

    [Fact]
    public async Task AC2_NoEncontrado()
    {
        var r = await Consultar(new ConsultarContratoConductorResponse { Resultado = ResultadoContratoConductor.NoEncontrado });
        r.Resultado.Should().Be(ResultadoContrato.NoEncontrado);
    }

    [Fact]
    public async Task AC3_ErrorDeRenting_YCoreConsultasCaido_SonError()
    {
        var r = await Consultar(new ConsultarContratoConductorResponse { Resultado = ResultadoContratoConductor.Error, TipoError = Flit.Consultas.Grpc.V1.TipoErrorFuente.Timeout, MensajeError = "Renting no respondió a tiempo." });
        (r.Resultado, r.Mensaje).Should().Be((ResultadoContrato.Error, "Renting no respondió a tiempo."));

        var caido = await Consultar(null, new RpcException(new Status(StatusCode.Unavailable, "sin conexión")), simulada: true);
        (caido.Resultado, caido.Mensaje).Should().Be((ResultadoContrato.Error, "core-consultas: sin conexión"), "solo «no conectado» cae a la simulada");
    }

    [Fact]
    public async Task AC4_RentingNoConectado_EnDevUsaLaSimulada_YFueraEsError()
    {
        var noConectado = new RpcException(new Status(StatusCode.FailedPrecondition, "Falta configurar la API de contratos de Renting: RENTING_API_BASE_URL."));
        (await Consultar(null, noConectado, simulada: true)).Resultado.Should().Be(ResultadoContrato.Encontrado, "LZO228 termina en par: conductor de prueba");

        var fuera = await Consultar(null, noConectado, simulada: false);
        (fuera.Resultado, fuera.Mensaje).Should().Be((ResultadoContrato.Error, "core-consultas: Falta configurar la API de contratos de Renting: RENTING_API_BASE_URL."));
    }

    private sealed class ClienteFalso(ConsultarContratoConductorResponse? respuesta, RpcException? falla) : FuentesComparendosService.FuentesComparendosServiceClient
    {
        public ConsultarContratoConductorRequest? Pedido { get; private set; }

        public string? Empresa { get; private set; }

        public override AsyncUnaryCall<ConsultarContratoConductorResponse> ConsultarContratoConductorAsync(ConsultarContratoConductorRequest request, Metadata headers = null!,
            DateTime? deadline = null, CancellationToken cancellationToken = default)
        {
            (Pedido, Empresa) = (request, headers?.GetValue("x-flit-tenant-id"));
            var tarea = falla is null ? Task.FromResult(respuesta!) : Task.FromException<ConsultarContratoConductorResponse>(falla);
            return new AsyncUnaryCall<ConsultarContratoConductorResponse>(tarea, Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => [], () => { });
        }
    }
}
