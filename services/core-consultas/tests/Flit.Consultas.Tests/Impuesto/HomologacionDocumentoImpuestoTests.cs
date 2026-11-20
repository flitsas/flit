using Flit.Consultas.Api.Impuesto;
using FluentAssertions;
using Xunit;

namespace Flit.Consultas.Tests.Impuesto;

/// <summary>
/// Diseño H10 — la fuente nueva acepta CC, NIT, CE, PA, TI y CD en todos los departamentos: una sola tabla. Diagnóstico manda
/// <c>PAS</c> y la fuente lo escribe <c>PA</c>; lo demás (RC, nombres largos de Valle) no se consulta.
/// <para>Uso de ejemplo: <c>HomologacionDocumentoImpuesto.Homologar("PAS") // "PA"</c>.</para>
/// </summary>
public sealed class HomologacionDocumentoImpuestoTests
{
    [Theory]
    [InlineData("CC", "CC")]
    [InlineData("cc", "CC")]
    [InlineData(" NIT ", "NIT")]
    [InlineData("CE", "CE")]
    [InlineData("PAS", "PA")]
    [InlineData("TI", "TI")]
    [InlineData("CD", "CD")]
    public void Homologa_a_la_tabla_unica(string tipo, string esperado) =>
        HomologacionDocumentoImpuesto.Homologar(tipo).Should().Be(esperado);

    [Theory]
    [InlineData("RC")]
    [InlineData("PASAP")]
    [InlineData("Cédula Ciudadanía")]
    [InlineData("XX")]
    [InlineData("")]
    [InlineData(null)]
    public void Lo_que_no_esta_en_la_tabla_no_se_consulta(string? tipo) =>
        HomologacionDocumentoImpuesto.Homologar(tipo).Should().BeNull();

    [Theory]
    [InlineData("05", true)]
    [InlineData("13", true)]
    [InlineData("17", true)]
    [InlineData("63", true)]
    [InlineData("68", true)]
    [InlineData("76", true)]
    [InlineData("08", true)]
    [InlineData("11", true)]
    [InlineData("99", false)]
    public void El_conjunto_conocido_se_conserva_para_el_catalogo(string codigo, bool conocido) =>
        HomologacionDocumentoImpuesto.Conocido(codigo).Should().Be(conocido);
}
