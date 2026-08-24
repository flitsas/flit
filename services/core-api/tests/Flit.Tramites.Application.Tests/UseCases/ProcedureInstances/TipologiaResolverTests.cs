using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// ADR-0050 — la tipología dejó de ser un catálogo propio: es el <c>code</c> del tipo de
/// trámite (<c>MATRICULA_NUEVA</c> / <c>TRASPASO_STANDARD</c>). La modalidad todavía existe
/// como columna en retirada, así que el resolver sigue devolviéndola en snake_case.
/// </summary>
public sealed class TipologiaResolverTests
{
    [Theory]
    [InlineData(ProcedureFamilyCodes.Traspaso, "traspaso", "TRASPASO_STANDARD")]
    [InlineData("traspaso", "traspaso", "TRASPASO_STANDARD")]
    [InlineData(ProcedureFamilyCodes.Matriculas, "matricula_inicial", "MATRICULA_NUEVA")]
    [InlineData(ProcedureFamilyCodes.Otros, "matricula_inicial", "MATRICULA_NUEVA")]
    [InlineData("anything-else", "matricula_inicial", "MATRICULA_NUEVA")]
    [InlineData(null, "matricula_inicial", "MATRICULA_NUEVA")]
    public void FromFamily_MapsFamilyToModalidadAndTipologia(
        string? family, string expectedModalidad, string expectedTipologia)
    {
        var (modalidad, tipologia) = TipologiaResolver.FromFamily(family);

        modalidad.Should().Be(expectedModalidad);
        tipologia.Should().Be(expectedTipologia);
    }
}
