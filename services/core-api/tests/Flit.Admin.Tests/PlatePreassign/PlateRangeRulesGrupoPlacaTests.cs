using Flit.Admin.Domain.PlatePreassign;
using FluentAssertions;
using Xunit;

namespace Flit.Admin.Tests.PlatePreassign;

/// <summary>
/// HU8 [BACKEND] (Caso C 2026-11-09, D1) — grupos de placa por clase de vehículo en
/// <see cref="PlateRangeRules"/>: CARRO <c>ABC123</c>, MOTO <c>ABC12</c>/<c>ABC12D</c>, REMOLQUE
/// <c>R|S</c> + 5–6 dígitos, MAQUINARIA <c>AA123456</c>. Sin grupo ⇒ unión de formatos actual.
/// <para>Uso de ejemplo: <c>PlateRangeRules.IsValidPlateForGroup(PlateClassGroup.Moto, "ABC123")</c> ⇒ <c>false</c>.</para>
/// </summary>
public sealed class PlateRangeRulesGrupoPlacaTests
{
    [Theory]
    [InlineData("AUTOMOVIL", PlateClassGroup.Carro)]
    [InlineData("VOLQUETA", PlateClassGroup.Carro)]
    [InlineData("MOTOCICLETA", PlateClassGroup.Moto)]
    [InlineData("MOTOCARRO", PlateClassGroup.Motocarro)]
    [InlineData("CUATRIMOTO", PlateClassGroup.Moto)]
    [InlineData("CUADRICICLO", PlateClassGroup.Moto)]
    [InlineData("MOTOTRICICLO", PlateClassGroup.Motocarro)]
    [InlineData("TRICIMOTO", PlateClassGroup.Motocarro)]
    [InlineData("CICLOMOTOR", PlateClassGroup.Moto)]
    [InlineData("REMOLQUE", PlateClassGroup.Remolque)]
    [InlineData("SEMIREMOLQUE", PlateClassGroup.Remolque)]
    [InlineData("SEMIRREMOLQUE", PlateClassGroup.Remolque)]
    [InlineData("REMOLQUE Y SEMIRREMOLQUE", PlateClassGroup.Remolque)]
    public void Resolve_ListasExactas_SinCatalogo(string normalizada, PlateClassGroup esperado) =>
        PlateRangeRules.ResolvePlateClassGroup(normalizada, catalogGroup: null).Should().Be(esperado);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SIN CLASE")]
    [InlineData("NAVE ESPACIAL")]
    [InlineData("MOTO")]
    [InlineData("CAMION GRUA")]
    public void Resolve_DesconocidaVaciaOSinClase_SinCatalogo_SinGrupo(string? normalizada) =>
        PlateRangeRules.ResolvePlateClassGroup(normalizada, catalogGroup: null).Should().BeNull();

    [Fact]
    public void Resolve_CatalogoMaquinaria_EsMaquinaria() =>
        PlateRangeRules.ResolvePlateClassGroup("CAMION GRUA", PlateClassGroup.Maquinaria).Should().Be(PlateClassGroup.Maquinaria);

    [Fact]
    public void Resolve_CatalogoRemolques_EsRemolque() =>
        PlateRangeRules.ResolvePlateClassGroup("MAQ. CONSTRUCCION O MINERA", PlateClassGroup.Remolque)
            .Should().Be(PlateClassGroup.Remolque);

    [Theory]
    [InlineData(PlateClassGroup.Carro)]
    [InlineData(PlateClassGroup.Moto)]
    public void Resolve_ElCatalogoNoFabricaCarroNiMoto(PlateClassGroup catalogo) =>
        PlateRangeRules.ResolvePlateClassGroup("NAVE ESPACIAL", catalogo).Should().BeNull(
            "carro y moto salen solo de las listas exactas; el AUTOMOTOR por defecto del catálogo no es grupo");

    [Fact]
    public void Resolve_ListaExactaGanaAlCatalogo() =>
        PlateRangeRules.ResolvePlateClassGroup("CAMION", PlateClassGroup.Maquinaria).Should().Be(PlateClassGroup.Carro);

