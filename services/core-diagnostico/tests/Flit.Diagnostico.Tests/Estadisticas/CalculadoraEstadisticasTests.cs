using Flit.Diagnostico.Api.Modulos.Estadisticas;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Derivacion;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Saneamiento;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Estadisticas;

/// <summary>
/// HU #14579, BE-1 (contrato-api §1, diseno §dashboard criterio S1): armado del <c>EstadisticasDto</c> a partir de las lecturas ya
/// agregadas por la base. Granularidad automática (DIA ≤ 62 días, SEMANA desde el lunes ≤ 183, si no MES) con cubetas en cero,
/// tasa de éxito con 4 decimales o null sin divisor, tiempo promedio sobre los intentos no manuales (la base ya los excluye),
/// fuentes en el orden del enum y solo con celdas, los 5 estados de saneamiento con ceros, veredictos y top de empresas. Sin base.
/// </summary>
/// <example>
/// <code>
/// var dto = CalculadoraEstadisticas.Armar(ambito, filtro, ahora, lecturas, top: []);
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~CalculadoraEstadisticasTests
/// </code>
/// </example>
public sealed class CalculadoraEstadisticasTests
{
    private static readonly TimeSpan Colombia = TimeSpan.FromHours(-5);
    private static readonly Guid EmpresaA = Guid.Parse("0192f1a2-0000-7000-8000-00000000000a");
    private static readonly Guid EmpresaB = Guid.Parse("0192f1a2-0000-7000-8000-00000000000b");
    private static readonly AmbitoEstadisticasDto Ambito = new("EMPRESA", EmpresaA, "Renting Ficticio S.A.S.");

    [Theory]
    [InlineData(1, Granularidad.Dia)]
    [InlineData(62, Granularidad.Dia)]
    [InlineData(63, Granularidad.Semana)]
    [InlineData(183, Granularidad.Semana)]
    [InlineData(184, Granularidad.Mes)]
    [InlineData(366, Granularidad.Mes)]
    public void BE1_granularidad_por_dias_del_rango(int dias, Granularidad esperada) =>
        Cubetas.De(dias).Should().Be(esperada);

