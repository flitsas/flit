using Flit.Infrastructure.Documents;
using Flit.Tramites.Application.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// HU #10914 (ADR-0036) — smoke tests del generador de la Solicitud de trámite virtual: produce un PDF
/// real (tipo <c>tramite_virtual</c>) para persona natural y jurídica, y no falla cuando las firmas
/// están ocultas (estado borrador) ni cuando falta la parte radicadora.
/// </summary>
public sealed class SolicitudVirtualPdfGeneratorTests
{
    private static readonly SolicitudVirtualPdfGenerator Generator = new();

    private static FurDocumentData DataWith(DocumentParte? parte, bool firmasVisibles = true, string codigo = "MATRICULA_NUEVA") =>
        new(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "REF-2026-1",
            Modalidad: "matricula",
            TipologiaCodigo: codigo,
            Vehiculo: new VehiculoDatos(null, null, null, null, null, null, null, "VIN123", "ABC123"),
            Organismo: new OrganismoTransito("5631000", "STRIA MOVILIDAD SABANETA", "Sabaneta"),
            Partes: parte is null ? [] : [parte],
            ValorVenta: null,
            Causal: null,
            SellosFirma: [],
            FirmasVisibles: firmasVisibles);

    /// <summary>PNG 1×1 válido: QuestPDF decodifica la imagen de verdad, no basta con bytes sueltos.</summary>
    private static readonly byte[] FirmaPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void ConFirmaDelBaul_GeneraElPdfConSuTrazabilidad()
    {
        // HU #11170 — la solicitud estampaba la imagen del baúl sin vigencia ni hash. El texto impreso
        // se verifica con render; aquí se comprueba que el bloque acepta los metadatos sin romperse.
        var parte = new DocumentParte("comprador", "Renting S.A.S.", "900123456-7", "info@renting.com", "NIT", "6041112233", EsJuridica: true);
        var data = DataWith(parte) with
        {
            FirmaImagenes = new Dictionary<string, byte[]> { ["comprador"] = FirmaPng },
            FirmaBaulMetadatos = new Dictionary<string, FirmaBaulMetadata>(StringComparer.OrdinalIgnoreCase)
            {
                ["comprador"] = new FirmaBaulMetadata(
                    "52123456", "Ana Gómez", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Guid.NewGuid(), "ABC-123"),
            },
        };

        Generator.GenerateSolicitudVirtual(data).Content.Should().NotBeEmpty();
    }

    [Fact]
    public void GeneratesPdf_ForPersonaNatural()
    {
        var data = DataWith(new DocumentParte("comprador", "Juan Pérez", "123456", "juan@x.com", "CC", "3001112233"));

        var doc = Generator.GenerateSolicitudVirtual(data);

        doc.Tipo.Should().Be("tramite_virtual");
        doc.Mimetype.Should().Be("application/pdf");
        doc.Content.Should().NotBeEmpty();
        doc.Filename.Should().StartWith("solicitud_tramite_virtual_");
    }

    [Fact]
    public void GeneratesPdf_ForPersonaJuridica_WithRepresentanteLegal()
    {
        var parte = new DocumentParte(
            "comprador", "Renting S.A.S.", "900123456-7", "info@renting.com", "NIT", "6041112233",
            EsJuridica: true,
            RepresentanteLegalNombre: "Ana Gómez",
            RepresentanteLegalTipoDoc: "CC",
            RepresentanteLegalDocumento: "52123456");

        var doc = Generator.GenerateSolicitudVirtual(DataWith(parte));

        doc.Tipo.Should().Be("tramite_virtual");
        doc.Content.Should().NotBeEmpty();
    }

    [Fact]
    public void GeneratesPdf_WhenFirmasHidden_AndWhenNoRadicador()
    {
        // Estado borrador: firmas ocultas, aún genera el documento.
        Generator.GenerateSolicitudVirtual(
            DataWith(new DocumentParte("comprador", "Juan", "1", null, "CC"), firmasVisibles: false))
            .Content.Should().NotBeEmpty();

        // Sin parte radicadora: no debe lanzar (usa placeholders).
        Generator.GenerateSolicitudVirtual(DataWith(null)).Content.Should().NotBeEmpty();
    }
}
