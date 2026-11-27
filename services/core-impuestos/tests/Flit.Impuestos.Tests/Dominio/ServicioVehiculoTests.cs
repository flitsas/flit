using Flit.Impuestos.Domain.Liquidaciones;
using FluentAssertions;
using Xunit;

namespace Flit.Impuestos.Tests.Dominio;

/// <summary>
/// HU #14694 (R3, decisión del usuario 2026-11-26) — un vehículo de servicio público no paga impuesto vehicular. El tipo de servicio
/// del RUNT (<c>tipo_servicio</c>) se normaliza (sin espacios a los lados, sin tildes, en mayúsculas) y solo <c>PUBLICO</c> es público.
/// </summary>
/// <example>
/// <code>
/// ServicioVehiculo.EsPublico(" Público "); // true
/// </code>
/// </example>
public sealed class ServicioVehiculoTests
{
    [Theory]
    [InlineData("Público", true)]
    [InlineData("PUBLICO", true)]
    [InlineData(" publico ", true)]
    [InlineData("PÚBLICO", true)]
    [InlineData("Particular", false)]
    [InlineData("PARTICULAR", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Oficial", false)]
    public void Solo_PUBLICO_normalizado_es_servicio_publico(string? tipoServicio, bool publico)
    {
        ServicioVehiculo.EsPublico(tipoServicio).Should().Be(publico);
    }
}
