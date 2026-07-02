using System.Text;
using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using PdfSharpCore.Fonts;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>Tests del generador FUR por overlay PdfSharpCore (HU #10256).</summary>
public sealed class FurOverlayDocumentGeneratorTests
{
    private static FurOverlayDocumentGenerator CreateGenerator()
    {
        var templates = FurTemplatePaths.FromBaseDirectory(AppContext.BaseDirectory);
        return new FurOverlayDocumentGenerator(FurFieldManifestLoader.LoadEmbedded(), templates);
    }

    private static FurDocumentData FullData() => new(
        ProcedureInstanceId: Guid.NewGuid(),
        ReferenceNumber: "TRM-2026-000001",
        Modalidad: "matricula_inicial",
        TipologiaCodigo: "matricula_inicial",
        Vehiculo: new VehiculoDatos(
            Marca: "TESLA",
            Linea: "MODELO Y",
            Modelo: "2026",
            Color: "BLANCO PERLA",
            Clase: "CAMIONETA",
            Combustible: "ELECTRICO",
            Cilindraje: "0",
            Vin: "LRWYGCFJ7TC495717",
            Placa: "YYY090",
            NumeroChasis: "LRWYGCFJ7TC495717",
            TipoCarroceria: "SUV",
            TipoServicio: "PARTICULAR",
            Capacidad: "5"),
        Organismo: new OrganismoTransito("25286000", "STRIATTOYTTE MCPAL FUNDA", "FUNDA"),
        Partes: [new DocumentParte("comprador", "DANIEL AMADO GARCIA", "1193552679", null)],
        ValorVenta: null,
        Causal: null,
        SellosFirma: []);

    private static FurDocumentData TraspasoData() => FullData() with
    {
        Modalidad = "traspaso",
        TipologiaCodigo = "traspaso_standard",
        Vehiculo = FullData().Vehiculo with
        {
            Marca = "BAJAJ",
            Linea = "PULSAR 200 NS",
            Clase = "MOTOCICLETA",
            Combustible = "GASOLINA",
            Placa = "IWL38D",
        },
        Partes =
        [
            new DocumentParte("vendedor", "AMOR Y CERVEZA JIMENEZ GUERRA", "1000445459", null),
            new DocumentParte("comprador", "STEFFEN REICHERT", "C27WKYL7", null),
        ],
    };

    [Fact]
    public void GenerateFur_ProducesTwoPagePdf()
    {
        if (!TemplatesExist()) return;

        var doc = CreateGenerator().GenerateFur(FullData());
        doc.Content.Should().NotBeEmpty();
        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
        CountPages(doc.Content).Should().Be(2);
    }

    [Fact]
    public void GenerateFur_HasCorrectMimetypeAndFilename()
    {
        if (!TemplatesExist()) return;

        var doc = CreateGenerator().GenerateFur(FullData());
        doc.Mimetype.Should().Be("application/pdf");
        doc.Tipo.Should().Be("fur");
        doc.Filename.Should().Contain("TRM-2026-000001");
    }

    [Fact]
    public void GenerateFur_Traspaso_ProducesTwoPages()
    {
        if (!TemplatesExist()) return;

        var doc = CreateGenerator().GenerateFur(TraspasoData());
        CountPages(doc.Content).Should().Be(2);
    }

    [Fact]
    public void GenerateCompraventa_ProducesPdf()
    {
        var doc = CreateGenerator().GenerateCompraventa(TraspasoData());
        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");
        doc.Tipo.Should().Be("compraventa");
    }

    [Fact]
    public void FurFieldMapper_MarksMatriculaTramite()
    {
        var values = FurFieldMapper.Map(FullData());
        values["requested_process_1"].Text.Should().Be("X");
        values["requested_process_2"].Text.Should().Be("");
    }

    [Fact]
    public void FurFieldMapper_MarksTraspasoTramite()
    {
        var values = FurFieldMapper.Map(TraspasoData());
        values["requested_process_2"].Text.Should().Be("X");
    }

    [Fact]
    public void FurFieldMapper_UsesActorContactAndDocumentType()
    {
        var data = FullData() with
        {
            Partes =
            [
                new DocumentParte(
                    "comprador",
                    "DANIEL AMADO GARCIA",
                    "1193552679",
                    "daniel@example.com",
                    DocumentType: "CC",
                    Phone: "3001234567",
                    Address: "CALLE 1 # 2-3",
                    City: "BOGOTA"),
            ],
        };

        var values = FurFieldMapper.Map(data);
        values["vehicle_owner_phone"].Text.Should().Be("3001234567");
        values["vehicle_owner_address"].Text.Should().Be("CALLE 1 # 2-3");
        values["vehicle_owner_city"].Text.Should().Be("BOGOTA");
        values["vehicle_owner_document_type_c"].Text.Should().Be("X");
        values["vehicle_owner_document_type_p"].Text.Should().Be("");
    }

    [Fact]
    public void FurFieldMapper_TraspasoBuyerPassport_MarksPassportCheckbox()
    {
        var data = TraspasoData() with
        {
            Partes =
            [
                new DocumentParte("vendedor", "VENDEDOR TEST", "1000445459", null, DocumentType: "CC"),
                new DocumentParte("comprador", "STEFFEN REICHERT", "C27WKYL7", null, DocumentType: "PAS"),
            ],
        };

        var values = FurFieldMapper.Map(data);
        values["vehicle_buyer_document_type_p"].Text.Should().Be("X");
        values["vehicle_buyer_document_type_c"].Text.Should().Be("");
    }

    [Fact]
    public void FurFieldMapper_UsesConfiguredProcessingDate()
    {
        var data = FullData() with { FechaTramite = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc) };
        var values = FurFieldMapper.Map(data);
        values["processing_day"].Text.Should().Be("15");
        values["processing_month"].Text.Should().Be("3");
        values["processing_year"].Text.Should().Be("2026");
    }

    // ── HU #10457 — calibración de la sección comprador/vendedor del FUR de traspaso ──

    [Fact]
    public void FurFieldMapper_TraspasoWithoutBuyer_LeavesBuyerSectionBlank()
    {
        // AC2: traspaso sin comprador resuelto → sección comprador en blanco (no '-' ni basura),
        // incluidos los checkboxes de tipo de documento del comprador.
        var data = TraspasoData() with
        {
            Partes = [new DocumentParte("vendedor", "VENDEDOR TEST", "1000445459", null, DocumentType: "CC")],
        };

        var values = FurFieldMapper.Map(data);
        values["vehicle_buyer_name"].Text.Should().BeEmpty();
        values["vehicle_buyer_first_last_name"].Text.Should().BeEmpty();
        values["vehicle_buyer_second_last_name"].Text.Should().BeEmpty();
        values["vehicle_buyer_document_number"].Text.Should().BeEmpty();
        values["vehicle_buyer_address"].Text.Should().BeEmpty();
        values["vehicle_buyer_signature"].Text.Should().BeEmpty();
        values["vehicle_buyer_document_type_c"].Text.Should().Be("");
        values["vehicle_buyer_document_type_p"].Text.Should().Be("");
    }

    [Fact]
    public void FurFieldMapper_CompoundBuyerName_SplitsNamesAndLastNames()
    {
        // AC3: nombre compuesto (4+ palabras) → SplitName reparte 2 apellidos + nombres.
        var data = TraspasoData() with
        {
            Partes =
            [
                new DocumentParte("vendedor", "VENDEDOR TEST", "1000445459", null, DocumentType: "CC"),
                new DocumentParte("comprador", "JUAN CARLOS PEREZ GOMEZ", "1234567890", null, DocumentType: "CC"),
            ],
        };

        var values = FurFieldMapper.Map(data);
        values["vehicle_buyer_first_last_name"].Text.Should().Be("PEREZ");
        values["vehicle_buyer_second_last_name"].Text.Should().Be("GOMEZ");
        values["vehicle_buyer_name"].Text.Should().Be("JUAN CARLOS");
    }

    [Fact]
    public void FurFieldMapper_PlateWithHyphen_SplitsLettersAndNumbers()
    {
        // AC3: placa con guion → SplitPlaca separa letras/números en plate_letter/plate_number.
        var data = TraspasoData() with
        {
            Vehiculo = TraspasoData().Vehiculo with { Placa = "ABC-123" },
        };

        var values = FurFieldMapper.Map(data);
        values["plate_letter"].Text.Should().Be("ABC");
        values["plate_number"].Text.Should().Be("123");
    }

    // ── HU #10463 — sello "NO FIRMADO" cuando no hay validación de identidad ──

    [Fact]
    public void FurFieldMapper_WithoutIdentity_PaintsNoFirmadoInSignatures()
    {
        // AC1: sin validación de identidad, el espacio de firma muestra "NO FIRMADO"
        // (traspaso: vendedor/propietario + comprador).
        var data = TraspasoData() with { IdentidadValidada = false };

        var values = FurFieldMapper.Map(data);
        values["vehicle_owner_signature"].Text.Should().Be("NO FIRMADO");
        values["vehicle_buyer_signature"].Text.Should().Be("NO FIRMADO");
    }

    [Fact]
    public void FurFieldMapper_WithIdentity_DoesNotPaintNoFirmado()
    {
        // AC2: con validación (por defecto), no se pinta "NO FIRMADO".
        var values = FurFieldMapper.Map(TraspasoData());
        values["vehicle_owner_signature"].Text.Should().NotBe("NO FIRMADO");
        values["vehicle_buyer_signature"].Text.Should().NotBe("NO FIRMADO");
    }

    // ── HU #10256 fix — resolutor de fuentes embebido (raíz del HTTP 500 en runtime alpine) ──

    [Fact]
    public void Templates_ArePresentInTestOutput()
    {
        // Guardia explícita: si esto falla, los tests de render están pasando en vacío por el
        // early-return `if (!TemplatesExist()) return;` y NO ejercitan el overlay ni las fuentes.
        TemplatesExist().Should().BeTrue(
            "las plantillas blank del FUR deben copiarse al output de test; sin ellas los tests de render no prueban nada");
    }

    [Fact]
    public void Generator_RegistersFontResolver_ForOsIndependentRendering()
    {
        // Construir el generador dispara el static ctor que registra el resolutor embebido.
        _ = CreateGenerator();
        GlobalFontSettings.FontResolver.Should().BeOfType<FurFontResolver>(
            "sin resolutor, PdfSharpCore no resuelve 'Arial' en runtimes sin fuentes (alpine) y el FUR responde HTTP 500");
    }

    [Fact]
    public void FontResolver_ResolvesArial_ToEmbeddedTrueType()
    {
        FurFontResolver.EnsureRegistered();
        var resolver = GlobalFontSettings.FontResolver;

        foreach (var isBold in new[] { false, true })
        {
            var info = resolver.ResolveTypeface("Arial", isBold, isItalic: false);
            info.Should().NotBeNull();

            var bytes = resolver.GetFont(info.FaceName);
            bytes.Should().NotBeNullOrEmpty();
            // Cabecera sfnt de TrueType (0x00 01 00 00): confirma que el recurso embebido es una fuente válida.
            bytes.Length.Should().BeGreaterThan(4);
            bytes[0].Should().Be(0x00);
            bytes[1].Should().Be(0x01);
            bytes[2].Should().Be(0x00);
            bytes[3].Should().Be(0x00);
        }
    }

    private static bool TemplatesExist()
    {
        try
        {
            FurTemplatePaths.FromBaseDirectory(AppContext.BaseDirectory).EnsureExists();
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static int CountPages(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(ms, PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Import);
        return doc.PageCount;
    }
}
