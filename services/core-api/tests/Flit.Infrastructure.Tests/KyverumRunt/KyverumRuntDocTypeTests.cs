using Flit.Infrastructure.KyverumRunt;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.KyverumRunt;

/// <summary>
/// HU #10478 / ADR-0082 — normalización del tipo de documento FLIT/Verifik → códigos Kyverum RUNT.
/// Persona (<c>/v1/personas:consultar</c>) envía <c>"Y"</c> para PPT (kyverum-runt #113); vehículo
/// (<c>/v1/vehiculos:consultar</c>) omite <c>tipoDocumento</c> para PPT porque su esquema solo admite
/// <c>C, E, T, P, N, D, R, PT</c>.
/// </summary>
public sealed class KyverumRuntDocTypeTests
{
    [Theory]
    [InlineData("CC", "C")]
    [InlineData("C", "C")]
    [InlineData("cc", "C")]
    [InlineData("TI", "T")]
    [InlineData("T", "T")]
    [InlineData("CE", "E")]
    [InlineData("E", "E")]
    [InlineData("NIT", "N")]
    [InlineData("N", "N")]
    [InlineData("PAS", "P")]
    [InlineData("PA", "P")]
    [InlineData("P", "P")]
    [InlineData("PPT", "Y")]
    [InlineData("ppt", "Y")]
    [InlineData(" ppt ", "Y")]
    [InlineData("Y", "Y")]
    [InlineData(" ce ", "E")]
    public void NormalizeForPersona_MapeaCodigosConocidosIncluyendoPpt(string input, string expected) =>
        KyverumRuntDocType.NormalizeForPersona(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XX")]
    [InlineData("DESCONOCIDO")]
    public void NormalizeForPersona_DesconocidoOVacio_DevuelveNull(string? input) =>
        KyverumRuntDocType.NormalizeForPersona(input).Should().BeNull();

    [Theory]
    [InlineData("CC", "C")]
    [InlineData("C", "C")]
    [InlineData("cc", "C")]
    [InlineData("TI", "T")]
    [InlineData("CE", "E")]
    [InlineData("NIT", "N")]
    [InlineData("N", "N")]
    [InlineData("PAS", "P")]
    [InlineData("PA", "P")]
    [InlineData(" ce ", "E")]
    public void NormalizeForVehicle_MapeaCodigosAdmitidosEnVehiculos(string input, string expected) =>
        KyverumRuntDocType.NormalizeForVehicle(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XX")]
    [InlineData("DESCONOCIDO")]
    [InlineData("PPT")] // /v1/vehiculos:consultar rechaza "Y"/"PPT" (400 VALIDATION_ERROR); omitir tipo.
    [InlineData("Y")]
    public void NormalizeForVehicle_PptODesconocido_DevuelveNull(string? input) =>
        KyverumRuntDocType.NormalizeForVehicle(input).Should().BeNull();
}
