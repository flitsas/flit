using Flit.Tramites.Domain.RuntConfirmation;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Domain.Tests.RuntConfirmation;

/// <summary>HU #12312 AC1/AC2 — la columna del gestor solo dice SÍ / NO / — y solo en aprobados.</summary>
public sealed class RuntConfirmedColumnTests
{
    [Fact]
    public void Aprobado_Confirmado_EsYes() =>
        RuntConfirmedColumn.Derive("aprobado", DateTimeOffset.UtcNow, 3, null).Should().Be("yes");

    [Theory]
    [InlineData(1, null)]
    [InlineData(3, "discrepancia")]
    [InlineData(10, "tope")]
    [InlineData(1, "no_verificable")]
    public void Aprobado_ConIntentosOMarca_EsNo_SinDistinguir(int attempts, string? flag) =>
        RuntConfirmedColumn.Derive("aprobado", null, attempts, flag).Should().Be("no");

    [Fact]
    public void Aprobado_SinIntentos_EsNotConsulted() =>
        RuntConfirmedColumn.Derive("aprobado", null, 0, null).Should().Be("not_consulted");

    [Theory]
    [InlineData("borrador")]
    [InlineData("entregado")]
    [InlineData("rechazado")]
    public void NoAprobado_EsNulo(string status) =>
        RuntConfirmedColumn.Derive(status, null, 2, "discrepancia").Should().BeNull();
}
