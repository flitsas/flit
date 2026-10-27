using Flit.Consultas.Api.Bus;
using Flit.Consultas.Api.Grpc;
using Flit.Consultas.Grpc.Mapping;
using Flit.Consultas.Grpc.V1;
using Flit.Platform.Grpc.V1;
using Flit.Platform.Sdk.Grpc;
using Flit.Tramites.Application.UseCases.Consultations;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Consultas.Tests.Bus;

/// <summary>
/// Resolvedor de errores del Diagnóstico — en un trabajo del bus core-consultas publica <c>motivoFallo</c> (el
/// <see cref="FalloProveedor.Codigo"/> del último resultado del proveedor) para que Diagnóstico no trate un 403 como un 5xx
/// transitorio. Fuera del bus el colector no hace nada y la respuesta gRPC (Trámites, Comparendos) es exactamente la de hoy.
/// Datos ficticios (TST###, 99999…).
/// <para>Uso de ejemplo: <c>using (RegistroFallosProveedor.Abrir()) { …; RegistroFallosProveedor.Motivo(); }</c>.</para>
/// </summary>
public sealed class MotivoFalloTests(AnfitrionBus bus) : IClassFixture<AnfitrionBus>
{
    private static ConsultarMultasRequest Request(string placa) => new()
    {
        Opciones = new OpcionesConsulta { Proveedor = AnfitrionBus.SinCredenciales.Clave },
        Placa = new Placa { Valor = placa },
        Documento = new DocumentoIdentidad { Tipo = "CC", Numero = "9999900081" },
    };

    [Fact]
    public async Task El_trabajo_SIMIT_con_un_403_del_proveedor_publica_motivoFallo_CREDENCIALES()
    {
        var empresa = Guid.NewGuid();
        await bus.SembrarEmpresaAsync(empresa, "external");
        var sobre = AnfitrionBus.Trabajo(empresa, FuentesBus.SimitVehiculo, RpcConsultas.Multas, Request("TST801"));

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre);

        var publicado = (await bus.ResultadosAsync(sobre)).Should().ContainSingle().Subject;
        var datos = publicado.DataAs<ResultadoConsultaDiagnostico>();
        (datos.Estado, datos.GrpcStatus, datos.MotivoFallo).Should().Be(("OK", (string?)null, "CREDENCIALES"));
        publicado.Data.GetProperty("motivoFallo").GetString().Should().Be("CREDENCIALES", "camelCase, como el resto del sobre");
    }

    [Fact]
    public async Task El_trabajo_con_respuesta_sana_publica_motivoFallo_null()
    {
        var empresa = Guid.NewGuid();
        await bus.SembrarEmpresaAsync(empresa, "external");
        var sobre = AnfitrionBus.Trabajo(empresa, FuentesBus.SimitVehiculo, RpcConsultas.Multas,
            new ConsultarMultasRequest { Opciones = new OpcionesConsulta(), Placa = new Placa { Valor = "TST802" } });

        await bus.EjecutarAsync<ConsumidorTrabajosLote>(sobre);

        var datos = (await bus.ResultadosAsync(sobre)).Should().ContainSingle().Subject.DataAs<ResultadoConsultaDiagnostico>();
        (datos.Estado, datos.MotivoFallo).Should().Be(("OK", (string?)null));
    }

    [Fact]
    public async Task La_respuesta_gRPC_de_ConsultarMultas_es_identica_con_y_sin_el_fallo()
    {
        var empresa = Guid.NewGuid();
        await bus.SembrarEmpresaAsync(empresa, "external");

        await using var scope = bus.App.Services.CreateAsyncScope();
        var servicio = scope.ServiceProvider.GetRequiredService<ConsultasGrpcService>();
        var respuesta = await servicio.ConsultarMultas(Request("TST803"), new PlatformServiceCaller("svc-tramites", empresa), TestContext.Current.CancellationToken);

        var sinFallo = new ConsultarMultasResponse { Resultado = ResultadoConsultaMapper.ToProto(AnfitrionBus.SinCredenciales.Resultado with { Fallo = null }, false) };
        respuesta.Should().Be(sinFallo, "el proto completo no cambia: Fallo no viaja por gRPC");
        ResultadoConsultaMapper.ToProto(AnfitrionBus.SinCredenciales.Resultado, true)
            .Should().Be(ResultadoConsultaMapper.ToProto(AnfitrionBus.SinCredenciales.Resultado with { Fallo = null }, true));
        respuesta.Resultado.Chequeos.Should().ContainSingle().Which.Estado.Should().Be(EstadoChequeo.Error, "el chequeo en ERROR de siempre");
        RegistroFallosProveedor.Motivo().Should().BeNull("fuera de un trabajo del bus nadie lo lee");
    }

    [Fact]
    public void Fuera_del_bus_el_colector_no_tiene_efecto_y_dentro_guarda_el_ultimo()
    {
        RegistroFallosProveedor.Anotar(AnfitrionBus.SinCredenciales.Resultado);
        RegistroFallosProveedor.Motivo().Should().BeNull("sin trabajo abierto es un no-op");

        using (RegistroFallosProveedor.Abrir())
        {
            RegistroFallosProveedor.Anotar(AnfitrionBus.SinCredenciales.Resultado);
            RegistroFallosProveedor.Motivo().Should().Be("CREDENCIALES");
            RegistroFallosProveedor.Anotar(AnfitrionBus.SinCredenciales.Resultado with { Fallo = null });
            RegistroFallosProveedor.Motivo().Should().BeNull("manda el último resultado: el respaldo respondió");
            RegistroFallosProveedor.Anotar(AnfitrionBus.SinCredenciales.Resultado with { Fallo = new FalloProveedor(ClaseFalloProveedor.Limite, 429) });
            RegistroFallosProveedor.Motivo().Should().Be("LIMITE");
        }

        RegistroFallosProveedor.Motivo().Should().BeNull("al cerrar el trabajo el colector se descarta");
    }

    [Fact]
    public async Task Dos_trabajos_concurrentes_no_se_cruzan_el_motivo()
    {
        async Task<string?> Trabajo(FalloProveedor? fallo)
        {
            using var _ = RegistroFallosProveedor.Abrir();
            await Task.Yield();
            RegistroFallosProveedor.Anotar(AnfitrionBus.SinCredenciales.Resultado with { Fallo = fallo });
            await Task.Delay(20, TestContext.Current.CancellationToken);
            return RegistroFallosProveedor.Motivo();
        }

        var motivos = await Task.WhenAll(Trabajo(new FalloProveedor(ClaseFalloProveedor.Credenciales, 401)), Trabajo(new FalloProveedor(ClaseFalloProveedor.Timeout)), Trabajo(null));

        motivos.Should().Equal("CREDENCIALES", "TIMEOUT", null);
    }
}
