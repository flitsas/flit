using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Documents.BulkTramites;
using Flit.Tramites.Application.BulkTramites;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents.BulkTramites;

/// <summary>
/// Plantillas XLSX de carga masiva de trámites (HU #12520, AC1/AC3). Cubre lo que hace inválida
/// una plantilla de carga masiva sin que se note a simple vista: la hoja de datos debe ir primera
/// (el parser de HU #12522 lee por posición, no por nombre) y el desplegable de tipo de trámite de
/// la plantilla «Otros» tiene que reflejar el catálogo vigente, ni un código de más ni de menos.
/// </summary>
public sealed class BulkTramitesXlsxTemplateTests
{
    private readonly IProcedureTypeRepository _procedureTypeRepository = Substitute.For<IProcedureTypeRepository>();
    private readonly ITransitGrantRepository _transitGrants = Substitute.For<ITransitGrantRepository>();
    private readonly ITransitOfficeCatalog _transitOffices = Substitute.For<ITransitOfficeCatalog>();
    private readonly BulkTramitesXlsxTemplate _plantilla;

    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OficinaId = Guid.NewGuid();

    public BulkTramitesXlsxTemplateTests()
    {
        _transitGrants.ListEnabledOfficeIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([OficinaId]);
        _transitOffices.GetById(OficinaId)
            .Returns(new TransitOfficeEntry(OficinaId, "SDM-BOG", "Secretaría de Movilidad de Bogotá", "11", "11001"));

        _plantilla = new BulkTramitesXlsxTemplate(_procedureTypeRepository, _transitGrants, _transitOffices);
    }

    private static ProcedureType Tipo(string code, bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        Family = "VEHICULAR",
        IsActive = isActive,
    };

    [Theory]
    [InlineData(BulkTramitesTemplateType.Matricula)]
    [InlineData(BulkTramitesTemplateType.Traspaso)]
    [InlineData(BulkTramitesTemplateType.Otros)]
    public async Task Build_DejaLaHojaDeDatosPrimera_ConLaGuiaDetrasYLasListasOcultas(BulkTramitesTemplateType tipo)
    {
        _procedureTypeRepository
            .ListAsync(null, null, Arg.Any<CancellationToken>())
            .Returns([Tipo("CAMBIO_COLOR")]);

        var archivo = await _plantilla.BuildAsync(tipo, Tenant, TestContext.Current.CancellationToken);

        using var libro = SpreadsheetDocument.Open(new MemoryStream(archivo.Content), false);
        var hojas = libro.WorkbookPart!.Workbook.Sheets!.Elements<Sheet>().ToList();

        hojas.Should().HaveCount(3);
        hojas[0].Name!.Value.Should().Be(BulkTramitesTemplateCatalog.SheetName);
        hojas[1].Name!.Value.Should().Be(BulkTramitesTemplateCatalog.GuideSheetName);
        hojas[2].Name!.Value.Should().Be(BulkTramitesTemplateCatalog.ListsSheetName);
        hojas[2].State!.Value.Should().Be(SheetStateValues.Hidden);
    }

    [Fact]
    public async Task Build_Matricula_NoTraePorcentaje_PorqueEsUnSoloPropietario()
    {
        var columnas = BulkTramitesTemplateCatalog.MatriculaColumns();

        columnas.Select(c => c.Header).Should().Contain(["fila", "placa", "vin", "propietario_1_numero_documento", "propietario_4_porcentaje"]);
        // Copropiedad: hasta 4 propietarios con porcentaje, igual que en el wizard de matrícula.
        columnas.Count(c => c.Header.EndsWith("_porcentaje", StringComparison.Ordinal)).Should().Be(4);

        var archivo = await _plantilla.BuildAsync(BulkTramitesTemplateType.Matricula, Tenant, TestContext.Current.CancellationToken);
        archivo.Filename.Should().Contain("matricula");
    }

