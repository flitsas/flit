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

    // Lo que la tabla no tiene se rechaza: volver a CREADO, saltar a COMPLETADO, otro disparador o quedarse donde está.
    [Theory]
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
