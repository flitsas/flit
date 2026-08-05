using FluentAssertions;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// HU #11257 (Feature #11254) — <see cref="PrendaDecision.ToFurMarking"/>: traduce la decisión de
/// prenda al valor semántico que consume el generador del FUR. A diferencia de
/// <see cref="PrendaDecision.ImplicaGravamen"/> (que colapsa <c>levantar</c> al mismo resultado que
/// "sin prenda"), <c>ToFurMarking</c> distingue las tres modalidades: constitución, levantamiento y
/// ninguna.
/// </summary>
public sealed class PrendaDecisionFurMarkingTests
{
    [Theory]
    [InlineData("solicitar", FurPrendaMarking.Constitucion)]
    [InlineData("registrar", FurPrendaMarking.Constitucion)]
    [InlineData("SOLICITAR", FurPrendaMarking.Constitucion)] // case-insensitive
    [InlineData("REGISTRAR", FurPrendaMarking.Constitucion)]
    [InlineData("levantar", FurPrendaMarking.Levantamiento)]
    [InlineData("LEVANTAR", FurPrendaMarking.Levantamiento)]
    [InlineData("omitir", FurPrendaMarking.Ninguna)]
    [InlineData("sin_prenda", FurPrendaMarking.Ninguna)]
    [InlineData(null, FurPrendaMarking.Ninguna)]
    [InlineData("", FurPrendaMarking.Ninguna)]
    [InlineData("valor_desconocido", FurPrendaMarking.Ninguna)]
    public void ToFurMarking_traduce_la_decision_a_la_marca_del_FUR(string? decision, FurPrendaMarking esperado)
    {
        PrendaDecision.ToFurMarking(decision).Should().Be(esperado);
    }

    [Fact]
    public void ToFurMarking_distingue_levantar_de_sin_prenda()
    {
        // Antes de la HU #11257, ImplicaGravamen(levantar) == ImplicaGravamen(sin_prenda) == false: la
        // modalidad se perdía. ToFurMarking las distingue.
        PrendaDecision.ToFurMarking(PrendaDecision.Levantar).Should().Be(FurPrendaMarking.Levantamiento);
        PrendaDecision.ToFurMarking(PrendaDecision.SinPrenda).Should().Be(FurPrendaMarking.Ninguna);
        PrendaDecision.ToFurMarking(PrendaDecision.Levantar)
            .Should().NotBe(PrendaDecision.ToFurMarking(PrendaDecision.SinPrenda));
    }

    [Fact]
    public void ImplicaGravamen_no_se_toca_por_esta_HU()
    {
        // Restricción del plan: ImplicaGravamen sigue sirviendo a otros consumidores con su semántica
        // original (presencia de gravamen), no la modalidad de marcación del FUR.
        PrendaDecision.ImplicaGravamen(PrendaDecision.Levantar).Should().BeFalse();
        PrendaDecision.ImplicaGravamen(PrendaDecision.Solicitar).Should().BeTrue();
    }
}
