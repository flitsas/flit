using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Infrastructure.Documents.Reports;
using Flit.Queries.Domain;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Analytics.Scheduling;

/// <summary>
/// Hoja «Firmas por persona» de los Excel programados de Consultas: una fila por persona, con las mismas filas
/// que la hoja principal, y ausente cuando la consulta no calculó la firma.
/// </summary>
public sealed class FirmasPorPersonaSheetTests
{
    private static FirmaPersonaDto Persona(string rol, string nombre, bool firmo, string estado, DateTimeOffset? fecha = null) =>
        new(rol, 1, nombre, "CC", "1020304050", firmo, estado, fecha);

    [Fact]
    public void Build_SinFirmaCalculada_NoCreaHoja()
    {
        var filas = new List<FirmasPorPersonaSheet.Fila>
        {
            new("FT1-1", "Organismo", "Empresa", "Traspaso", Personas: null),
        };

        FirmasPorPersonaSheet.Build(filas, incluirOrganismo: true, incluirCompania: false).Should().BeNull();
    }

    [Fact]
    public void Build_UnaFilaPorPersona_ConEstadoYFecha()
    {
        var firmo = new DateTimeOffset(2026, 11, 20, 15, 30, 0, TimeSpan.Zero);
        var filas = new List<FirmasPorPersonaSheet.Fila>
        {
            new("FT1-1", "Organismo", "Empresa", "Traspaso",
                [Persona("Vendedor", "Vera", true, "Firmó", firmo), Persona("Comprador", "Cándida", false, "Pendiente")]),
        };

        var sheet = FirmasPorPersonaSheet.Build(filas, incluirOrganismo: true, incluirCompania: false);

        sheet.Should().NotBeNull();
        sheet!.Name.Should().Be("Firmas por persona");
        sheet.Rows.Should().HaveCount(2, "una fila por persona, no por trámite");
        sheet.Columns.Select(c => c.Header).Should().Contain(["Radicado", "Organismo", "Rol", "Nombre", "Estado de firma", "Fecha de la firma"]);
    }

    [Fact]
    public void Write_ProduceUnLibroConLaHojaYLasPersonasLegibles()
    {
        var filas = new List<FirmasPorPersonaSheet.Fila>
        {
            new("FT1-1", "Organismo", "Empresa", "Traspaso",
                [Persona("Vendedor", "Vera", true, "Firmó", new DateTimeOffset(2026, 11, 20, 15, 30, 0, TimeSpan.Zero)),
                 Persona("Comprador", "Cándida", false, "Pendiente")]),
        };
        var sheet = FirmasPorPersonaSheet.Build(filas, incluirOrganismo: true, incluirCompania: true)!;

        var bytes = TabularWorkbookWriter.Write([sheet]);

        using var ms = new MemoryStream(bytes);
        using var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(ms, false);
        var nombres = doc.WorkbookPart!.Workbook.Descendants<DocumentFormat.OpenXml.Spreadsheet.Sheet>()
            .Select(s => s.Name!.Value).ToList();
        nombres.Should().Contain("Firmas por persona");

        // El writer escribe cadenas inline: el texto de cada celda está en los nodos Text de las hojas.
        var textos = doc.WorkbookPart.WorksheetParts
            .SelectMany(p => p.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Text>())
            .Select(t => t.Text).ToList();
        textos.Should().Contain(["Radicado", "Compañía", "Rol", "Nombre", "Estado de firma", "Vera", "Cándida", "Firmó", "Pendiente"]);
    }
}
