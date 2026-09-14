using Flit.Infrastructure.Documents.BulkTramites;
using Flit.Tramites.Application.BulkTramites;
using Flit.Tramites.Application.BulkTramites.Parsing;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.BulkTramites;

/// <summary>
/// Parser XLSX de carga masiva de trámites (HU #12522). El hecho central: el archivo que la
/// plantilla entrega (HU #12520) es el mismo que este parser acepta —incluye el encabezado
/// EXACTO producido por <c>BulkTramitesTemplateCatalog</c>—, y una fila mala no tumba el lote
/// completo (AC3), solo esa fila.
/// </summary>
public sealed class BulkTramitesXlsxParserTests
{
    private readonly BulkTramitesXlsxParser _parser = new();

    [Fact]
    public void Parse_ArchivoValidoDeMatricula_LeeLaFilaSinError()
    {
        var archivo = BulkTramitesXlsxBuilder.ConPlantilla(
            BulkTramitesTemplateType.Matricula,
            new Dictionary<string, string>
            {
                ["fila"] = "1",
                ["placa"] = "ABC123",
                ["propietario_1_numero_documento"] = "123456789",
                ["propietario_1_email"] = "juan@example.com",
                ["propietario_1_ciudad"] = "Bogotá",
            });

        var resultado = _parser.Parse(BulkTramitesTemplateType.Matricula, new MemoryStream(archivo));

        resultado.FileError.Should().BeNull();
        resultado.Rows.Should().HaveCount(1);
        resultado.Rows[0].RowNumber.Should().Be(1);
        resultado.Rows[0].StructuralErrorCode.Should().BeNull();
        resultado.Rows[0].Values["placa"].Should().Be("ABC123");
        resultado.Rows[0].Values["propietario_1_ciudad"].Should().Be("Bogotá");
    }

    [Fact]
    public void Parse_EncabezadoQueNoCoincideConLaPlantilla_RechazaTodoElArchivo()
    {
        var archivo = BulkTramitesXlsxBuilder.ConEncabezado(["fila", "otra_columna"]);

        var resultado = _parser.Parse(BulkTramitesTemplateType.Matricula, new MemoryStream(archivo));

        resultado.FileError.Should().Be(BulkTramitesFileError.TemplateInvalid);
        resultado.Rows.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ArchivoQueNoEsUnXlsx_SeRechazaComoInvalidFile()
    {
        var resultado = _parser.Parse(
            BulkTramitesTemplateType.Matricula, new MemoryStream("no soy un xlsx"u8.ToArray()));

        resultado.FileError.Should().Be(BulkTramitesFileError.InvalidFile);
    }

    [Fact]
    public void Parse_MasDe50Filas_RechazaTodoElArchivoConTooManyRows()
    {
        var filas = Enumerable.Range(1, 51)
            .Select(i => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>
            {
                ["fila"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["placa"] = "ABC123",
            })
            .ToArray();

        var archivo = BulkTramitesXlsxBuilder.ConPlantilla(BulkTramitesTemplateType.Matricula, filas);

        var resultado = _parser.Parse(BulkTramitesTemplateType.Matricula, new MemoryStream(archivo));

        resultado.FileError.Should().Be(BulkTramitesFileError.TooManyRows);
    }

    [Fact]
    public void Parse_Traspaso_DosCompradoresConPorcentajesQueNoSuman100_MarcaSoloEsaFilaEnError()
    {
        var filaMala = new Dictionary<string, string>
        {
            ["fila"] = "1",
            ["placa"] = "ABC123",
            ["comprador_1_numero_documento"] = "1",
            ["comprador_1_porcentaje"] = "60",
            ["comprador_2_numero_documento"] = "2",
            ["comprador_2_porcentaje"] = "30",
            ["vendedor_1_numero_documento"] = "9",
        };
        var filaBuena = new Dictionary<string, string>
        {
            ["fila"] = "2",
            ["placa"] = "DEF456",
            ["comprador_1_numero_documento"] = "3",
            ["vendedor_1_numero_documento"] = "8",
        };

        var archivo = BulkTramitesXlsxBuilder.ConPlantilla(BulkTramitesTemplateType.Traspaso, filaMala, filaBuena);

        var resultado = _parser.Parse(BulkTramitesTemplateType.Traspaso, new MemoryStream(archivo));

        resultado.FileError.Should().BeNull();
        resultado.Rows.Should().HaveCount(2);
        resultado.Rows[0].StructuralErrorCode.Should().Be(BulkTramitesPercentageValidator.PorcentajesNoSuman100);
        resultado.Rows[1].StructuralErrorCode.Should().BeNull();
    }
}
