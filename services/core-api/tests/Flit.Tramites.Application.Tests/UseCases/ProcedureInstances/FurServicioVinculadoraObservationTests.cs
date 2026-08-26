using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// El tipo de servicio y la empresa vinculadora en el recuadro de observaciones del FUR, por el
/// mismo canal automático que las transformaciones de ADR-0029.
///
/// <para>Los ejemplos son los MISMOS que usa <c>fur-auto-observations.test.ts</c> en el frontend, que
/// mantiene una copia de esta regla para previsualizarla en el paso de observaciones. Si una
/// redacción cambia aquí y no allá, ese test lo delata.</para>
/// </summary>
public sealed class FurServicioVinculadoraObservationTests
{
    [Fact]
    public void Compose_ConServicioRazonSocialYNit_ImprimeLosTres()
    {
        FurServicioVinculadoraObservation.Compose("PUBLICO", "TRANSPORTES SAS", "900123456")
            .Should().Be("Servicio: PÚBLICO. Empresa vinculadora: TRANSPORTES SAS, NIT 900123456.");
    }

    /// <summary>Sin NIT no quedan comas sueltas que delaten el campo vacío.</summary>
    [Fact]
    public void Compose_SinNit_ImprimeSoloLaRazonSocial()
    {
        var texto = FurServicioVinculadoraObservation.Compose("PUBLICO", "TRANSPORTES SAS", null);

        texto.Should().Be("Servicio: PÚBLICO. Empresa vinculadora: TRANSPORTES SAS.");
        texto.Should().NotContain("NIT");
        texto.Should().NotContain(",");
    }

    /// <summary>
    /// Sin empresa vinculadora no se imprime NADA, ni siquiera el servicio: ese ya tiene su casilla
    /// propia en el FUR y repetirlo solo gastaría renglones del recuadro.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_SinRazonSocial_NoImprimeNada(string? razonSocial)
    {
        FurServicioVinculadoraObservation.Compose("PARTICULAR", razonSocial, "900123456")
            .Should().BeNull();
    }

    /// <summary>Con vinculadora pero sin código de servicio se declara lo que sí se sabe.</summary>
    [Fact]
    public void Compose_SinCodigoDeServicio_DeclaraSoloLaEmpresa()
    {
        FurServicioVinculadoraObservation.Compose(null, "TRANSPORTES SAS", "900123456")
            .Should().Be("Empresa vinculadora: TRANSPORTES SAS, NIT 900123456.");
    }

    /// <summary>Los códigos cerrados salen con su nombre legible, tildes incluidas.</summary>
    [Theory]
    [InlineData("PUBLICO", "PÚBLICO")]
    [InlineData("publico", "PÚBLICO")]
    [InlineData("DIPLOMATICO", "DIPLOMÁTICO")]
    [InlineData("PARTICULAR", "PARTICULAR")]
    [InlineData("OFICIAL", "OFICIAL")]
    [InlineData("ESPECIAL", "ESPECIAL")]
    [InlineData("OTROS", "OTROS")]
    public void Compose_TraduceLosCodigosCerrados(string code, string esperado)
    {
        FurServicioVinculadoraObservation.Compose(code, "TRANSPORTES SAS", null)
            .Should().StartWith($"Servicio: {esperado}.");
    }

    /// <summary>
    /// `vehicle_service` también carga el texto libre que hidrata el RUNT. Un valor fuera del catálogo
    /// se imprime en mayúsculas antes que perderse: el dato sigue siendo real y el recuadro informativo.
    /// </summary>
    [Fact]
    public void Compose_CodigoDesconocido_NoLoPierde()
    {
        FurServicioVinculadoraObservation.Compose("carga pesada", "TRANSPORTES SAS", null)
            .Should().StartWith("Servicio: CARGA PESADA.");
    }
}
