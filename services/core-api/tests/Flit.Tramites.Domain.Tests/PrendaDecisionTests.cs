using FluentAssertions;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

// HU #10594 (IT-3) — value objects del dominio de prenda: conjunto cerrado de decisiones,
// documentos exigidos por decisión y marca de gravamen para el FUR.
public sealed class PrendaDecisionTests
{
    [Theory]
    [InlineData("solicitar", true)]
    [InlineData("registrar", true)]
    [InlineData("levantar", true)]
    [InlineData("omitir", true)]
    [InlineData("sin_prenda", true)]
    [InlineData("SOLICITAR", true)] // case-insensitive
    [InlineData("otra_cosa", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_reconoce_solo_el_conjunto_cerrado(string? decision, bool esperado)
    {
        PrendaDecision.IsValid(decision).Should().Be(esperado);
    }

    [Theory]
    [InlineData("solicitar", true)]
    [InlineData("registrar", true)]
    [InlineData("levantar", true)]
    [InlineData("omitir", false)]
    [InlineData("sin_prenda", false)]
    public void RequiereDocumento_solo_para_gestiones_con_soporte(string decision, bool esperado)
    {
        PrendaDecision.RequiereDocumento(decision).Should().Be(esperado);
    }

    [Theory]
    [InlineData("solicitar", true)]
    [InlineData("registrar", true)]
    [InlineData("levantar", false)]
    [InlineData("omitir", false)]
    [InlineData("sin_prenda", false)]
    public void ImplicaGravamen_marca_el_FUR_solo_cuando_hay_prenda(string decision, bool esperado)
    {
        PrendaDecision.ImplicaGravamen(decision).Should().Be(esperado);
    }

    [Theory]
    [InlineData("solicitar", "prenda_solicitud")]
    [InlineData("registrar", "prenda_registro")]
    [InlineData("levantar", "prenda_levantamiento")]
    [InlineData("omitir", null)]
    [InlineData("sin_prenda", null)]
    public void DocTipoFor_mapea_cada_decision_a_su_documento(string decision, string? esperado)
    {
        PrendaDecision.DocTipoFor(decision).Should().Be(esperado);
    }

    [Fact]
    public void DocTipos_de_prenda_son_los_tres_esperados()
    {
        PrendaDocTipos.All.Should().BeEquivalentTo(
            new[] { "prenda_solicitud", "prenda_registro", "prenda_levantamiento" });
    }
}
