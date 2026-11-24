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

    /// <summary>Los 33 códigos DANE (32 departamentos y Bogotá D.C.), decisión P5' del 2026-11-23.</summary>
    public static readonly string[] CodigosDane =
    [
        "05", "08", "11", "13", "15", "17", "18", "19", "20", "23", "25", "27", "41", "44", "47", "50", "52",
        "54", "63", "66", "68", "70", "73", "76", "81", "85", "86", "88", "91", "94", "95", "97", "99",
    ];

    public static TheoryData<string> Dane => new(CodigosDane);

    [Theory]
    [MemberData(nameof(Dane))]
    public void Todo_codigo_DANE_es_conocido_para_el_catalogo(string codigo) =>
        HomologacionDocumentoImpuesto.Conocido(codigo).Should().BeTrue();

    [Theory]
    [InlineData("00")]
    [InlineData("12")]
    [InlineData("98")]
    [InlineData("5")]
    [InlineData("")]
    public void Un_codigo_que_no_es_DANE_no_es_conocido(string codigo) =>
        HomologacionDocumentoImpuesto.Conocido(codigo).Should().BeFalse();
}
