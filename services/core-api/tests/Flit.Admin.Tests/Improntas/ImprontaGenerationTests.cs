using Flit.Admin.Domain.Improntas;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.Improntas;

/// <summary>
/// Uso de ejemplo:
/// var impronta = new ImprontaGeneration { NumMotor = "M123" };
/// impronta.TieneIdentificadorVehiculo(); // true
///
/// <see cref="ImprontaGeneration.TieneIdentificadorVehiculo"/> es un helper informativo (ej. para UI),
/// NO un invariante obligatorio: el CHECK <c>ck_impronta_generations_identificador_vehiculo</c> que
/// originalmente lo exigía en BD se eliminó (migración <c>DropImprontaVehiculoIdentificadorCheck</c>) —
/// verificado contra el proveedor real que Kyverum genera la impronta sin ningún identificador de
/// vehículo, resolviéndolo internamente vía placa+documento.
/// </summary>
public sealed class ImprontaGenerationTests
{
    [Fact]
    public void TieneIdentificadorVehiculo_ConNumMotor_DebeRetornarTrue()
    {
        var impronta = new ImprontaGeneration { NumMotor = "MTR-123", NumChasis = null, NumSerie = null };

        impronta.TieneIdentificadorVehiculo().Should().BeTrue();
    }

    [Fact]
    public void TieneIdentificadorVehiculo_ConNumChasisUnicamente_DebeRetornarTrue()
    {
        var impronta = new ImprontaGeneration { NumMotor = null, NumChasis = "CHS-456", NumSerie = null };

        impronta.TieneIdentificadorVehiculo().Should().BeTrue();
    }

    [Fact]
    public void TieneIdentificadorVehiculo_ConNumSerieUnicamente_DebeRetornarTrue()
    {
        var impronta = new ImprontaGeneration { NumMotor = null, NumChasis = null, NumSerie = "SER-789" };

        impronta.TieneIdentificadorVehiculo().Should().BeTrue();
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData("   ", null, "")]
    public void TieneIdentificadorVehiculo_SinNingunIdentificador_DebeRetornarFalse(
        string? numMotor, string? numChasis, string? numSerie)
    {
        var impronta = new ImprontaGeneration
        {
            NumMotor = numMotor,
            NumChasis = numChasis,
            NumSerie = numSerie,
        };

        impronta.TieneIdentificadorVehiculo().Should().BeFalse();
    }

    [Fact]
    public void ImprontaGeneration_ExponeContratoCompletoDeTrazabilidad()
    {
        var impronta = new ImprontaGeneration
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            FlitUserId = Guid.NewGuid(),
            Radicado = "IMPR-00000001",
            HashSha256 = new string('a', 64),
            FechaImpresa = DateTimeOffset.UtcNow,
            Placa = "ABC123",
            NumMotor = "MTR-1",
            OrgNombre = "FLIT SAS",
            OrgNit = "900000000-1",
            OrgCiudad = "Bogotá",
            Operador = "Operador X",
            PdfContent = [0x25, 0x50, 0x44, 0x46],
            PdfSizeBytes = 4,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        impronta.Radicado.Should().Be("IMPR-00000001");
        impronta.PdfContent.Should().HaveCount(4);
        impronta.TieneIdentificadorVehiculo().Should().BeTrue();
    }
}
