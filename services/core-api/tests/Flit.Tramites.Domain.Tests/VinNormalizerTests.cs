using FluentAssertions;
using Flit.Tramites.Domain.Tramites.Services;
using Xunit;

namespace Flit.Tramites.Domain.Tests;

/// <summary>
/// Uso de ejemplo: <c>VinNormalizer.Normalize("8ab 12-3")</c> → <c>"8AB123"</c>.
/// Cubre la normalización del VIN (HU #10538, R3) usada para la invariante "un VIN → una matrícula".
/// </summary>
public sealed class VinNormalizerTests
{
    [Fact]
    public void Normalize_MayusculasYSinSeparadores_HappyPath()
    {
        // Happy path: mayúsculas + eliminación de espacios/guiones → clave canónica comparable.
        VinNormalizer.Normalize("8ab 12-3").Should().Be("8AB123");
        VinNormalizer.Normalize("1hgcm82633a004352").Should().Be("1HGCM82633A004352");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("   -  ")]
    public void Normalize_VacioONulo_RetornaNull(string? raw)
    {
        // Edge case: entrada vacía o que queda vacía tras quitar separadores → null (sin lanzar).
        VinNormalizer.Normalize(raw).Should().BeNull();
    }

    [Fact]
    public void Normalize_Idempotente_YPreservaLetrasAmbiguas()
    {
        // Contrato: es idempotente y NO elimina letras (no aplica la exclusión ISO I/O/Q, que
        // corrompería VINs de proveedores que no la respetan).
        var once = VinNormalizer.Normalize(" iOq-123 ");
        once.Should().Be("IOQ123");
        VinNormalizer.Normalize(once).Should().Be(once);
    }
}
