using Flit.Comparendos.Domain.Dp;
using Flit.Comparendos.Domain.Modelo;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Dominio;

/// <summary>HU #14618: persona jurídica cuando el tipo de documento viene vacío — una sola regla, con evidencia.</summary>
public sealed class PropietarioJuridicoTests
{
    [Theory]
    [InlineData(TipoDocumento.NIT, "900123456", "900123456", true)]
    [InlineData(TipoDocumento.NIT, "900123456", null, true)]
    [InlineData(TipoDocumento.NIT, null, null, true)]
    [InlineData(null, "890903938", "890903938", true)]
    [InlineData(null, "8909039388", "890903938", true)]
    [InlineData(null, "890.903.938-8", "890903938", true)]
    [InlineData(null, "0890903938", "890903938", true)]
    [InlineData(null, "123456789", "890903938", false)]
    [InlineData(null, null, "890903938", false)]
    [InlineData(null, "", "890903938", false)]
    [InlineData(null, "890903938", null, false)]
    [InlineData(null, "890903938", "", false)]
    [InlineData(null, null, null, false)]
    [InlineData(TipoDocumento.CC, "890903938", "890903938", false)]
    [InlineData(TipoDocumento.CC, "1020304050", null, false)]
    public void Es_persona_juridica_solo_con_NIT_o_con_tipo_vacio_y_el_NIT_consultado(TipoDocumento? tipo, string? numero, string? nitConsultado, bool esperado) =>
        PropietarioJuridico.Es(tipo, numero, nitConsultado).Should().Be(esperado);

    [Fact]
    public void Un_nit_demasiado_largo_no_revienta_el_digito_de_verificacion() =>
        PropietarioJuridico.Es(null, "1234567890123456789", "1234567890123456").Should().BeFalse();
}
