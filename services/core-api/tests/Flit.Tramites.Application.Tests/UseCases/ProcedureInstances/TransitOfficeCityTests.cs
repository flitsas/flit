using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// HU #11016 — la ciudad del organismo que llega en <c>transit_office_city</c> es el CÓDIGO DIVIPOLA
/// del municipio, no su nombre. Imprimirlo hacía que la solicitud de trámite virtual mostrara
/// «25286, 28 de julio de 2026», con el código aparentemente concatenado a la fecha.
/// </summary>
public sealed class TransitOfficeCityTests
{
    [Theory]
    [InlineData("25286")]   // el caso reportado
    [InlineData("05001")]   // con cero a la izquierda
    [InlineData(" 11001 ")] // con espacios alrededor
    public void Legible_DescartaCodigosDivipola(string codigo)
    {
        TransitOfficeCity.Legible(codigo).Should().BeNull();
    }

    [Theory]
    [InlineData("Sabaneta")]
    [InlineData("Bogotá D.C.")]
    [InlineData("San José del Guaviare")]
    public void Legible_ConservaNombresDeCiudad(string nombre)
    {
        TransitOfficeCity.Legible(nombre).Should().Be(nombre);
    }

    [Fact]
    public void Legible_RecortaEspacios()
    {
        TransitOfficeCity.Legible("  Medellín  ").Should().Be("Medellín");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Legible_SinValor_DevuelveNull(string? valor)
    {
        TransitOfficeCity.Legible(valor).Should().BeNull();
    }

    [Theory]
    [InlineData("Secretaría de Movilidad de Medellín", "Medellín")]
    [InlineData("Tránsito de Envigado", "Envigado")]
    [InlineData("SECRETARIA DE MOVILIDAD DE CHIA", "CHIA")]
    [InlineData("Medellín — Secretaría de Movilidad", "Medellín")]
    [InlineData("Cali - STTMP", "Cali")]
    public void ForDocuments_ConCodigoDivipola_InfiereCiudadDelNombreDelOt(string nombreOt, string ciudad)
    {
        TransitOfficeCity.ForDocuments("05001", nombreOt).Should().Be(ciudad);
    }

    [Fact]
    public void ForDocuments_ConNombreLegible_NoPisaConInferencia()
    {
        TransitOfficeCity.ForDocuments("Bogotá D.C.", "Secretaría de Movilidad de Medellín")
            .Should().Be("Bogotá D.C.");
    }

    [Fact]
    public void ForDocuments_ConCityNameLegible_PriorizaSobreCodigoDivipola()
    {
        TransitOfficeCity.ForDocuments("FUNZA", "STRIA TTEyTTO MCPAL")
            .Should().Be("FUNZA");
    }

    [Fact]
    public void ForDocuments_SinCiudadEnNombre_NoInventa()
    {
        TransitOfficeCity.ForDocuments("25286", "STRIA TTEyTTO MCPAL").Should().BeNull();
    }
}
