using Flit.Api.Grpc;
using Flit.Ict.Grpc.Contracts;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.Catalog;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Ict;

/// <summary>
/// Hallazgos QA de ICT — reglas puras de la placa de la matrícula (<c>plate_assignment_type</c> 2/3/4) y de
/// la carrocería de la transformación 17 (coincidencia EXACTA con el catálogo, salvo mayúsculas y espacios).
/// <para>Uso de ejemplo:
/// <code>
/// var items = new List&lt;FieldValueInput&gt; { new(null, "plate", "abc123", null) };
/// IctPlacaCarroceria.AjustarPlaca(items, 3); // plate = "ABC123" con Source = "ict"
/// </code></para>
/// </summary>
public sealed class IctPlacaCarroceriaTests
{
    private static CreateDraftFromIctRequest Request(string? tipo, string? plate)
    {
        var request = new CreateDraftFromIctRequest();
        if (tipo is not null)
        {
            request.FieldValues.Add(new FieldValue { FieldKey = "plate_assignment_type", ValueText = tipo });
        }

        if (plate is not null)
        {
            request.FieldValues.Add(new FieldValue { FieldKey = "plate", ValueText = plate });
        }

        return request;
    }

    [Fact]
    public void LosCodigosDeError_SonElContratoConCoreIct()
    {
        IctPlacaCarroceria.ErrorPlacaFueraDeRango.Should().Be("placa_fuera_de_rango");
        IctPlacaCarroceria.ErrorPlacaRangoSinOt.Should().Be("placa_rango_sin_ot");
        IctPlacaCarroceria.WarningCarroceriaFueraDeCatalogo.Should().Be("carroceria_warning:fuera_de_catalogo");
        IctPlacaCarroceria.SourceIct.Should().Be("ict");
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData(" 3 ", 3)]
    [InlineData("4", 4)]
    [InlineData("1", null)]
    [InlineData("5", null)]
    [InlineData("x", null)]
    [InlineData("-3", null)]
    [InlineData(null, null)]
    public void TipoPlaca_SoloAcepta2a4(string? valor, int? esperado) =>
        IctPlacaCarroceria.TipoPlaca(Request(valor, null)).Should().Be(esperado);

    [Theory]
    [InlineData("3", " abc123 ", "ABC123")]
    [InlineData("4", "abc123", "ABC123")]
    [InlineData("2", "5", null)]
    [InlineData(null, "ABC123", null)]
    [InlineData("4", "  ", null)]
    public void PlacaEntregada_SoloEnTipos3y4_Normalizada(string? tipo, string? plate, string? esperado) =>
        IctPlacaCarroceria.PlacaEntregada(Request(tipo, plate)).Should().Be(esperado);

    [Fact]
    public void PlacaDelVehiculo_EnTipo2_NoEsLaPlaca_YEnLosDemasSiLoEs()
    {
        IctPlacaCarroceria.PlacaDelVehiculo(Request("2", "5")).Should().BeNull();
        IctPlacaCarroceria.PlacaDelVehiculo(Request("3", "ABC123")).Should().Be("ABC123");
        IctPlacaCarroceria.PlacaDelVehiculo(Request(null, "ABC123")).Should().Be("ABC123");
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void AjustarPlaca_Tipos3y4_NormalizaYMarcaElOrigenIct(int tipo)
    {
        var items = new List<FieldValueInput>
        {
            new(null, "vin", "VIN1", null),
            new(null, "plate", " abc123 ", null),
        };

        IctPlacaCarroceria.AjustarPlaca(items, tipo);

        var plate = items.Single(i => i.FieldKey == "plate");
        plate.ValueText.Should().Be("ABC123");
        plate.Source.Should().Be("ict");
        items.Single(i => i.FieldKey == "vin").Source.Should().BeNull("solo la placa lleva el origen ict");
    }

    [Fact]
    public void AjustarPlaca_Tipo2_RetiraPlate_YConvierteElDigitoEnPreferencia()
    {
        var items = new List<FieldValueInput> { new(null, "plate", "5", null) };

        IctPlacaCarroceria.AjustarPlaca(items, 2);

        items.Should().NotContain(i => i.FieldKey == "plate");
        items.Should().ContainSingle(i => i.FieldKey == "plate_preferred_last_digit")
            .Which.ValueText.Should().Be("5");
    }

    [Fact]
    public void AjustarPlaca_Tipo2_NoDuplicaElDigitoSiYaVino_NiInventaUnoInvalido()
    {
        var conDigito = new List<FieldValueInput>
        {
            new(null, "plate", "5", null),
            new(null, "plate_preferred_last_digit", "7", null),
        };
        IctPlacaCarroceria.AjustarPlaca(conDigito, 2);
        conDigito.Should().ContainSingle().Which.ValueText.Should().Be("7");

        var invalido = new List<FieldValueInput> { new(null, "plate", "ABC123", null) };
        IctPlacaCarroceria.AjustarPlaca(invalido, 2);
        invalido.Should().BeEmpty("una placa completa en tipo 2 no es un dígito: no se guarda como placa");
    }

    [Fact]
    public void AjustarPlaca_SinTipo_NoCambiaNada()
    {
        var items = new List<FieldValueInput> { new(null, "plate", "abc123", null) };

        IctPlacaCarroceria.AjustarPlaca(items, null);

        items.Should().ContainSingle().Which.Should().Be(new FieldValueInput(null, "plate", "abc123", null));
    }

    // ---------- Carrocería ----------

    private static readonly IReadOnlyList<VehicleBodyworkEntry> Catalogo =
    [
        new(Guid.NewGuid(), "01", "VOLQUETA", "CAMION"),
        new(Guid.NewGuid(), "02", "FURGON  CERRADO", "CAMION"),
        new(Guid.NewGuid(), "03", "ESTACAS", "CAMION"),
    ];

    [Theory]
    [InlineData("VOLQUETA", "VOLQUETA")]
    [InlineData("  volqueta ", "VOLQUETA")]
    [InlineData("furgon cerrado", "FURGON  CERRADO")]
    [InlineData("FURGON   CERRADO", "FURGON  CERRADO")]
    public void CoincidenciaCarroceria_ExactaSalvoMayusculasYEspacios(string enviada, string nombreEsperado) =>
        IctPlacaCarroceria.CoincidenciaCarroceria(enviada, Catalogo)!.Name.Should().Be(nombreEsperado);

    [Theory]
    [InlineData("Cambio carroceria Hatchback")]
    [InlineData("VOLQUET")]
    [InlineData("VOLQUETA GRANDE")]
    [InlineData("FURGON")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CoincidenciaCarroceria_SinFuzzyNiContains_NoCoincide(string? enviada) =>
        IctPlacaCarroceria.CoincidenciaCarroceria(enviada, Catalogo).Should().BeNull();

    [Fact]
    public void CoincidenciaCarroceria_ConCatalogoVacio_NoCoincide() =>
        IctPlacaCarroceria.CoincidenciaCarroceria("VOLQUETA", []).Should().BeNull();
}
