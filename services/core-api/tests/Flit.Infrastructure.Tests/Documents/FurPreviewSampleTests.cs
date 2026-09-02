using System.Text;
using Flit.Infrastructure.Documents.Fur;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Tramites.ValueObjects;
using FluentAssertions;
using PdfSharpCore.Pdf.IO;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>HU #11701 — sample sintético del simulador FUR usa el generador productivo.</summary>
public sealed class FurPreviewSampleTests
{
    [Theory]
    [InlineData(FurPreviewSample.VehicleCarro, FurTemplateFormat.Automotor)]
    [InlineData(FurPreviewSample.VehicleMoto, FurTemplateFormat.Automotor)]
    [InlineData(FurPreviewSample.VehicleCamioneta, FurTemplateFormat.Automotor)]
    [InlineData(FurPreviewSample.VehicleRemolque, FurTemplateFormat.Remolques)]
    [InlineData(FurPreviewSample.VehicleMaquinaria, FurTemplateFormat.Maquinaria)]
    public void Build_GenerateFur_ProducesTwoPagePdf(string vehicleKind, FurTemplateFormat expectedFormat)
    {
        var data = FurPreviewSample.Build("MATRICULA_NUEVA", "MATRICULAS", "natural", "natural", vehicleKind);
        data.TemplateFormat.Should().Be(expectedFormat);

        var doc = new FurOverlayDocumentGenerator().GenerateFur(data);
        doc.Tipo.Should().Be("fur");
        Encoding.ASCII.GetString(doc.Content, 0, 4).Should().Be("%PDF");

        using var ms = new MemoryStream(doc.Content);
        var pdf = PdfReader.Open(ms, PdfDocumentOpenMode.InformationOnly);
        pdf.PageCount.Should().Be(2);
    }

    [Fact]
    public void Build_TwoNaturalBuyers_MapsCopropiedadColumns()
    {
        var data = FurPreviewSample.Build(
            "MATRICULA_NUEVA",
            "MATRICULAS",
            "natural",
            "natural",
            "carro",
            new FurPreviewFlags(BuyerCount: 2));
        data.Partes.Where(p => p.Rol == "comprador").Should().HaveCount(2);

        var mapped = FurFieldMapper.Map(data);
        mapped["vehicle_owner_first_last_name"].Text.Should().Be("CARDENAS\nFONNEGRA");
        mapped["vehicle_owner_document_number"].Text.Should().Contain("43623787");
        mapped["observations"].Text.Should().Contain("es el propietario del 50%");
        var stamps = mapped["vehicle_owner_signature"].SignatureStamps;
        stamps.Should().HaveCount(2);
        stamps![0].ImageBytes.Should().NotBeNullOrEmpty();
        stamps[0].ImageSidecarText.Should().Contain("Doc.");
        stamps[1].ImageSidecarText.Should().Contain("Doc.");
    }

    [Fact]
    public void Build_Matricula_IncludesBuyerNotSeller()
    {
        var data = FurPreviewSample.Build("MATRICULA_NUEVA", "MATRICULAS", "juridica", "natural", "carro");
        data.Partes.Should().ContainSingle(p => p.Rol == "comprador");
        data.Partes.Should().NotContain(p => p.Rol == "vendedor");
        FurPreviewSample.IsTraspaso("MATRICULAS", "MATRICULA_NUEVA").Should().BeFalse();
    }

    [Fact]
    public void Build_Traspaso_IncludesBothPartiesAsJuridica()
    {
        var data = FurPreviewSample.Build("TRASPASO_STANDARD", "TRASPASO", "juridica", "juridica", "carro");
        data.Partes.Should().HaveCount(2);
        data.Partes.Should().Contain(p => p.Rol == "vendedor" && p.EsJuridica);
        data.Partes.Should().Contain(p => p.Rol == "comprador" && p.EsJuridica);

        var mapped = FurFieldMapper.Map(data);
        mapped["requested_process_2"].Text.Should().Be("X");
        mapped["requested_process_1"].Text.Should().BeEmpty();
    }

    [Fact]
    public void Build_Moto_MarksMotocicletaClass()
    {
        var data = FurPreviewSample.Build("MATRICULA_NUEVA", "MATRICULAS", "natural", "natural", "moto");
        var mapped = FurFieldMapper.Map(data);
        mapped["vehicle_class_MOTOCICLETA"].Text.Should().Be("X");
        mapped["vehicle_class_AUTOMOVIL"].Text.Should().BeEmpty();
    }

    [Fact]
    public void BuildFromClassification_Cuadriciclo_MarksCuatrimotoNotAutomovil()
    {
        var data = FurPreviewSample.BuildFromClassification(
            "MATRICULA_NUEVA", "MATRICULAS", "natural", "natural",
            "CUADRICICLO", FurTemplateFormat.Automotor, "CUATRIMOTO");
        var mapped = FurFieldMapper.Map(data);
        mapped["vehicle_class_CUATRIMOTO"].Text.Should().Be("X");
        mapped["vehicle_class_AUTOMOVIL"].Text.Should().BeEmpty();
        mapped["vehicle_class_MOTOCICLETA"].Text.Should().BeEmpty();
    }

