using Flit.Consultas.Api.Impuesto;
using FluentAssertions;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto;

public sealed class HomologacionDocumentoImpuestoTests
{
    [Theory]
    [InlineData("76", "CC", "Cédula Ciudadanía")]
    [InlineData("05", "PAS", "PASAP")]
    [InlineData("13", "cc", "CC")]
    public void Homologa_el_par_conocido(string departamento, string tipo, string esperado) =>
        HomologacionDocumentoImpuesto.Homologar(departamento, tipo).Should().Be(esperado);

    [Theory]
    [InlineData("76", "RC")]
    [InlineData("05", "CD")]
    [InlineData("05", "XX")]
    [InlineData("76", "PS")]
    public void Un_par_sin_tabla_no_se_consulta(string departamento, string tipo) =>
        HomologacionDocumentoImpuesto.Homologar(departamento, tipo).Should().BeNull();
}
