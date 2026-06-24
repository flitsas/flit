using System.Text;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Tests del generador PDF real <see cref="FurDocumentGenerator"/> (HU #10256).
/// Verifican salida binaria válida (%PDF), mimetype, extensión y robustez ante campos nulos.
/// No se acoplan a la estructura visual interna del PDF; solo al contrato de GeneratedDocument.
/// </summary>
public sealed class FurDocumentGeneratorTests
{
    private static readonly FurDocumentGenerator Generator = new();

    private static FurDocumentData FullData() => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "TRM-2026-000001",
        Modalidad: "matricula_inicial",
        TipologiaCodigo: "matricula_inicial",
        Vehiculo: new VehiculoDatos(
            Marca: "TOYOTA",
            Linea: "COROLLA",
            Modelo: "2024",
            Color: "ROJO",
            Clase: "AUTOMOVIL",
            Combustible: "GASOLINA",
            Cilindraje: "1800",
            Vin: "1HGCM82633A004352",
            Placa: "ABC123",
            NumeroMotor: "ENG-99887766",
            NumeroChasis: "CHS-11223344",
            NumeroSerie: "SER-ABCDE12345",
            TipoCarroceria: "SEDAN",
            TipoServicio: "PARTICULAR",
            Capacidad: "5",
            PesoBruto: "1500",
            NumeroEjes: "2"),
        Organismo: new OrganismoTransito(Codigo: "11001000", Nombre: "SDM Bogotá", Ciudad: "Bogotá"),
        Partes: [new DocumentParte("comprador", "Juan Pérez", "12345678", "juan@example.com")],
        ValorVenta: null,
        Causal: null,
        SellosFirma: ["comprador/fur: abc123 (2026-06-24T00:00:00Z)"]);

    private static FurDocumentData NullFieldsData() => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "TRM-2026-000002",
        Modalidad: "matricula_inicial",
        TipologiaCodigo: null,
        Vehiculo: new VehiculoDatos(
            Marca: null, Linea: null, Modelo: null, Color: null, Clase: null,
            Combustible: null, Cilindraje: null, Vin: null, Placa: null),
        Organismo: new OrganismoTransito(null, null, null),
        Partes: [],
        ValorVenta: null,
        Causal: null,
        SellosFirma: []);

    // ── GenerateFur ──────────────────────────────────────────────────────────

    [Fact]
    public void GenerateFur_ProducesPdfBytes()
    {
        var doc = Generator.GenerateFur(FullData());

        doc.Content.Should().NotBeEmpty();
        // Cabecera PDF estándar: %PDF
        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void GenerateFur_HasCorrectMimetypeAndExtension()
    {
        var doc = Generator.GenerateFur(FullData());

        doc.Mimetype.Should().Be("application/pdf");
        doc.Filename.Should().EndWith(".pdf");
        doc.Tipo.Should().Be("fur");
    }

    [Fact]
    public void GenerateFur_FilenameContainsReference()
    {
        var doc = Generator.GenerateFur(FullData());

        doc.Filename.Should().Contain("TRM-2026-000001");
    }

    [Fact]
    public void GenerateFur_WithAllNullVehicleFields_DoesNotThrow()
    {
        var act = () => Generator.GenerateFur(NullFieldsData());

        act.Should().NotThrow();
        var doc = act();
        doc.Content.Should().NotBeEmpty();
        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void GenerateFur_NullData_ThrowsArgumentNullException()
    {
        var act = () => Generator.GenerateFur(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── GenerateCompraventa ──────────────────────────────────────────────────

    [Fact]
    public void GenerateCompraventa_ProducesPdfBytes()
    {
        var doc = Generator.GenerateCompraventa(FullData());

        doc.Content.Should().NotBeEmpty();
        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void GenerateCompraventa_HasCorrectMimetypeAndExtension()
    {
        var doc = Generator.GenerateCompraventa(FullData());

        doc.Mimetype.Should().Be("application/pdf");
        doc.Filename.Should().EndWith(".pdf");
        doc.Tipo.Should().Be("compraventa");
    }

    [Fact]
    public void GenerateCompraventa_NullData_ThrowsArgumentNullException()
    {
        var act = () => Generator.GenerateCompraventa(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── VehiculoDatos — nuevos campos HU #10256 ──────────────────────────────

    [Fact]
    public void VehiculoDatos_NewFieldsDefaultToNull_WhenNotProvided()
    {
        var v = new VehiculoDatos(
            Marca: "FORD", Linea: "FIESTA", Modelo: "2022", Color: "AZUL",
            Clase: "AUTOMOVIL", Combustible: "GASOLINA", Cilindraje: "1600",
            Vin: "VIN001", Placa: "XYZ999");

        v.NumeroMotor.Should().BeNull();
        v.NumeroChasis.Should().BeNull();
        v.NumeroSerie.Should().BeNull();
        v.TipoCarroceria.Should().BeNull();
        v.TipoServicio.Should().BeNull();
        v.Capacidad.Should().BeNull();
        v.PesoBruto.Should().BeNull();
        v.NumeroEjes.Should().BeNull();
    }

    [Fact]
    public void VehiculoDatos_NewFieldsRoundtrip()
    {
        var v = new VehiculoDatos(
            Marca: "RENAULT", Linea: "LOGAN", Modelo: "2023", Color: "BLANCO",
            Clase: "AUTOMOVIL", Combustible: "GASOLINA", Cilindraje: "1400",
            Vin: "VIN002", Placa: "DEF456",
            NumeroMotor: "M-001",
            NumeroChasis: "C-002",
            NumeroSerie: "S-003",
            TipoCarroceria: "SEDAN",
            TipoServicio: "PARTICULAR",
            Capacidad: "5",
            PesoBruto: "1200",
            NumeroEjes: "2");

        v.NumeroMotor.Should().Be("M-001");
        v.NumeroChasis.Should().Be("C-002");
        v.NumeroSerie.Should().Be("S-003");
        v.TipoCarroceria.Should().Be("SEDAN");
        v.TipoServicio.Should().Be("PARTICULAR");
        v.Capacidad.Should().Be("5");
        v.PesoBruto.Should().Be("1200");
        v.NumeroEjes.Should().Be("2");
    }
}