    [Fact]
    public async Task Build_Matricula_TraeElOrganismoDeTransito_ConLosHabilitadosDeLaEmpresa()
    {
        // Sin esta columna la matrícula NO se puede cargar masivamente: el paso 1 exige la
        // secretaría antes de consultar el VIN (HU #11199) y la fila muere en
        // TRANSIT_OFFICE_REQUIRED. Se detectó probando el flujo completo contra la API.
        BulkTramitesTemplateCatalog.MatriculaColumns().Select(c => c.Header)
            .Should().Contain(BulkTramitesTemplateCatalog.OrganismoTransitoHeader);

        var archivo = await _plantilla.BuildAsync(
            BulkTramitesTemplateType.Matricula, Tenant, TestContext.Current.CancellationToken);

        using var libro = SpreadsheetDocument.Open(new MemoryStream(archivo.Content), false);
        var hojaListas = libro.WorkbookPart!.WorksheetParts.Last().Worksheet;
        var valores = hojaListas.Descendants<Cell>()
            .Select(c => c.InlineString?.Text?.Text)
            .Where(v => v is not null)
            .ToList();

        // El desplegable ofrece el NOMBRE, que es lo que el procesador resuelve contra los
        // organismos habilitados de esa empresa.
        valores.Should().Contain("Secretaría de Movilidad de Bogotá");
    }

    [Fact]
    public async Task Build_Traspaso_NoPideOrganismo_PorqueLoImponeElRunt()
    {
        BulkTramitesTemplateCatalog.TraspasoColumns().Select(c => c.Header)
            .Should().NotContain(BulkTramitesTemplateCatalog.OrganismoTransitoHeader);

        await _plantilla.BuildAsync(
            BulkTramitesTemplateType.Traspaso, Tenant, TestContext.Current.CancellationToken);

        // Ni siquiera se consultan los grants: no hay desplegable que alimentar.
        await _transitGrants.DidNotReceive()
            .ListEnabledOfficeIdsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Build_Traspaso_SoportaHasta4PropietariosPorLado_ConPorcentaje()
    {
        var columnas = BulkTramitesTemplateCatalog.TraspasoColumns().Select(c => c.Header).ToList();

        columnas.Should().Contain("comprador_1_porcentaje");
        columnas.Should().Contain("comprador_4_porcentaje");
        columnas.Should().Contain("vendedor_1_porcentaje");
        columnas.Should().Contain("vendedor_4_porcentaje");
        columnas.Should().NotContain("comprador_5_numero_documento");
    }

    [Fact]
    public async Task Build_Otros_ElDesplegableDeTipoTramite_ExcluyeMatriculaYTraspaso_YSoloTraeActivos()
    {
        _procedureTypeRepository
            .ListAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(
            [
                Tipo("MATRICULA_NUEVA"),
                Tipo("TRASPASO_STANDARD"),
                Tipo("CAMBIO_COLOR"),
                Tipo("PRENDA_INSCRIPCION"),
                Tipo("BLINDAJE", isActive: false),
            ]);

        var archivo = await _plantilla.BuildAsync(BulkTramitesTemplateType.Otros, Tenant, TestContext.Current.CancellationToken);

        using var libro = SpreadsheetDocument.Open(new MemoryStream(archivo.Content), false);
        // Columna A de «Listas»: el primer catálogo distinto que aparece recorriendo las columnas
        // de la hoja de datos en orden es el de tipo_tramite (segunda columna de Otros).
        var hojaListas = libro.WorkbookPart!.WorksheetParts.Last().Worksheet;
        var valores = hojaListas.Descendants<Cell>()
            .Where(c => c.CellReference!.Value!.StartsWith('A') && c.CellReference.Value != "A1")
            .Select(c => c.InlineString?.Text?.Text)
            .Where(v => v is not null)
            .ToList();

        valores.Should().Contain("CAMBIO_COLOR");
        valores.Should().Contain("PRENDA_INSCRIPCION");
        valores.Should().NotContain("MATRICULA_NUEVA");
        valores.Should().NotContain("TRASPASO_STANDARD");
        valores.Should().NotContain("BLINDAJE");
    }

    [Theory]
    [InlineData("matricula", BulkTramitesTemplateType.Matricula)]
    [InlineData("Traspaso", BulkTramitesTemplateType.Traspaso)]
    [InlineData("OTROS", BulkTramitesTemplateType.Otros)]
    public void Parse_AceptaLosTresTipos_SinImportarMayusculas(string valor, BulkTramitesTemplateType esperado) =>
        BulkTramitesTemplateTypeParser.Parse(valor).Should().Be(esperado);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("duplicado_placa")]
    public void Parse_RechazaTiposNoSoportados(string? valor) =>
        BulkTramitesTemplateTypeParser.Parse(valor).Should().BeNull();
}
