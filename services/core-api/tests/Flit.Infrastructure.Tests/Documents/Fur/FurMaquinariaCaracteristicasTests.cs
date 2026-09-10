using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Fur;

/// <summary>
/// Marcación de tracción (8), cabina (16) y combustible (20) en el FUR de maquinaria.
/// </summary>
public sealed class FurMaquinariaCaracteristicasTests
{
    private static FurDocumentData Data(string? combustible = "DIESEL", string? traccion = null) => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "TRM-2026-000011",
        Modalidad: "traspaso",
        TipologiaCodigo: "TRASPASO_STANDARD",
        Vehiculo: new VehiculoDatos(
            Marca: "KOBELCO", Linea: "SK210LC-8", Modelo: "2015", Color: "AZUL VERDE",
            Clase: "EXCAVADORA", Combustible: combustible, Cilindraje: "0",
            Vin: "YQ12", Placa: "MC029554", TipoTraccion: traccion),
        Organismo: new OrganismoTransito("05001", "OT", "CIUDAD"),
        Partes: [new DocumentParte("vendedor", "A B C", "1", null, DocumentType: "CC")],
        ValorVenta: null, Causal: null, SellosFirma: [],
        TemplateFormat: FurTemplateFormat.Maquinaria,
        FieldToFill: "CONSTRUCCION");

    private static IReadOnlyDictionary<string, FurFieldValue> Map(
        string? combustible = "DIESEL",
        string? traccion = null) =>
        FurFieldMapper.Map(Data(combustible, traccion));

    [Theory]
    [InlineData("LLANTAS", "vehicle_traction_llantas")]
    [InlineData("LLANTA", "vehicle_traction_llantas")]
    [InlineData("ORUGAS", "vehicle_traction_orugas")]
    [InlineData("CILINDROS", "vehicle_traction_cilindros")]
    [InlineData("CILUNDRO", "vehicle_traction_cilindros")]
    public void TraccionConocida_MarcaSoloEsaCasilla(string rodaje, string esperada)
    {
        var dict = Map(traccion: rodaje);
        foreach (var id in new[]
                 {
                     "vehicle_traction_llantas",
                     "vehicle_traction_orugas",
                     "vehicle_traction_cilindros",
                     "vehicle_traction_otros",
                 })
        {
            dict[id].Text.Should().Be(id == esperada ? "X" : "", id);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MIXTO")]
    [InlineData("NO APLICA")]
    public void TraccionDesconocidaOVacia_MarcaOtros(string? rodaje)
    {
        var dict = Map(traccion: rodaje);
        dict["vehicle_traction_otros"].Text.Should().Be("X");
        dict["vehicle_traction_llantas"].Text.Should().BeEmpty();
        dict["vehicle_traction_orugas"].Text.Should().BeEmpty();
        dict["vehicle_traction_cilindros"].Text.Should().BeEmpty();
    }

    [Fact]
    public void Cabina_SiempreMarcaOtros()
    {
        var dict = Map();
        dict["vehicle_cabin_otros"].Text.Should().Be("X");
        dict["vehicle_cabin_cerrada"].Text.Should().BeEmpty();
        dict["vehicle_cabin_parasol"].Text.Should().BeEmpty();
        dict["vehicle_cabin_sin"].Text.Should().BeEmpty();
    }

    [Theory]
    [InlineData("GASOLINA", "vehicle_fuel_maq_1")]
    [InlineData("DIESEL", "vehicle_fuel_maq_2")]
    [InlineData("ELECTRICO", "vehicle_fuel_maq_3")]
    [InlineData("GAS NATURAL", "vehicle_fuel_maq_4")]
    [InlineData("HIBRIDO", "vehicle_fuel_maq_5")]
    [InlineData("MIXTO", "vehicle_fuel_maq_5")]
    public void CombustibleValido_MarcaLaCasillaDelBlank(string combustible, string esperada)
    {
        var dict = Map(combustible);
        for (var i = 1; i <= 6; i++)
        {
            var id = $"vehicle_fuel_maq_{i}";
            dict[id].Text.Should().Be(id == esperada ? "X" : "", id);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("OTRO")]
    [InlineData("BIODIESEL")]
    [InlineData("HIDROGENO")]
    public void CombustibleInvalidoOVacio_MarcaOtros(string? combustible)
    {
        var dict = Map(combustible);
        dict["vehicle_fuel_maq_6"].Text.Should().Be("X");
        for (var i = 1; i <= 5; i++)
            dict[$"vehicle_fuel_maq_{i}"].Text.Should().BeEmpty();
    }

    [Fact]
    public void Automotor_NoEmiteTokensDeMaquinaria()
    {
        var dict = FurFieldMapper.Map(Data() with { TemplateFormat = FurTemplateFormat.Automotor });
        dict.Should().NotContainKey("vehicle_traction_otros");
        dict.Should().NotContainKey("vehicle_cabin_otros");
        dict.Should().NotContainKey("vehicle_fuel_maq_2");
        dict["vehicle_fuel_type_2"].Text.Should().Be("X", "en automotor DIESEL sigue siendo la casilla 2");
    }
}