    [Fact]
    public void Build_Maquinaria_MarksConstruccion()
    {
        var data = FurPreviewSample.Build("MATRICULA_NUEVA", "MATRICULAS", "natural", "natural", "maquinaria");
        data.FieldToFill.Should().Be("CONSTRUCCION");
        var mapped = FurFieldMapper.Map(data);
        mapped["vehicle_class_CONSTRUCCION"].Text.Should().Be("X");
        mapped["vehicle_class_AGRICOLA"].Text.Should().BeEmpty();
        mapped["vehicle_axles"].Text.Should().Be("3");
        mapped["vehicle_height"].Text.Should().Be("2");
        mapped["vehicle_width"].Text.Should().Be("2.98");
    }

    [Fact]
    public void Build_Remolque_PintaEjesAltoYAncho()
    {
        var data = FurPreviewSample.Build("MATRICULA_NUEVA", "MATRICULAS", "natural", "natural", "remolque");
        var mapped = FurFieldMapper.Map(data);
        mapped["vehicle_class_REMOLQUE"].Text.Should().Be("X");
        mapped["vehicle_axles"].Text.Should().Be("3");
        mapped["vehicle_height"].Text.Should().Be("2");
        mapped["vehicle_width"].Text.Should().Be("2.98");
        mapped["vehicle_length"].Text.Should().Be("15.5");
    }

    [Fact]
    public void BuildFromClassification_Carretilla_MarksIndustrial()
    {
        var data = FurPreviewSample.BuildFromClassification(
            "MATRICULA_NUEVA", "MATRICULAS", "natural", "natural",
            "CARRETILLA APILADORA", FurTemplateFormat.Maquinaria, "INDUSTRIAL");
        FurFieldMapper.Map(data)["vehicle_class_INDUSTRIAL"].Text.Should().Be("X");
    }

    [Fact]
    public void BuildFromClassification_MaqConstruccion_MarksSimilar()
    {
        var data = FurPreviewSample.BuildFromClassification(
            "MATRICULA_NUEVA", "MATRICULAS", "natural", "natural",
            "MAQ. CONSTRUCCION O MINERA", FurTemplateFormat.Remolques, "SIMILAR");
        var mapped = FurFieldMapper.Map(data);
        mapped["vehicle_class_SIMILAR"].Text.Should().Be("X");
        mapped["vehicle_class_REMOLQUE"].Text.Should().BeEmpty();
    }

    [Fact]
    public void Build_CambioColor_MarksProcess5()
    {
        var data = FurPreviewSample.Build("CAMBIO_COLOR", "OTROS", "natural", "natural", "carro");
        data.Transformaciones.Color.Should().BeTrue();
        var mapped = FurFieldMapper.Map(data);
        mapped["requested_process_5"].Text.Should().Be("X");
        mapped["requested_process_1"].Text.Should().BeEmpty();
    }

    [Fact]
    public void Build_PrendaInscripcion_MarksProcess11()
    {
        var data = FurPreviewSample.Build("PRENDA_INSCRIPCION", "OTROS", "natural", "natural", "carro");
        data.PrendaMarking.Should().Be(FurPrendaMarking.Constitucion);
        FurFieldMapper.Map(data)["requested_process_11"].Text.Should().Be("X");
    }

    [Fact]
    public void Build_FlagsOverrideProcedureCode()
    {
        var data = FurPreviewSample.Build(
            "MATRICULA_NUEVA",
            "MATRICULAS",
            "natural",
            "natural",
            "carro",
            new FurPreviewFlags(
                CambioColor: true,
                CambioCombustible: true,
                CambioCarroceria: true,
                Blindaje: true,
                Prenda: FurPrendaMarking.Levantamiento));

        var mapped = FurFieldMapper.Map(data);
        mapped["requested_process_5"].Text.Should().Be("X");
        mapped["requested_process_17"].Text.Should().Be("X");
        mapped["requested_process_18"].Text.Should().Be("X");
        mapped["requested_process_12"].Text.Should().Be("X");
        mapped["alert_data_code_4"].Text.Should().Be("X");
        mapped["alert_data_code_2"].Text.Should().BeEmpty();
        mapped["alert_data_code_5"].Text.Should().Be("FONDEICON");
        mapped["is_armored_vehicle_yes"].Text.Should().Be("X");
        mapped["is_armored_vehicle_no"].Text.Should().BeEmpty();
    }

