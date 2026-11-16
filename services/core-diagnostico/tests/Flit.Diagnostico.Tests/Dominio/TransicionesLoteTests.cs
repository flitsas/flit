using Flit.Diagnostico.Domain.Diagnosticos;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Dominio;

/// <summary>
/// HU #13727 (F03-H2) — máquina de estados del lote en el dominio: <c>CREADO → EN_PROCESO</c> con disparador AUTO (al encolar),
/// y lo que no está en la tabla se rechaza.
/// </summary>
/// <example>
/// <code>
/// var t = TransicionesLote.Transicionar(EstadoDiagnostico.Creado, EstadoDiagnostico.EnProceso, DisparadorEstado.Auto);
/// </code>
/// </example>
public sealed class TransicionesLoteTests
{
    // AC1 (F03-C1) — el lote pasa a EN_PROCESO y la transición queda lista para el historial con disparador AUTO.
    [Fact]
    public void Creado_pasa_a_EnProceso_y_registra_historial_AUTO()
    {
        var transicion = TransicionesLote.Transicionar(EstadoDiagnostico.Creado, EstadoDiagnostico.EnProceso, DisparadorEstado.Auto);

        transicion.Should().Be(new TransicionLote(EstadoDiagnostico.Creado, EstadoDiagnostico.EnProceso, DisparadorEstado.Auto));
        TransicionesLote.Permitida(EstadoDiagnostico.Creado, EstadoDiagnostico.EnProceso, DisparadorEstado.Auto).Should().BeTrue();
    }

    // HU #13746 (F03-H8, R-8) — un lote cerrado (COMPLETADO o INCOMPLETO) vuelve a EN_PROCESO al reprocesar, con disparador
    // REPROCESO; por otro disparador no se reabre.
    [Theory]
    [InlineData(EstadoDiagnostico.Completado)]
    [InlineData(EstadoDiagnostico.Incompleto)]
    public void Completado_e_Incompleto_pasan_a_EnProceso_por_reproceso(EstadoDiagnostico cerrado)
    {
        var transicion = TransicionesLote.Transicionar(cerrado, EstadoDiagnostico.EnProceso, DisparadorEstado.Reproceso);

        transicion.Should().Be(new TransicionLote(cerrado, EstadoDiagnostico.EnProceso, DisparadorEstado.Reproceso));
        TransicionesLote.Permitida(cerrado, EstadoDiagnostico.EnProceso, DisparadorEstado.Auto).Should().BeFalse("solo el reproceso (o el llenado manual, D3) reabre un lote cerrado");
        TransicionesLote.Permitida(cerrado, EstadoDiagnostico.EnProceso, DisparadorEstado.CierrePanel).Should().BeFalse();
    }

    // Lo que la tabla no tiene se rechaza: volver a CREADO, saltar a COMPLETADO, otro disparador o quedarse donde está.
    [Theory]
    [InlineData(EstadoDiagnostico.EnProceso, EstadoDiagnostico.EnProceso, DisparadorEstado.Reproceso)]
    [InlineData(EstadoDiagnostico.Creado, EstadoDiagnostico.EnProceso, DisparadorEstado.Reproceso)]
    [InlineData(EstadoDiagnostico.EnProceso, EstadoDiagnostico.Creado, DisparadorEstado.Auto)]
    [InlineData(EstadoDiagnostico.Creado, EstadoDiagnostico.Completado, DisparadorEstado.Auto)]
    [InlineData(EstadoDiagnostico.Creado, EstadoDiagnostico.EnProceso, DisparadorEstado.Manual)]
    [InlineData(EstadoDiagnostico.EnProceso, EstadoDiagnostico.EnProceso, DisparadorEstado.Auto)]
    public void Transicion_fuera_de_la_tabla_se_rechaza(EstadoDiagnostico de, EstadoDiagnostico a, DisparadorEstado por)
    {
        TransicionesLote.Permitida(de, a, por).Should().BeFalse();
        var transicionar = () => TransicionesLote.Transicionar(de, a, por);
        transicionar.Should().Throw<InvalidOperationException>();
    }
}
