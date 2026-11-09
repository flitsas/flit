using Flit.Admin.Domain.PlatePreassign;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.PlatePreassign;

/// <summary>
/// Caso C 2026-11-09 — predicado de dominio «¿es carro?» y regla <c>ABC123</c> de
/// <see cref="PlateRangeRules"/>. El predicado recibe la clase YA normalizada con
/// <see cref="FurClassificationNormalizer.Normalize"/> y compara por igualdad exacta (nunca prefijo
/// ni contains: hay clases CAMION* de MAQUINARIA).
/// <para>Uso de ejemplo: <c>PlateRangeRules.IsCarClass(FurClassificationNormalizer.Normalize("Camioneta"))</c> ⇒ <c>true</c>.</para>
/// </summary>
public sealed class PlateRangeRulesCarroTests
{
    [Theory]
    [InlineData("AUTOMOVIL")]
    [InlineData("CAMIONETA")]
    [InlineData("CAMPERO")]
    [InlineData("BUS")]
    [InlineData("BUSETA")]
    [InlineData("MICROBUS")]
    [InlineData("CAMION")]
    [InlineData("TRACTOCAMION")]
    [InlineData("VOLQUETA")]
    public void IsCarClass_LasNueveClasesDeCarro(string clase) =>
        PlateRangeRules.IsCarClass(clase).Should().BeTrue();

    [Theory]
    [InlineData("Camioneta")]
    [InlineData("AUTOMÓVIL")]
    [InlineData("  micro   bus ")]
    [InlineData("tractocamión")]
    public void IsCarClass_ConElNormalizadorCompartido_TildesYMinusculasSonCarro(string crudo)
    {
        var normalizada = FurClassificationNormalizer.Normalize(crudo);
        // "MICRO BUS" con espacio NO es MICROBUS: igualdad exacta tras normalizar.
        PlateRangeRules.IsCarClass(normalizada).Should().Be(normalizada != "MICRO BUS");
    }

    [Theory]
    [InlineData("CAMION GRUA")]
    [InlineData("CAMIÓN GRÚA")]
    [InlineData("CAMIONETA PICK UP")]
    [InlineData("MOTOCICLETA")]
    [InlineData("MOTOCARRO")]
    [InlineData("CUATRIMOTO")]
    [InlineData("REMOLQUE")]
    [InlineData("SEMIRREMOLQUE")]
    [InlineData("SIN CLASE")]
    [InlineData("")]
    [InlineData(null)]
    public void IsCarClass_OtrasClasesNoSonCarro(string? crudo) =>
        PlateRangeRules.IsCarClass(FurClassificationNormalizer.Normalize(crudo)).Should().BeFalse();

    [Theory]
    [InlineData("ABC123", true)]
    [InlineData("ABC12D", false)]
    [InlineData("ABC12", false)]
    [InlineData("R12345", false)]
    [InlineData("MC029554", false)]
    [InlineData("AB1234", false)]
    [InlineData("ABC1234", false)]
    [InlineData("abc123", false)] // la normalización (trim + mayúsculas) va antes, en quien llama
    [InlineData("ABC١٢٣", false)] // dígitos no ASCII
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidCarPlate_SoloTresLetrasYTresDigitos(string? placa, bool esperado) =>
        PlateRangeRules.IsValidCarPlate(placa).Should().Be(esperado);

    [Fact]
    public void InvalidCarPlateFormatMessage_LiteralExacto() =>
        PlateRangeRules.InvalidCarPlateFormatMessage.Should()
            .Be("La placa de este vehículo debe tener 3 letras y 3 números, por ejemplo ABC123.");
}