    [Fact]
    public void Build_Matricula_StampsBuyerSignatureWithAudit()
    {
        var data = FurPreviewSample.Build("MATRICULA_NUEVA", "MATRICULAS", "natural", "natural", "carro");
        data.IdentidadValidada.Should().BeTrue();
        data.FirmaImagenes.Should().ContainKey("comprador");
        data.FirmaImagenes.Should().NotContainKey("vendedor");

        var mapped = FurFieldMapper.Map(data);
        var sig = mapped["vehicle_owner_signature"];
        sig.ImageBytes.Should().NotBeNullOrEmpty();
        sig.ImageSidecarText.Should().Contain("Doc.");
        sig.ImageSidecarText.Should().Contain("Vig. 2026/07/01 — 2026/08/31");
        sig.ImageSidecarText.Should().Contain("Hash:");
        mapped["vehicle_buyer_signature"].Text.Should().BeEmpty();
    }

    [Fact]
    public void Build_Traspaso_StampsSellerAndBuyerSignatures()
    {
        var data = FurPreviewSample.Build("TRASPASO_STANDARD", "TRASPASO", "natural", "natural", "carro");
        data.FirmaImagenes.Should().ContainKey("vendedor");
        data.FirmaImagenes.Should().ContainKey("comprador");

        var mapped = FurFieldMapper.Map(data);
        mapped["vehicle_owner_signature"].ImageBytes.Should().NotBeNullOrEmpty();
        mapped["vehicle_buyer_signature"].ImageBytes.Should().NotBeNullOrEmpty();
        mapped["vehicle_owner_signature"].ImageSidecarText.Should().Contain(FurPreviewSample.PhVendedorPn.ToUpperInvariant());
        mapped["vehicle_buyer_signature"].ImageSidecarText.Should().Contain(FurPreviewSample.PhCompradorPn.ToUpperInvariant());
    }

    [Fact]
    public void TryParsePrenda_AcceptsKnownValues()
    {
        FurPreviewSample.TryParsePrenda("inscripcion", out var marking).Should().BeTrue();
        marking.Should().Be(FurPrendaMarking.Constitucion);
        FurPreviewSample.TryParsePrenda("ambas", out var ambas).Should().BeTrue();
        ambas.Should().Be(FurPrendaMarking.Ambos);
        FurPreviewSample.TryParsePrenda("barco", out _).Should().BeFalse();
    }

    [Fact]
    public void Build_Cancelacion_Marca13YNo1()
    {
        var mapped = FurFieldMapper.Map(
            FurPreviewSample.Build("CANCELACION_MATRICULA", "OTROS", "natural", "natural", "carro"));
        mapped["requested_process_13"].Text.Should().Be("X");
        mapped["requested_process_1"].Text.Should().BeEmpty();
    }

    [Fact]
    public void Build_EjemploCerrado_ObservacionesYCasillas()
    {
        var data = FurPreviewSample.Build(
            "TRASPASO_STANDARD",
            "TRASPASO",
            "natural",
            "natural",
            "carro",
            new FurPreviewFlags(
                CambioColor: true,
                CambioCarroceria: true,
                Prenda: FurPrendaMarking.Constitucion));
        var mapped = FurFieldMapper.Map(data);
        mapped["requested_process_2"].Text.Should().Be("X");
        mapped["requested_process_11"].Text.Should().Be("X");
        mapped["requested_process_5"].Text.Should().Be("X");
        mapped["requested_process_17"].Text.Should().Be("X");
        data.Observaciones.Should().Contain("Inscripción de prenda a favor de FONDEICON");
        data.Observaciones.Should().Contain("Color nuevo(NUEVO COLOR: MULTICOLOR CON AEROGRAFIAS)");
        data.Observaciones.Should().Contain("Carroceria nueva(NUEVA CARROCERIA: PICKUP)");
        data.Observaciones.Should().NotContain("[OBSERVACIONES DE SIMULACIÓN]");
        mapped["observations"].Text.Should().Be(data.Observaciones);
        mapped["observations"].Text.Should().NotBeNullOrWhiteSpace();
        mapped["alert_data_code_2"].Text.Should().Be("X");
        mapped["alert_data_code_4"].Text.Should().BeEmpty();
        mapped["alert_data_code_5"].Text.Should().Be("FONDEICON");
    }

    [Fact]
    public void Build_MatriculaLeasing_ObservacionDeLocatario()
    {
        var data = FurPreviewSample.Build("MATRICULA_LEASING", "MATRICULAS", "natural", "juridica", "carro");
        data.Observaciones.Should().Contain("Matrícula con locatario por Leasing de");
        data.Observaciones.Should().Contain("LOCATARIO TIPO DE DOCUMENTO");
        data.Partes.Should().Contain(p => p.Rol == "locatario");
    }

    [Fact]
    public void Build_Unilateral_ObservacionYSinFirmaComprador()
    {
        var data = FurPreviewSample.Build("TRASPASO_UNILATERAL", "TRASPASO", "natural", "natural", "carro");
        data.FirmaImagenes.Should().ContainKey("vendedor");
        data.FirmaImagenes.Should().NotContainKey("comprador");
        FurFieldMapper.Map(data)["vehicle_buyer_signature"].ImageBytes.Should().BeNullOrEmpty();
        data.Observaciones.Should().Contain("Traspaso unilateral por leasing a");
    }
}
