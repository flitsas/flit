using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13743 (F04-H1) — estado global del lote por tabla (plan §3.5, R-8): pendientes → EN_PROCESO; todo verde o NA →
/// COMPLETADO; ERROR sin cierre → EN_PROCESO; cierre con ERROR → INCOMPLETO. También el avance por vehículo (R-7), cuándo se
/// puede cerrar el panel y las transiciones nuevas de la máquina de estados.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EstadoGlobalLoteTests
/// </code>
/// </example>
public sealed class EstadoGlobalLoteTests
{
    private const EstadoCelda P = EstadoCelda.Pendiente;
    private const EstadoCelda C = EstadoCelda.EnCurso;
    private const EstadoCelda V = EstadoCelda.Completado;
    private const EstadoCelda E = EstadoCelda.Error;
    private const EstadoCelda N = EstadoCelda.Na;

    public static TheoryData<EstadoDiagnostico, EstadoCelda[], bool, EstadoDiagnostico> Tabla => new()
    {
        // pendientes → EN_PROCESO
        { EstadoDiagnostico.EnProceso, [P, V], false, EstadoDiagnostico.EnProceso },
        { EstadoDiagnostico.EnProceso, [C, N], false, EstadoDiagnostico.EnProceso },
        { EstadoDiagnostico.EnProceso, [], false, EstadoDiagnostico.EnProceso },
        // todo verde o NA → COMPLETADO (AC3)
        { EstadoDiagnostico.EnProceso, [V, N, V], false, EstadoDiagnostico.Completado },
        { EstadoDiagnostico.EnProceso, [N], false, EstadoDiagnostico.Completado },
        // ERROR sin cierre → EN_PROCESO (el lote no se cierra solo)
        { EstadoDiagnostico.EnProceso, [V, E, N], false, EstadoDiagnostico.EnProceso },
        // cierre con ERROR → INCOMPLETO (AC4); el cierre no adelanta lo pendiente
        { EstadoDiagnostico.EnProceso, [V, E], true, EstadoDiagnostico.Incompleto },
        { EstadoDiagnostico.EnProceso, [P, E], true, EstadoDiagnostico.EnProceso },
        { EstadoDiagnostico.EnProceso, [V, N], true, EstadoDiagnostico.Completado },
        // un lote que no está EN_PROCESO no cambia aquí
        { EstadoDiagnostico.Creado, [], false, EstadoDiagnostico.Creado },
        { EstadoDiagnostico.Completado, [V], false, EstadoDiagnostico.Completado },
        { EstadoDiagnostico.Incompleto, [E], true, EstadoDiagnostico.Incompleto },
    };

    [Theory]
    [MemberData(nameof(Tabla))]
    public void Estado_global_por_tabla(EstadoDiagnostico actual, EstadoCelda[] celdas, bool cierrePanel, EstadoDiagnostico esperado) =>
        EstadoGlobalLote.Calcular(actual, celdas, cierrePanel).Should().Be(esperado);

    public static TheoryData<EstadoDiagnostico, EstadoCelda[], ResultadoCierrePanel> Cierres => new()
    {
        { EstadoDiagnostico.EnProceso, [V, E, N], ResultadoCierrePanel.Permitido },
        { EstadoDiagnostico.EnProceso, [E, P], ResultadoCierrePanel.CeldasSinTerminar },
        { EstadoDiagnostico.EnProceso, [E, C], ResultadoCierrePanel.CeldasSinTerminar },
        { EstadoDiagnostico.EnProceso, [V, N], ResultadoCierrePanel.SinErrores },
        { EstadoDiagnostico.Completado, [V], ResultadoCierrePanel.LoteNoEnProceso },
        { EstadoDiagnostico.Incompleto, [E], ResultadoCierrePanel.LoteNoEnProceso },
        { EstadoDiagnostico.Creado, [], ResultadoCierrePanel.LoteNoEnProceso },
    };

    // AC4 (F04-C5) — solo se cierra un lote EN_PROCESO con todo terminado y al menos un ERROR.
    [Theory]
    [MemberData(nameof(Cierres))]
    public void Cierre_de_panel_por_tabla(EstadoDiagnostico actual, EstadoCelda[] celdas, ResultadoCierrePanel esperado) =>
        EstadoGlobalLote.EvaluarCierrePanel(actual, celdas).Should().Be(esperado);

    // AC2 (F04-C8, R-7) — el vehículo termina con todas sus celdas en COMPLETADO o NA, sin esperar al lote.
    [Theory]
    [InlineData(new[] { V, N }, true)]
    [InlineData(new[] { V }, true)]
    [InlineData(new[] { V, E }, false)]
    [InlineData(new[] { V, P }, false)]
    [InlineData(new[] { C }, false)]
    [InlineData(new EstadoCelda[0], false)]
    public void Vehiculo_terminado_con_todas_sus_celdas_en_verde_o_NA(EstadoCelda[] celdas, bool terminado) =>
        AvanceVehiculo.Terminado(celdas).Should().Be(terminado);

    // AC3 y AC4 — la máquina de estados admite el completado AUTO y el cierre de panel, y nada más nuevo.
    [Fact]
    public void Transiciones_de_completado_y_cierre_de_panel_estan_en_la_tabla()
    {
        TransicionesLote.Permitida(EstadoDiagnostico.EnProceso, EstadoDiagnostico.Completado, DisparadorEstado.Auto).Should().BeTrue();
        TransicionesLote.Permitida(EstadoDiagnostico.EnProceso, EstadoDiagnostico.Incompleto, DisparadorEstado.CierrePanel).Should().BeTrue();
        TransicionesLote.Permitida(EstadoDiagnostico.EnProceso, EstadoDiagnostico.Incompleto, DisparadorEstado.Auto).Should().BeFalse("el lote con ERROR no se cierra solo");
        TransicionesLote.Permitida(EstadoDiagnostico.EnProceso, EstadoDiagnostico.Completado, DisparadorEstado.CierrePanel).Should().BeFalse();
    }
}
