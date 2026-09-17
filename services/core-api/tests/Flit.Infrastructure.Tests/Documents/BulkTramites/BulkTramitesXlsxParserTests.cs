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
                ["propietario_1_celular"] = "3001234567",
                ["propietario_1_ciudad"] = "Bogotá",
                ["propietario_1_direccion"] = "Calle 1 # 2-3",
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
            ["comprador_1_email"] = "c@example.com",
            ["comprador_1_celular"] = "3001234567",
            ["comprador_1_ciudad"] = "Bogotá",
            ["comprador_1_direccion"] = "Calle 1 # 2-3",
            ["vendedor_1_numero_documento"] = "8",
            ["vendedor_1_email"] = "v@example.com",
            ["vendedor_1_celular"] = "3007654321",
            ["vendedor_1_ciudad"] = "Medellín",
            ["vendedor_1_direccion"] = "Carrera 43 # 5-15",
        };

        var archivo = BulkTramitesXlsxBuilder.ConPlantilla(BulkTramitesTemplateType.Traspaso, filaMala, filaBuena);

        var resultado = _parser.Parse(BulkTramitesTemplateType.Traspaso, new MemoryStream(archivo));

        resultado.FileError.Should().BeNull();
        resultado.Rows.Should().HaveCount(2);
        resultado.Rows[0].StructuralErrorCode.Should().Be(BulkTramitesPercentageValidator.PorcentajesNoSuman100);
        resultado.Rows[1].StructuralErrorCode.Should().BeNull();
    }

    /// <summary>
    /// Salió de las pruebas en DEV de la Feature #12519: una empresa (NIT) sin ciudad ni dirección
    /// se creaba «a medias» y el wizard la devolvía a Actores. Ahora la fila queda en error
    /// estructural, con el prefijo del actor al que le faltan datos.
    /// </summary>
    [Fact]
    public void Parse_Traspaso_EmpresaSinCiudadNiDireccion_MarcaLaFilaConElActor()
    {
        var fila = new Dictionary<string, string>
        {
            ["fila"] = "1",
            ["placa"] = "WQM281",
            ["comprador_1_numero_documento"] = "3",
            ["comprador_1_email"] = "c@example.com",
            ["comprador_1_celular"] = "3001234567",
            ["comprador_1_ciudad"] = "Bogotá",
            ["comprador_1_direccion"] = "Calle 1 # 2-3",
            ["vendedor_1_tipo_documento"] = "NIT",
            ["vendedor_1_numero_documento"] = "890903938",
            ["vendedor_1_email"] = "v@example.com",
            ["vendedor_1_celular"] = "3007654321",
        };

        var archivo = BulkTramitesXlsxBuilder.ConPlantilla(BulkTramitesTemplateType.Traspaso, fila);

        var resultado = _parser.Parse(BulkTramitesTemplateType.Traspaso, new MemoryStream(archivo));

        resultado.FileError.Should().BeNull();
        resultado.Rows.Should().ContainSingle()
            .Which.StructuralErrorCode.Should().Be($"{BulkTramitesContactValidator.DatosContactoIncompletos}:vendedor_1");
    }
}