    [Fact]
    public void BE1_cubetas_semanales_empiezan_en_lunes_y_mensuales_el_dia_1()
    {
        // 2026-11-15 es domingo: su semana empieza el lunes 2026-11-09.
        Cubetas.Inicio(Granularidad.Semana, new DateOnly(2026, 11, 15)).Should().Be(new DateOnly(2026, 11, 9));
        Cubetas.Inicio(Granularidad.Semana, new DateOnly(2026, 11, 9)).Should().Be(new DateOnly(2026, 11, 9));
        Cubetas.Inicio(Granularidad.Mes, new DateOnly(2026, 11, 15)).Should().Be(new DateOnly(2026, 11, 1));
        Cubetas.Del(Granularidad.Semana, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 15))
            .Should().Equal(new DateOnly(2026, 10, 26), new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9));
        Cubetas.Del(Granularidad.Mes, new DateOnly(2025, 11, 20), new DateOnly(2026, 2, 3))
            .Should().Equal(new DateOnly(2025, 11, 1), new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));
    }

    // AC «indicadores de la compañía en el periodo» — resumen, serie diaria con ceros, tipos de análisis y los 5 estados con ceros.
    [Fact]
    public void BE1_resumen_y_serie_diaria_con_ceros_en_los_dias_sin_lotes()
    {
        var filtro = new FiltroEstadisticas(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 14));
        var lecturas = Lecturas(
            lotes:
            [
                // 23:30 del 10 en Colombia es el 11 en UTC: cuenta en el día 10.
                new LoteLeido(EmpresaA, new DateTimeOffset(2026, 11, 10, 23, 30, 0, Colombia), true, ModoDiagnostico.Simple, 1),
                new LoteLeido(EmpresaA, new DateTimeOffset(2026, 11, 12, 9, 0, 0, Colombia), true, ModoDiagnostico.ConAnalisis, 1),
                new LoteLeido(EmpresaA, new DateTimeOffset(2026, 11, 12, 18, 0, 0, Colombia), false, ModoDiagnostico.Simple, 5),
            ],
            vehiculosUnicos: 6,
            saneamientos: [new ConteoSaneamiento(EmpresaA, EstadoSaneamiento.EnGestion, 2)]);

        var dto = CalculadoraEstadisticas.Armar(Ambito, filtro, Ahora, lecturas, []);

        dto.Ambito.Should().Be(Ambito);
        dto.Periodo.Should().Be(new PeriodoEstadisticasDto(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 14), "DIA"));
        dto.GeneradoEn.Offset.Should().Be(Colombia, "ISO 8601 con offset -05:00");
        dto.Resumen.Diagnosticos.Should().Be(3);
        dto.Resumen.DiagnosticosExpress.Should().Be(2);
        dto.Resumen.DiagnosticosMasivos.Should().Be(1);
        dto.Resumen.Vehiculos.Should().Be(7);
        dto.Resumen.VehiculosUnicos.Should().Be(6);
        dto.Resumen.SaneamientosSolicitados.Should().Be(2);
        dto.Serie.Select(c => c.Inicio).Should().Equal(
            new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 12), new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 14));
        dto.Serie.Select(c => (c.Diagnosticos, c.Express, c.Masivos, c.Vehiculos)).Should().Equal(
            (1, 1, 0, 1), (0, 0, 0, 0), (2, 1, 1, 6), (0, 0, 0, 0), (0, 0, 0, 0));
        dto.TiposAnalisis.Should().Be(new TiposAnalisisDto(2, 1));
        dto.Saneamientos.Select(s => (s.Estado, s.Cantidad)).Should().Equal(
            ("SOLICITADO", 0), ("EN_GESTION", 2), ("SANEADO", 0), ("NO_PROCEDE", 0), ("ANULADO", 0));
        dto.TopEmpresas.Should().BeEmpty();
    }

    // AC «tiempos sin intentos manuales y tasa de éxito» — tasa con 4 decimales; null sin divisor; tiempo promedio null sin intentos.
    [Fact]
    public void BE1_fuentes_en_orden_del_enum_con_tasa_y_tiempo()
    {
        var filtro = new FiltroEstadisticas(new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 14));
        var lecturas = Lecturas(
            celdas:
            [
                new ConteoCelda(Fuente.SimitVehiculo, EstadoCelda.Na, 2),
                new ConteoCelda(Fuente.Runt, EstadoCelda.Completado, 2),
                new ConteoCelda(Fuente.Runt, EstadoCelda.Error, 1),
                new ConteoCelda(Fuente.Runt, EstadoCelda.Pendiente, 1),
                new ConteoCelda(Fuente.Runt, EstadoCelda.EnCurso, 1),
            ],
            tiempos: [new TiempoFuente(Fuente.Runt, 4000, 2)]);

        var dto = CalculadoraEstadisticas.Armar(Ambito, filtro, Ahora, lecturas, []);

        dto.Fuentes.Select(f => f.Fuente).Should().Equal("RUNT", "SIMIT_VEHICULO");
        var runt = dto.Fuentes[0];
        (runt.Consultas, runt.Exitosas, runt.ConError, runt.NoAplica, runt.Pendientes).Should().Be((5, 2, 1, 0, 2));
        runt.TasaExito.Should().Be(0.6667);
        runt.TiempoPromedioMs.Should().Be(2000);
        var simit = dto.Fuentes[1];
        simit.TasaExito.Should().BeNull("sin exitosas ni con error no hay divisor");
        simit.TiempoPromedioMs.Should().BeNull();
        dto.Resumen.Consultas.Should().Be(7);
        dto.Resumen.ConsultasExitosas.Should().Be(2);
        dto.Resumen.ConsultasConError.Should().Be(1);
        dto.Resumen.ConsultasNoAplica.Should().Be(2);
        dto.Resumen.ConsultasPendientes.Should().Be(2);
        dto.Resumen.TasaExito.Should().Be(0.6667);
        dto.Resumen.TiempoPromedioConsultaMs.Should().Be(2000);
    }

    [Theory]
    [InlineData(0, 0, null)]
    [InlineData(1, 0, 1.0)]
    [InlineData(0, 3, 0.0)]
    [InlineData(2, 1, 0.6667)]
    [InlineData(10500, 300, 0.9722)]
    public void BE1_tasa_de_exito_con_4_decimales_o_null(int exitosas, int conError, double? esperada) =>
        CalculadoraEstadisticas.TasaExito(exitosas, conError).Should().Be(esperada);

    [Fact]
    public void BE1_sin_datos_todo_en_cero_y_promedios_null()
    {
        var dto = CalculadoraEstadisticas.Armar(Ambito, new FiltroEstadisticas(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2)), Ahora, Lecturas(), []);

        dto.Resumen.Diagnosticos.Should().Be(0);
        dto.Resumen.TasaExito.Should().BeNull();
        dto.Resumen.TiempoPromedioConsultaMs.Should().BeNull();
        dto.Serie.Should().HaveCount(2).And.OnlyContain(c => c.Diagnosticos == 0 && c.Vehiculos == 0);
        dto.Fuentes.Should().BeEmpty();
        dto.Saneamientos.Should().HaveCount(5).And.OnlyContain(s => s.Cantidad == 0);
        dto.Veredictos.Should().Be(new VeredictosDto(0, 0, 0));
        dto.Derivaciones.Should().Be(new DerivacionesEstadisticasDto(0, 0, 0, 0));
    }

    // Veredictos del dictamen vigente y derivaciones; tramites_creados = ítems CREADO; en_curso = PENDIENTE + ENVIADO.
    [Fact]
    public void BE1_veredictos_y_derivaciones()
    {
        var lecturas = Lecturas(
            veredictos: [new ConteoVeredicto("VIABLE", 4), new ConteoVeredicto("BLOQUEADO", 2), new ConteoVeredicto(null, 3)],
            operaciones: [new ConteoOperaciones(EmpresaA, 5, 2, 1), new ConteoOperaciones(EmpresaB, 4, 0, 0)],
            items:
            [
                new ConteoItems(EstadoItemDerivacion.Creado, 9), new ConteoItems(EstadoItemDerivacion.Pendiente, 2),
                new ConteoItems(EstadoItemDerivacion.Enviado, 1), new ConteoItems(EstadoItemDerivacion.Omitido, 2),
            ]);

        var dto = CalculadoraEstadisticas.Armar(Ambito, new FiltroEstadisticas(new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 14)), Ahora, lecturas, []);

        dto.Veredictos.Should().Be(new VeredictosDto(4, 2, 3));
        dto.Derivaciones.Should().Be(new DerivacionesEstadisticasDto(9, 2, 1, 3));
        dto.Resumen.TramitesCreados.Should().Be(9);
    }

    // Top: solo compañías con nombre (habilitadas), por diagnósticos desc, vehículos desc y nombre; hasta 10.
    [Fact]
    public void BE1_top_empresas_ordenado_solo_habilitadas_y_hasta_10()
    {
        var empresas = Enumerable.Range(1, 13).Select(i => Guid.Parse($"0192f1a2-0000-7000-8000-{i:000000000000}")).ToList();
        var apagada = Guid.NewGuid();
        var lotes = new List<LoteLeido>();
        for (var i = 0; i < empresas.Count; i++)
        {
            for (var n = 0; n <= i % 4; n++)
                lotes.Add(new LoteLeido(empresas[i], Ahora, false, ModoDiagnostico.Simple, i));
        }

        lotes.AddRange(Enumerable.Range(0, 20).Select(_ => new LoteLeido(apagada, Ahora, false, ModoDiagnostico.Simple, 1)));
        var nombres = empresas.Select((e, i) => (e, $"Compañía {(char)('A' + (12 - i))}")).ToDictionary(x => x.e, x => x.Item2);

        var top = CalculadoraEstadisticas.Top(Lecturas(lotes: lotes), nombres);

        top.Should().HaveCount(10);
        top.Select(t => t.EmpresaId).Should().NotContain(apagada, "una compañía apagada no aparece en el top");
        top.Should().BeInDescendingOrder(t => t.Diagnosticos);
        top[0].Diagnosticos.Should().Be(4);
        top.Where(t => t.Diagnosticos == 4).Should().BeInDescendingOrder(t => t.Vehiculos);
        top.Should().OnlyContain(t => !string.IsNullOrEmpty(t.EmpresaNombre));
    }

    [Fact]
    public void BE1_empresas_con_datos_suma_lotes_saneamientos_y_derivaciones()
    {
        var c = Guid.NewGuid();
        var lecturas = Lecturas(
            lotes: [new LoteLeido(EmpresaA, Ahora, false, ModoDiagnostico.Simple, 1)],
            saneamientos: [new ConteoSaneamiento(EmpresaB, EstadoSaneamiento.Solicitado, 1)],
            operaciones: [new ConteoOperaciones(c, 1, 0, 0)]);

        lecturas.EmpresasConDatos.Should().BeEquivalentTo([EmpresaA, EmpresaB, c]);
    }

    private static DateTimeOffset Ahora => new(2026, 11, 15, 10, 0, 0, Colombia);

    private static LecturasEstadisticas Lecturas(
        IReadOnlyList<LoteLeido>? lotes = null, int vehiculosUnicos = 0, IReadOnlyList<ConteoCelda>? celdas = null,
        IReadOnlyList<TiempoFuente>? tiempos = null, IReadOnlyList<ConteoVeredicto>? veredictos = null,
        IReadOnlyList<ConteoSaneamiento>? saneamientos = null, IReadOnlyList<ConteoOperaciones>? operaciones = null,
        IReadOnlyList<ConteoItems>? items = null) =>
        new(lotes ?? [], vehiculosUnicos, celdas ?? [], tiempos ?? [], veredictos ?? [], saneamientos ?? [], operaciones ?? [], items ?? []);
}
