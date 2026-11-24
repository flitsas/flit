using Flit.Ict.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Flit.Ict.Application.Tests.Status;

/// <summary>
/// HU #14668 Bloque 1 — IctProcessStatusCodes: centralización de códigos, descripciones y mapeo v2→v1.
/// </summary>
public sealed class StatusProcessCodeMapTests
{
    // ──────────────────────────────────────────────────────────────
    // Describe()
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1,  "Registrado")]
    [InlineData(2,  "En Validacion")]
    [InlineData(3,  "Procesado")]
    [InlineData(4,  "Con Novedades")]
    [InlineData(5,  "Borrador")]
    [InlineData(6,  "Anulado")]
    [InlineData(7,  "Preparado")]
    [InlineData(8,  "Preasignacion")]
    [InlineData(9,  "Asignado")]
    [InlineData(10, "Entregado")]
    [InlineData(11, "Aprobado")]
    [InlineData(12, "Rechazado")]
    [InlineData(13, "Revocado")]
    public void Describe_KnownCode_ReturnsExpectedLabel(short code, string expected)
    {
        IctProcessStatusCodes.Describe(code).Should().Be(expected);
    }

    [Fact]
    public void Describe_UnknownCode_ReturnsEmpty()
    {
        IctProcessStatusCodes.Describe(99).Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────
    // MapFromV2()
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("borrador",      IctProcessStatusCodes.Borrador)]
    [InlineData("preparado",     IctProcessStatusCodes.Preparado)]
    [InlineData("preasignacion", IctProcessStatusCodes.Preasignacion)]
    [InlineData("asignado",      IctProcessStatusCodes.Asignado)]
    [InlineData("entregado",     IctProcessStatusCodes.Entregado)]
    [InlineData("aprobado",      IctProcessStatusCodes.Aprobado)]
    [InlineData("rechazado",     IctProcessStatusCodes.Rechazado)]
    [InlineData("anulado",       IctProcessStatusCodes.Anulado)]
    [InlineData("revocado",      IctProcessStatusCodes.Revocado)]
    public void MapFromV2_KnownStatus_ReturnsMappedCode(string v2, short expectedCode)
    {
        IctProcessStatusCodes.MapFromV2(v2).Should().Be(expectedCode);
    }

    [Theory]
    [InlineData("APROBADO")]      // mayúsculas
    [InlineData("Preparado")]     // mixed
    [InlineData("BORRADOR")]
    public void MapFromV2_CaseInsensitive(string v2)
    {
        IctProcessStatusCodes.MapFromV2(v2).Should().NotBe(IctProcessStatusCodes.Procesado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("otro_estado_desconocido")]
    public void MapFromV2_UnknownOrNull_ReturnsProcesado(string? v2)
    {
        // El fallback para estados desconocidos es Procesado (código 3).
        IctProcessStatusCodes.MapFromV2(v2).Should().Be(IctProcessStatusCodes.Procesado);
    }

    // ──────────────────────────────────────────────────────────────
    // IsOtState()
    // ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void IsOtState_OtCodes_ReturnsTrue(short code)
    {
        IctProcessStatusCodes.IsOtState(code).Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void IsOtState_LegacyCodes_ReturnsFalse(short code)
    {
        IctProcessStatusCodes.IsOtState(code).Should().BeFalse();
    }
}
