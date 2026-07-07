using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

public sealed class TipologiaResolverTests
{
    [Theory]
    [InlineData(ProcedureFamily.Traspaso, "traspaso", "traspaso_standard")]
    [InlineData("traspaso", "traspaso", "traspaso_standard")]
    [InlineData(ProcedureFamily.Matriculas, "matricula_inicial", "matricula_inicial")]
    [InlineData(ProcedureFamily.Otros, "matricula_inicial", "matricula_inicial")]
    [InlineData("anything-else", "matricula_inicial", "matricula_inicial")]
    [InlineData(null, "matricula_inicial", "matricula_inicial")]
    public void FromFamily_MapsFamilyToModalidadAndTipologia(
        string? family, string expectedModalidad, string expectedTipologia)
    {
        var (modalidad, tipologia) = TipologiaResolver.FromFamily(family);

        modalidad.Should().Be(expectedModalidad);
        tipologia.Should().Be(expectedTipologia);
    }

    [Fact]
    public void ResolveCodigo_PrefersValidTipologiaCodigo()
    {
        TipologiaResolver.ResolveCodigo("traspaso_standard", "matricula_inicial")
            .Should().Be("traspaso_standard");
    }

    [Theory]
    // tipologia_codigo null → derivado de modalidad (default defensivo para instancias viejas)
    [InlineData(null, "matricula_inicial", "matricula_inicial")]
    [InlineData(null, "traspaso", "traspaso_standard")]
    [InlineData("", "traspaso", "traspaso_standard")]
    // tipologia_codigo inválido (no catálogo) → cae al de modalidad
    [InlineData("no_existe", "traspaso", "traspaso_standard")]
    public void ResolveCodigo_FallsBackToModalidadWhenTipologiaMissing(
        string? tipologiaCodigo, string? modalidad, string expected)
    {
        TipologiaResolver.ResolveCodigo(tipologiaCodigo, modalidad)
            .Should().Be(expected);
    }

    [Fact]
    public void ResolveCodigo_UnknownBoth_ReturnsNull()
    {
        TipologiaResolver.ResolveCodigo(null, "modalidad_desconocida")
            .Should().BeNull();
    }

    // ── HU #10590 — Traspaso unilateral ───────────────────────────────────────

    [Theory]
    // El code canónico del unilateral distingue por CODE (family=TRASPASO compartida con el estándar).
    [InlineData("TRASPASO_UNILATERAL", ProcedureFamily.Traspaso, "traspaso_unilateral", "traspaso_unilateral")]
    [InlineData("traspaso_unilateral", ProcedureFamily.Traspaso, "traspaso_unilateral", "traspaso_unilateral")] // case-insensitive
    // Sin el code unilateral, cae al mapeo por familia (comportamiento previo intacto).
    [InlineData("TRASPASO_STANDARD", ProcedureFamily.Traspaso, "traspaso", "traspaso_standard")]
    [InlineData(null, ProcedureFamily.Traspaso, "traspaso", "traspaso_standard")]
    [InlineData("MATRICULA_NUEVA", ProcedureFamily.Matriculas, "matricula_inicial", "matricula_inicial")]
    [InlineData("X", "anything-else", "matricula_inicial", "matricula_inicial")]
    public void FromType_DistinguishesUnilateralByCodeBeforeFamily(
        string? code, string? family, string expectedModalidad, string expectedTipologia)
    {
        var (modalidad, tipologia) = TipologiaResolver.FromType(code, family);

        modalidad.Should().Be(expectedModalidad);
        tipologia.Should().Be(expectedTipologia);
    }

    [Theory]
    // tipologia_codigo persistido válido tiene prioridad.
    [InlineData("traspaso_unilateral", "traspaso_unilateral", "traspaso_unilateral")]
    // tipologia_codigo null → derivado de la modalidad unilateral.
    [InlineData(null, "traspaso_unilateral", "traspaso_unilateral")]
    [InlineData("", "traspaso_unilateral", "traspaso_unilateral")]
    // tipologia_codigo inválido → cae al de la modalidad.
    [InlineData("no_existe", "traspaso_unilateral", "traspaso_unilateral")]
    public void ResolveCodigo_ResolvesTraspasoUnilateral(
        string? tipologiaCodigo, string? modalidad, string expected)
    {
        TipologiaResolver.ResolveCodigo(tipologiaCodigo, modalidad).Should().Be(expected);
    }
}
