using System.Collections.Concurrent;
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
/// reintentos se prueban en <see cref="ReintentosTests"/>). PostgreSQL real (<see cref="ApiDePrueba"/>).
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

    /// <summary>Fuente RUNT que falla de forma inesperada (no un error tipado).</summary>
    private sealed class FuenteQueLanza : IFuenteRunt
    {
        public Task<ResultadoRunt> ConsultarVehiculoAsync(Guid empresa, IdentificadorVehiculo identificador, CancellationToken ct) =>
            throw new HttpRequestException("falla inesperada de prueba");
    }
}
