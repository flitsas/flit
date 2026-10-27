using Flit.Consultas.Api.Bus;
using Flit.Consultas.Api.Grpc;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// Bug (registro diferido 2026-10-27) — SIMIT del vehículo por placa: <c>ConsultarMultas</c> exigía el documento aunque viniera la
/// placa, y la celda SIMIT_VEHICULO de Diagnóstico quedaba siempre en ERROR con fuentes reales. Regla: la consulta va por PLACA;
/// el documento es obligatorio solo sin placa. Verifik SIMIT consulta por placa (aunque llegue también el documento, sigue siendo
/// la consulta del vehículo); <c>flit_fines</c> y <c>kyverum_fines</c> exigen el documento (el del propietario que dejó el RUNT)
/// y sin él responden InvalidArgument con un mensaje propio. Datos ficticios (TST###, 99999…).
/// <para>Uso de ejemplo: <c>await Multas(empresa, new ConsultarMultasRequest { Placa = new Placa { Valor = "TST701" } })</c>.</para>
/// </summary>
public sealed class MultasPorPlacaTests(AnfitrionBus bus) : IClassFixture<AnfitrionBus>
{
    private const string Numero = "9999900071";

    [Fact]
    public async Task Placa_sin_documento_llega_a_Verifik_por_placa()
    {
        var empresa = Guid.NewGuid();
        await bus.SembrarEmpresaAsync(empresa, "external");

        var respuesta = await MultasAsync(empresa, new ConsultarMultasRequest { Opciones = new OpcionesConsulta(), Placa = new Placa { Valor = "TST701" } });

        respuesta.Resultado.Proveedor.Should().Be(FinesProviderResolver.VerifikSimit);
        var campos = CamposConPlaca(FinesProviderResolver.VerifikSimit, "TST701");
        campos.Keys.Should().BeEquivalentTo(["plate"], "por placa, sin owner_document_*");
    }

    [Fact]
    public async Task Placa_y_documento_con_Verifik_sigue_consultando_por_placa()
    {
        var empresa = Guid.NewGuid();
        await bus.SembrarEmpresaAsync(empresa, "external");

        var respuesta = await MultasAsync(empresa, new ConsultarMultasRequest
        {
            Opciones = new OpcionesConsulta(),
            Placa = new Placa { Valor = "TST702" },
            Documento = new DocumentoIdentidad { Tipo = "CC", Numero = Numero },
        });

        respuesta.Resultado.Proveedor.Should().Be(FinesProviderResolver.VerifikSimit);
        CamposConPlaca(FinesProviderResolver.VerifikSimit, "TST702").Keys.Should()
            .BeEquivalentTo(["plate"], "traer el documento no convierte la consulta del vehículo en la de una persona");
    }

    [Fact]
    public async Task Proveedor_que_exige_documento_recibe_el_documento_del_RUNT()
    {
        var empresa = Guid.NewGuid();
        await bus.SembrarEmpresaAsync(empresa, "internal");

        var respuesta = await MultasAsync(empresa, new ConsultarMultasRequest
        {
            Opciones = new OpcionesConsulta(),
            Placa = new Placa { Valor = "TST703" },
            Documento = new DocumentoIdentidad { Tipo = "CC", Numero = Numero },
        });

        respuesta.Resultado.Proveedor.Should().Be(FinesProviderResolver.FlitFines);
        var campos = CamposConPlaca(FinesProviderResolver.FlitFines, "TST703");
        (campos["owner_document_type"], campos["owner_document_number"]).Should().Be(("CC", Numero));
    }

    public static TheoryData<string, string, ConsultarMultasRequest, string> Invalidos => new()
    {
        { "sin placa ni documento", "external", new ConsultarMultasRequest { Opciones = new OpcionesConsulta() }, "Se necesita el documento (tipo y número) o la placa." },
        { "fuente interna con placa sola", "internal", new ConsultarMultasRequest { Opciones = new OpcionesConsulta(), Placa = new Placa { Valor = "TST704" } }, "flit_fines" },
        {
            "Kyverum (jurídica) con placa sola", "external",
            new ConsultarMultasRequest { Opciones = new OpcionesConsulta(), Placa = new Placa { Valor = "TST705" }, TipoPersona = TipoPersona.Juridica }, "kyverum_fines"
        },
    };

    [Theory]
    [MemberData(nameof(Invalidos))]
    public async Task Sin_lo_que_exige_el_proveedor_es_InvalidArgument_con_mensaje_propio(string caso, string fuente, ConsultarMultasRequest request, string mensaje)
    {
        var empresa = Guid.NewGuid();
        await bus.SembrarEmpresaAsync(empresa, fuente);

        var llamada = () => MultasAsync(empresa, request);

        var error = (await llamada.Should().ThrowAsync<RpcException>(caso)).Which;
        error.StatusCode.Should().Be(StatusCode.InvalidArgument, caso);
        error.Status.Detail.Should().Contain(mensaje, caso);
    }

    [Fact]
    public async Task Por_persona_no_cambia_Verifik_recibe_el_documento_sin_placa()
    {
        var empresa = Guid.NewGuid();
        await bus.SembrarEmpresaAsync(empresa, "external");
        var antes = bus.Llamadas.CamposDe(FinesProviderResolver.VerifikSimit).Count;

        await MultasAsync(empresa, new ConsultarMultasRequest
        {
            Opciones = new OpcionesConsulta(),
            Documento = new DocumentoIdentidad { Tipo = "CC", Numero = "9999900072" },
            TipoPersona = TipoPersona.Natural,
        });

        var campos = bus.Llamadas.CamposDe(FinesProviderResolver.VerifikSimit).Skip(antes).Should().ContainSingle().Subject;
        campos.Keys.Should().BeEquivalentTo(["owner_document_type", "owner_document_number"]);
        campos["owner_document_number"].Should().Be("9999900072");
    }

    [Fact]
    public async Task El_trabajo_SIMIT_VEHICULO_por_placa_del_bus_es_OK_con_Verifik()
    {
        var empresa = Guid.NewGuid();
        await bus.SembrarEmpresaAsync(empresa, "external");
        var sobre = AnfitrionBus.Trabajo(empresa, FuentesBus.SimitVehiculo, RpcConsultas.Multas,
            new ConsultarMultasRequest { Opciones = new OpcionesConsulta(), Placa = new Placa { Valor = "TST706" } });

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre);

        var datos = (await bus.ResultadosAsync(sobre)).Should().ContainSingle().Subject.DataAs<ResultadoConsultaDiagnostico>();
        (datos.Estado, datos.GrpcStatus).Should().Be(("OK", (string?)null));
        JsonParser.Default.Parse<ConsultarMultasResponse>(datos.Respuesta!.Value.GetRawText()).Resultado.Proveedor.Should().Be(FinesProviderResolver.VerifikSimit);
    }

    private async Task<ConsultarMultasResponse> MultasAsync(Guid empresa, ConsultarMultasRequest request)
    {
        await using var scope = bus.App.Services.CreateAsyncScope();
        var servicio = scope.ServiceProvider.GetRequiredService<ConsultasGrpcService>();
        return await servicio.ConsultarMultas(request, new PlatformServiceCaller("svc-diagnostico", empresa), TestContext.Current.CancellationToken);
    }

    private IReadOnlyDictionary<string, string?> CamposConPlaca(string proveedor, string placa) =>
        bus.Llamadas.CamposDe(proveedor).Should().ContainSingle(c => c.ContainsKey("plate") && c["plate"] == placa).Subject;
}