    [Theory]
    [InlineData(PlateClassGroup.Carro, "ABC123", true)]
    [InlineData(PlateClassGroup.Carro, "ABC12D", false)]
    [InlineData(PlateClassGroup.Carro, "MC029554", false)]
    [InlineData(PlateClassGroup.Moto, "ABC12", true)]
    [InlineData(PlateClassGroup.Moto, "ABC12D", true)]
    [InlineData(PlateClassGroup.Moto, "ABC123", false)]
    [InlineData(PlateClassGroup.Moto, "AB12D", false)]
    [InlineData(PlateClassGroup.Remolque, "R12345", true)]
    [InlineData(PlateClassGroup.Remolque, "S654321", true)]
    [InlineData(PlateClassGroup.Remolque, "S54321", true)]
    [InlineData(PlateClassGroup.Remolque, "X12345", false)]
    [InlineData(PlateClassGroup.Remolque, "R1234", false)]
    [InlineData(PlateClassGroup.Remolque, "R1234567", false)]
    [InlineData(PlateClassGroup.Moto, "123ABC", false)]
    [InlineData(PlateClassGroup.Motocarro, "123ABC", true)]
    [InlineData(PlateClassGroup.Motocarro, "523ABC", true)]
    [InlineData(PlateClassGroup.Motocarro, "ABC12D", false)]
    [InlineData(PlateClassGroup.Motocarro, "ABC123", false)]
    [InlineData(PlateClassGroup.Motocarro, "12ABC", false)]
    [InlineData(PlateClassGroup.Motocarro, "1234AB", false)]
    [InlineData(PlateClassGroup.Carro, "123ABC", false)]
    [InlineData(PlateClassGroup.Maquinaria, "MC029554", true)]
    [InlineData(PlateClassGroup.Maquinaria, "MC02955", false)]
    [InlineData(PlateClassGroup.Maquinaria, "ABC123", false)]
    public void IsValidPlateForGroup_PorGrupo(PlateClassGroup grupo, string placa, bool esperado) =>
        PlateRangeRules.IsValidPlateForGroup(grupo, placa).Should().Be(esperado);

    [Theory]
    [InlineData("ABC123")]
    [InlineData("ABC12D")]
    [InlineData("ABC12")]
    [InlineData("R12345")]
    [InlineData("S654321")]
    [InlineData("MC029554")]
    [InlineData("123ABC")]
    public void IsValidPlateForGroup_SinGrupo_AceptaLaUnion(string placa) =>
        PlateRangeRules.IsValidPlateForGroup(null, placa).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("X1")]
    public void IsValidPlateForGroup_SinGrupo_RechazaLoQueLaUnionRechaza(string? placa) =>
        PlateRangeRules.IsValidPlateForGroup(null, placa).Should().BeFalse();

    [Theory]
    [InlineData(PlateClassGroup.Carro, "La placa de este vehículo debe tener 3 letras y 3 números, por ejemplo ABC123.")]
    [InlineData(PlateClassGroup.Moto, "La placa de esta moto debe tener 3 letras y 2 números, con o sin una letra al final, por ejemplo ABC12D o ABC12.")]
    [InlineData(PlateClassGroup.Remolque, "La placa de este remolque debe empezar por R o S seguida de 5 o 6 números, por ejemplo R12345 o S54321.")]
    [InlineData(PlateClassGroup.Motocarro, "La placa de este motocarro debe tener 3 números y 3 letras, por ejemplo 123ABC.")]
    [InlineData(PlateClassGroup.Maquinaria, "La placa de esta maquinaria debe tener 2 letras y 6 números, por ejemplo MC029554.")]
    public void InvalidPlateFormatMessageFor_LiteralesExactos(PlateClassGroup grupo, string literal) =>
        PlateRangeRules.InvalidPlateFormatMessageFor(grupo).Should().Be(literal);

    [Theory]
    [InlineData(PlateClassGroup.Carro, "CARRO")]
    [InlineData(PlateClassGroup.Moto, "MOTO")]
    [InlineData(PlateClassGroup.Motocarro, "MOTOCARRO")]
    [InlineData(PlateClassGroup.Remolque, "REMOLQUE")]
    [InlineData(PlateClassGroup.Maquinaria, "MAQUINARIA")]
    [InlineData(null, null)]
    public void ToContractValue_ValoresDelContrato(PlateClassGroup? grupo, string? valor) =>
        PlateRangeRules.ToContractValue(grupo).Should().Be(valor);

    // HU10 — el mensaje de la unión (clase sin grupo) suma el ejemplo de motocarro.
    [Fact]
    public void InvalidPlateFormatMessage_IncluyeMotocarro() =>
        PlateRangeRules.InvalidPlateFormatMessage.Should().Be("La placa no cumple el formato esperado. Ej: ABC123 (carro), ABC12D (moto), 123ABC (motocarro), R12345 (remolque), S12345 (semirremolque) o MC029554 (maquinaria).");
}
