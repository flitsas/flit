using System.Net;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Tablero;

/// <summary>
/// Ajuste 2026-11-11 Punto 3 (AC3, AC4) — la grilla del lote (<c>GET /diagnosticos/{id}/grilla</c>) dice qué placas están pegadas:
/// <c>pegada</c> en cada fila que viaja y, siempre (también en el sondeo incremental), <c>total_pegadas</c> (placas) y
/// <c>vehiculos_pegados</c> (sus ids) a nivel del lote. «Pegada» = alguna celda que el reintento reencolaría (ERROR, o
/// PENDIENTE/EN_CURSO con más de 10 min; con la RUNT sin completar, solo la RUNT). PostgreSQL real.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~GrillaPegadasTests
/// </code>
/// </example>
public sealed class GrillaPegadasTests
{
    [Fact]
    public async Task La_grilla_marca_las_placas_pegadas_y_cuenta_las_del_lote()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = DosEmpresas.En(api).A;
        var lote = await Tablero.SembrarAsync(empresa, ct, version: 9, vehiculos:
        [
            new("TST401", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Error, 2)]),
            new("TST402", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.EnCurso, 2)]),
            new("TST403", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.EnCurso, 2)]),
            new("TST404", [new(Fuente.Runt, EstadoCelda.Completado, 1), new(Fuente.SimitVehiculo, EstadoCelda.Completado, 1)]),
        ]);
        await ReintentoPegadasTests.HaceMinutosAsync(api, lote.Celda("TST402", Fuente.SimitVehiculo).Id, 11, ct);
        await ReintentoPegadasTests.HaceMinutosAsync(api, lote.Celda("TST403", Fuente.SimitVehiculo).Id, 9, ct);
        using var cliente = api.Cliente(api.Token(empresa: empresa.Id, permisos: [Permisos.DiagnosticosVer], roles: ["usuario_diagnostico"]));

        var completa = await Tablero.JsonAsync(await cliente.GetAsync(Tablero.RutaGrilla(lote.Id), ct), ct);

        completa.GetProperty("total_pegadas").GetInt32().Should().Be(2);
        completa.GetProperty("vehiculos_pegados").EnumerateArray().Select(e => e.GetGuid())
            .Should().BeEquivalentTo([Vehiculo(lote, "TST401"), Vehiculo(lote, "TST402")]);
        completa.GetProperty("vehiculos").EnumerateArray()
            .ToDictionary(v => v.GetProperty("identificador").GetString()!, v => v.GetProperty("pegada").GetBoolean())
            .Should().Equal(new Dictionary<string, bool> { ["TST401"] = true, ["TST402"] = true, ["TST403"] = false, ["TST404"] = false });

        // Sondeo incremental sin cambios: no viajan filas, pero el lote sigue diciendo cuántas y cuáles están pegadas.
        var incremental = await cliente.GetAsync(Tablero.RutaGrilla(lote.Id, desde: 9), ct);
        incremental.StatusCode.Should().Be(HttpStatusCode.OK);
        var sinCambios = await Tablero.JsonAsync(incremental, ct);
        sinCambios.GetProperty("vehiculos").GetArrayLength().Should().Be(0);
        sinCambios.GetProperty("total_pegadas").GetInt32().Should().Be(2);
        sinCambios.GetProperty("vehiculos_pegados").GetArrayLength().Should().Be(2);
    }

    // Contrato: el DTO de la grilla lleva los campos nuevos con su nombre JSON.
    [Fact]
    public void El_contrato_de_la_grilla_lleva_pegada_y_total_pegadas()
    {
        Nombres(typeof(GrillaDto)).Should().Contain(["total_pegadas", "vehiculos_pegados"]);
        Nombres(typeof(FilaVehiculoDto)).Should().Contain("pegada");
    }

    private static Guid Vehiculo(LoteSembrado lote, string placa) => lote.Vehiculos.Single(v => v.IdentificadorNorm == placa).Id;

    private static IEnumerable<string> Nombres(Type tipo) =>
        tipo.GetProperties().Select(p => p.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
            .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>().Single().Name);
}
