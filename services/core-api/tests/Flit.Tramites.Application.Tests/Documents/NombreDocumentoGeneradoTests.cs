using System.Text;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Flit.Tramites.Application.Tests.Documents;

/// <summary>
/// HU #14735 (Feature #14733, Épica #14657) — los documentos que FLIT genera se guardan como
/// <c>{tipo}_{radicado}_{placa}.pdf</c>. Cubre la sobrecarga por instancia y los generadores de texto (mocks).
/// </summary>
public class NombreDocumentoGeneradoTests
{
    private static ProcedureInstance Instancia(string? plate, string? plateFieldValue = null)
    {
        var i = new ProcedureInstance { ReferenceNumber = "FT1-0000102", Plate = plate };
        if (plateFieldValue is not null)
            i.FieldValues.Add(new ProcedureInstanceFieldValue { FieldKey = "plate", ValueText = plateFieldValue });
        return i;
    }

    [Fact]
    public void PorInstancia_UsaPlateDenormalizada() =>
        NombreDocumentoTramite.Generar("consolidado", Instancia("aaa111"))
            .Should().Be("consolidado_FT1-0000102_AAA111.pdf");

    [Fact]
    public void PorInstancia_SinPlate_UsaFieldValuePlate() =>
        NombreDocumentoTramite.Generar("fur", Instancia(null, "bbb222"))
            .Should().Be("fur_FT1-0000102_BBB222.pdf");

    [Fact]
    public void PorInstancia_SinPlacaEnNingunLado_UsaSinPlaca() =>
        NombreDocumentoTramite.Generar("impronta", Instancia("  "))
            .Should().Be("impronta_FT1-0000102_SIN-PLACA.pdf");

    [Fact]
    public void MockIdentidad_ConservaTxt_ConElPatronEstandar()
    {
        var data = new IdentityCertificateData(Guid.NewGuid(), "FT1-0000102", "Ana", "1", 90, "APROBADO", "ccc333");

        var doc = new MockIdentityCertificateGenerator().GenerateIdentityCertificate(data);

        doc.Filename.Should().Be("certificado_identidad_FT1-0000102_CCC333.txt");
        doc.Mimetype.Should().Be("text/plain");
    }

    [Fact]
    public void MockIdentidad_SinPlaca_UsaSinPlaca()
    {
        var data = new IdentityCertificateData(Guid.NewGuid(), "FT1-0000102", "Ana", "1", 90, "APROBADO");

        new MockIdentityCertificateGenerator().GenerateIdentityCertificate(data).Filename
            .Should().Be("certificado_identidad_FT1-0000102_SIN-PLACA.txt");
    }

    [Fact]
    public void MockFurYCompraventa_ConservanTxt_ConElPatronEstandar()
    {
        var data = new FurDocumentData(
            ProcedureInstanceId: Guid.NewGuid(),
            ReferenceNumber: "FT1-0000102",
            Modalidad: "matricula",
            TipologiaCodigo: "MATRICULA_NUEVA",
            Vehiculo: new VehiculoDatos(null, null, null, null, null, null, null, "VIN123", "ddd444"),
            Organismo: new OrganismoTransito("5631000", "SABANETA", "Sabaneta"),
            Partes: [],
            ValorVenta: null,
            Causal: null,
            SellosFirma: []);
        var mock = new MockFurDocumentGenerator();

        mock.GenerateFur(data).Filename.Should().Be("fur_FT1-0000102_DDD444.txt");
        mock.GenerateCompraventa(data).Filename.Should().Be("compraventa_FT1-0000102_DDD444.txt");
        Encoding.UTF8.GetString(mock.GenerateFur(data).Content).Should().Contain("Referencia");
    }
}
