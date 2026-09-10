using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.Fur;

public sealed class FurDimensionesNoAutomotorTests
{
    [Theory]
    [InlineData("2000", "2")]
    [InlineData("2980", "2.98")]
    [InlineData("15500", "15.5")]
    [InlineData("2.5", "2.5")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ToFurMeters_ConvierteMilimetrosEnteros(string? raw, string esperado) =>
        FurFieldMapper.ToFurMeters(raw).Should().Be(esperado);

    [Fact]
    public void Remolques_PintaEjesAltoAnchoYLargo()
    {
        var dict = FurFieldMapper.Map(Data(FurTemplateFormat.Remolques));
        dict["vehicle_axles"].Text.Should().Be("3");
        dict["vehicle_height"].Text.Should().Be("2");
        dict["vehicle_width"].Text.Should().Be("2.98");
        dict["vehicle_length"].Text.Should().Be("15.5");
    }

    [Fact]
    public void Maquinaria_PintaEjesAltoAnchoYLargo()
    {
        var dict = FurFieldMapper.Map(Data(FurTemplateFormat.Maquinaria));
        dict["vehicle_axles"].Text.Should().Be("3");
        dict["vehicle_height"].Text.Should().Be("2");
        dict["vehicle_width"].Text.Should().Be("2.98");
    }

    [Fact]
    public void Automotor_NoEmiteDimensionesDeRemolque()
    {
        var dict = FurFieldMapper.Map(Data(FurTemplateFormat.Automotor));
        dict.Should().NotContainKey("vehicle_axles");
        dict.Should().NotContainKey("vehicle_height");
        dict.Should().NotContainKey("vehicle_width");
    }

    private static FurDocumentData Data(FurTemplateFormat format) => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "TRM-2026-000012",
        Modalidad: "traspaso",
        TipologiaCodigo: "TRASPASO_STANDARD",
        Vehiculo: new VehiculoDatos(
            Marca: "M", Linea: "L", Modelo: "2014", Color: "BLANCO",
            Clase: "SEMIREMOLQUE", Combustible: "DIESEL", Cilindraje: "0",
            Vin: "VIN", Placa: "S07249",
            NumeroEjes: "3", Alto: "2000", Ancho: "2980", Largo: "15500"),
        Organismo: new OrganismoTransito("05001", "OT", "CIUDAD"),
        Partes: [new DocumentParte("vendedor", "A B C", "1", null, DocumentType: "CC")],
        ValorVenta: null, Causal: null, SellosFirma: [],
        TemplateFormat: format,
        FieldToFill: format == FurTemplateFormat.Remolques ? "SEMIREMOLQUE" : "CONSTRUCCION");
}
