using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.UseCases.ProcedureInstances;

/// <summary>
/// Bug #13194 (P4, D4) — <see cref="BiometricRules.DocumentoCoincide"/> es fail-closed: sin los DOS
/// números de documento no hay coincidencia. Antes, si faltaba uno, coincidía siempre y una validación
/// podía aprobar a una persona distinta del mismo tenant.
/// </summary>
/// <remarks>
/// Uso de ejemplo: <c>BiometricRules.DocumentoCoincide(validacion, "CC", "9000000301")</c> → true solo si
/// la validación es de ese documento (y del mismo tipo cuando ambos traen tipo).
/// </remarks>
public sealed class DocumentoCoincideFailClosedTests
{
    private static ProcedureInstanceBiometricValidation Validacion(string? tipo, string? numero) => new()
    {
        Id = Guid.NewGuid(),
        PartyRole = "comprador",
        Status = BiometricEstados.Aprobado,
        DocumentType = tipo ?? string.Empty,
        DocumentNumber = numero ?? string.Empty,
    };

    [Fact]
    public void MismoDocumento_Coincide() =>
        BiometricRules.DocumentoCoincide(Validacion("CC", "9000000301"), "CC", " 9000000301 ").Should().BeTrue();

    [Fact]
    public void DocumentoDistinto_NoCoincide() =>
        BiometricRules.DocumentoCoincide(Validacion("CC", "9000000301"), "CC", "9000000302").Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ValidacionSinDocumento_NoCoincide(string? numero) =>
        BiometricRules.DocumentoCoincide(Validacion("CC", numero), "CC", "9000000301").Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SujetoSinDocumento_NoCoincide(string? documento) =>
        BiometricRules.DocumentoCoincide(Validacion("CC", "9000000301"), "CC", documento).Should().BeFalse();

    [Fact]
    public void TipoDistinto_ConAmbosTipos_NoCoincide() =>
        BiometricRules.DocumentoCoincide(Validacion("CE", "9000000301"), "CC", "9000000301").Should().BeFalse();

    [Fact]
    public void TipoAusenteEnUnLado_ConMismoNumero_Coincide() =>
        BiometricRules.DocumentoCoincide(Validacion(null, "9000000301"), "CC", "9000000301").Should().BeTrue();
}
