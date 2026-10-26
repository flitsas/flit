using System.Collections.Concurrent;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Consultas.Bus;
using Flit.Diagnostico.Api.Modulos.Consultas.Fuentes;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Diagnostico.Tests.Consultas;

/// <summary>
/// HU #13730 (F03-H4), AC5 (F03-C7) — si la celda RUNT de un vehículo termina en ERROR, las demás celdas de ESE vehículo pasan
/// a ERROR con <c>tipo_error=BLOQUEADA_POR_RUNT</c> sin llamar a ninguna fuente, y el resto del lote se procesa normal. Los
/// errores de la fuente (Timeout, RespuestaInvalida, NoConectado) y un fallo inesperado del ejecutor dejan RUNT en ERROR. Desde
/// HU #13737 un Timeout se reintenta antes de bloquear: aquí corre con <c>MaxReintentos=0</c> para ver el bloqueo directo (los
/// reintentos se prueban en <see cref="ReintentosTests"/>). Desde ADR-0075 (HU #13730) la liberación también publica: con la RUNT
/// completada, cada celda liberada deja su trabajo de consulta en la outbox en la misma transacción del cierre (el avalúo con los
/// datos técnicos que acaba de dejar la RUNT); con la RUNT en ERROR no se publica nada. PostgreSQL real (<see cref="ApiDePrueba"/>).
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~BloqueoPorRuntTests
/// </code>
/// </example>
public sealed class BloqueoPorRuntTests
{
    // AC5 (F03-C7) — RUNT en ERROR: SIMIT y avalúo del vehículo quedan en ERROR bloqueadas, sin ninguna llamada.
    [Theory]
    [InlineData("TST007", "simulada", "TIMEOUT")]
    [InlineData("TST008", "simulada", "RESPUESTA_INVALIDA")]
    [InlineData("TST001", "no-conectada", "NO_CONECTADO")]
    [InlineData("TST001", "excepcion", "-")]
    public async Task Runt_en_error_pasa_las_demas_celdas_a_error_sin_llamar_fuentes(string placa, string fuenteRunt, string tipoError)
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        IFuenteRunt interna = fuenteRunt switch
        {
            "no-conectada" => new FuenteRuntNoConectada(),
            "excepcion" => new FuenteQueLanza(),
            _ => new FuenteRuntIntempoSimulada(),
        };
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            new FuenteRuntEspia(orden, interna).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, orden).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.Avaluo, orden).Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila(placa)], ct, Fuente.SimitVehiculo, Fuente.Avaluo);
        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, id, placa, ct);

        // HU #13737: sin reintentos el transitorio (Timeout) bloquea en la primera ejecución, como un definitivo.
        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 0), ct);

        orden.Should().Equal([$"RUNT PLACA_CARRO {placa}"], "solo se llamó a RUNT");
        (await MotorRunt.CeldasDelVehiculoAsync(api, vehiculo.Id, ct)).Should().BeEquivalentTo(
            [$"RUNT ERROR {tipoError}", "SIMIT_VEHICULO ERROR BLOQUEADA_POR_RUNT", "AVALUO ERROR BLOQUEADA_POR_RUNT"]);
        var celdas = await Motor.CeldasAsync(api, empresa.Id, id, ct);
        celdas.Select(c => c.VersionTablero).Distinct().Should().ContainSingle("RUNT y sus bloqueadas se cierran en la misma transacción");
        celdas.Should().OnlyContain(c => c.LeaseHasta == null && c.Payload == null);
        (await MotorRunt.VehiculoAsync(api, empresa, id, placa, ct)).RuntConsultadoEn.Should().BeNull("el RUNT no respondió con datos");
    }

    // AC5 (F03-C7) — el error de RUNT de un vehículo no detiene el resto del lote.
    [Fact]
    public async Task Runt_en_error_de_un_vehiculo_no_detiene_el_resto_del_lote()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            new FuenteRuntEspia(orden).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, orden).Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST008"), Lotes.Fila("TST001")], ct, Fuente.SimitVehiculo);
        var fallido = await MotorRunt.VehiculoAsync(api, empresa, id, "TST008", ct);
        var sano = await MotorRunt.VehiculoAsync(api, empresa, id, "TST001", ct);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api), ct);

        (await MotorRunt.CeldasDelVehiculoAsync(api, fallido.Id, ct)).Should().BeEquivalentTo(
            ["RUNT ERROR RESPUESTA_INVALIDA", "SIMIT_VEHICULO ERROR BLOQUEADA_POR_RUNT"]);
        (await MotorRunt.CeldasDelVehiculoAsync(api, sano.Id, ct)).Should().BeEquivalentTo(["RUNT COMPLETADO -", "SIMIT_VEHICULO COMPLETADO -"]);
        orden.Should().NotContain($"SIMIT_VEHICULO {fallido.Id}");
        orden.Should().ContainSingle(o => o == $"SIMIT_VEHICULO {sano.Id}");
        (await MotorRunt.VehiculoAsync(api, empresa, id, "TST001", ct)).Placa.Should().Be("TST001");
    }

    // ADR-0075 (HU #13730) — RUNT completada: un trabajo por cada celda liberada (SIMIT_VEHICULO y AVALUO), con el trabajo_id que
    // quedó en la celda y en la misma transacción que cierra la RUNT (misma versión del tablero).
    [Fact]
    public async Task Runt_completada_publica_un_trabajo_por_cada_celda_liberada_en_la_misma_transaccion()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, orden).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.Avaluo, orden).Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct, Fuente.SimitVehiculo, Fuente.Avaluo);
        var antes = await OutboxDePrueba.TrabajosAsync(api, ct);

        (await Motor.Procesador(api).ProcesarLoteAsync(ct)).Should().Be(1, "solo la RUNT es tomable");

        var celdas = (await Motor.CeldasAsync(api, empresa.Id, id, ct)).ToDictionary(c => c.Fuente);
        celdas[Fuente.Runt].Estado.Should().Be(EstadoCelda.Completado);
        var liberadas = new[] { celdas[Fuente.SimitVehiculo], celdas[Fuente.Avaluo] };
        liberadas.Should().OnlyContain(c => c.Estado == EstadoCelda.Pendiente && c.TrabajoId != null);
        liberadas.Select(c => c.VersionTablero).Should().OnlyContain(v => v == celdas[Fuente.Runt].VersionTablero, "se liberan en el cierre de la RUNT");
        var nuevos = (await OutboxDePrueba.TrabajosAsync(api, ct)).Skip(antes.Count).ToList();
        nuevos.Select(t => (t.RoutingKey, t.ConsultaId, t.TrabajoId)).Should().BeEquivalentTo(
        [
            ("consultas.diagnostico.simit_vehiculo", celdas[Fuente.SimitVehiculo].Id, celdas[Fuente.SimitVehiculo].TrabajoId!.Value),
            ("consultas.diagnostico.avaluo", celdas[Fuente.Avaluo].Id, celdas[Fuente.Avaluo].TrabajoId!.Value),
        ]);
        nuevos.Single(t => t.RoutingKey.EndsWith(".simit_vehiculo", StringComparison.Ordinal))
            .Data.GetProperty("solicitud").GetProperty("placa").GetProperty("valor").GetString().Should().Be("TST001");
    }

    // ADR-0075 (HU #13730) — RUNT en ERROR: las demás quedan BLOQUEADA_POR_RUNT y no se publica ningún trabajo nuevo.
    [Fact]
    public async Task Runt_en_error_no_publica_trabajos()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorRunt.ArrancarAsync(b =>
        {
            new EjecutorFuenteDePrueba(Fuente.SimitVehiculo, orden).Registrar(b.Services);
            new EjecutorFuenteDePrueba(Fuente.Avaluo, orden).Registrar(b.Services);
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST008")], ct, Fuente.SimitVehiculo, Fuente.Avaluo);

        await MotorRunt.ProcesarTodoAsync(Motor.Procesador(api, maxReintentos: 0), ct);

        var trabajos = await OutboxDePrueba.TrabajosAsync(api, ct);
        trabajos.Should().ContainSingle("solo el de la RUNT, del encolado").Which.RoutingKey.Should().Be("consultas.diagnostico.runt");
        (await Motor.CeldasAsync(api, empresa.Id, id, ct)).Where(c => c.Fuente != Fuente.Runt)
            .Should().OnlyContain(c => c.TipoError == "BLOQUEADA_POR_RUNT" && c.TrabajoId == null);
    }

    // ADR-0075 (HU #13730, Q5) — el avalúo liberado lleva en su *Request el modelo y el cilindraje que acaba de dejar la RUNT.
    [Fact]
    public async Task El_avaluo_liberado_lleva_el_modelo_y_el_cilindraje_de_la_RUNT()
    {
        var ct = TestContext.Current.CancellationToken;
        var orden = new ConcurrentQueue<string>();
        await using var api = await MotorRunt.ArrancarAsync(b => new EjecutorFuenteDePrueba(Fuente.Avaluo, orden).Registrar(b.Services));
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var id = await MotorRunt.LoteAsync(api, empresa, [Lotes.Fila("TST001")], ct, Fuente.Avaluo);

        await Motor.Procesador(api).ProcesarLoteAsync(ct);

        var vehiculo = await MotorRunt.VehiculoAsync(api, empresa, id, "TST001", ct);
        using var datos = JsonDocument.Parse(vehiculo.DatosConsolidados);
        var runt = datos.RootElement.GetProperty("vehiculo");
        var modelo = int.Parse(runt.GetProperty("modelo").ToString(), System.Globalization.CultureInfo.InvariantCulture);
        var cilindraje = int.Parse(runt.GetProperty("cilindraje").ToString(), System.Globalization.CultureInfo.InvariantCulture);
        modelo.Should().BePositive("el RUNT simulado trae el modelo");
        cilindraje.Should().BePositive("el RUNT simulado trae el cilindraje");
        var avaluo = (await OutboxDePrueba.TrabajosAsync(api, ct)).Should().ContainSingle(t => t.RoutingKey == "consultas.diagnostico.avaluo").Subject;
        avaluo.Data.GetProperty("rpc").GetString().Should().Be(RpcConsultas.Avaluos);
        var solicitud = avaluo.Data.GetProperty("solicitud");
        (solicitud.GetProperty("modelo").GetInt32(), solicitud.GetProperty("cilindraje").GetInt32()).Should().Be((modelo, cilindraje));
        solicitud.GetProperty("placa").GetProperty("valor").GetString().Should().Be("TST001");
    }

    /// <summary>Fuente RUNT que falla de forma inesperada (no un error tipado).</summary>
    private sealed class FuenteQueLanza : IFuenteRunt
    {
        public Task<ResultadoRunt> ConsultarVehiculoAsync(Guid empresa, IdentificadorVehiculo identificador, CancellationToken ct) =>
            throw new HttpRequestException("falla inesperada de prueba");
    }
}
