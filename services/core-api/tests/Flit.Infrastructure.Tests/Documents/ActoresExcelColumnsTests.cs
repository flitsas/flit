using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using Flit.Analytics.Application.Abstractions;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Analytics.Application.Dtos;
using Flit.Infrastructure.Documents;
using Flit.Tramites.Domain.Identity;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Bloques de validación de identidad por persona en el Excel del servidor: encabezados planos con el rol, 5 columnas por
/// actor, al final de la hoja, ancho común a todas las partes y ninguna mención al mecanismo de validación.
/// </summary>
public sealed class ActoresExcelColumnsTests
{
    private static ActorValidacionDto Actor(string rol, int ordinal, string estado = "aprobado", int intentos = 1,
        string? motivo = null) => new(rol, ordinal, $"{rol} {ordinal}", "CC", $"{ordinal}00", estado, motivo, intentos);

    private static IReadOnlyList<ActorValidacionDto> Actores(int c, int v, int p) =>
    [
        .. Enumerable.Range(1, c).Select(i => Actor("comprador", i)),
        .. Enumerable.Range(1, v).Select(i => Actor("vendedor", i)),
        .. Enumerable.Range(1, p).Select(i => Actor("propietario", i)),
    ];

    [Fact]
    public void Con_cero_actores_no_agrega_columnas()
    {
        var max = new MaxActoresPorRolDto(0, 0, 0);
        ActoresExcelColumns.Headers(max).Should().BeEmpty();
        ActoresExcelColumns.Cells([], max).Should().BeEmpty();
        ActoresExcelColumns.Width(max).Should().Be(0);
    }

    [Fact]
    public void Un_actor_por_rol_trae_el_bloque_de_cinco_columnas_sin_numero()
    {
        var max = new MaxActoresPorRolDto(1, 1, 0);

        ActoresExcelColumns.Headers(max).Should().Equal(
            "Comprador", "Documento comprador", "Validación comprador", "Intentos comprador", "Motivo rechazo comprador",
            "Vendedor", "Documento vendedor", "Validación vendedor", "Intentos vendedor", "Motivo rechazo vendedor");
        ActoresExcelColumns.Cells(Actores(1, 1, 0), max).Should().HaveCount(10);
    }

    [Fact]
    public void Cuatro_actores_por_rol_numeran_desde_el_segundo_y_no_truncan()
    {
        var max = new MaxActoresPorRolDto(4, 4, 4);
        var headers = ActoresExcelColumns.Headers(max);

        headers.Should().HaveCount(60);
        headers.Should().Contain(["Comprador", "Comprador 2", "Documento comprador 2", "Validación comprador 2",
            "Intentos comprador 2", "Motivo rechazo comprador 2", "Propietario 4", "Motivo rechazo propietario 4"]);
        headers.Should().NotContain("Comprador 1");
        ActoresExcelColumns.Cells(Actores(4, 4, 4), max).Should().HaveCount(60);
    }

    [Fact]
    public void Actor_ausente_deja_celdas_vacias_y_sin_validar_sale_con_cero_intentos()
    {
        var max = new MaxActoresPorRolDto(2, 0, 0);
        var cells = ActoresExcelColumns.Cells([Actor("comprador", 1, "sin_validacion", 0)], max);

        cells.Take(5).Should().Equal("comprador 1", "CC 100", "Sin validar", "0", "");
        cells.Skip(5).Should().OnlyContain(c => c == "");
    }

    [Theory]
    [InlineData("CC", "1000098455", "CC 1000098455")]
    [InlineData("NIT", "890903938", "NIT 890903938")]
    [InlineData(null, "1000098455", "1000098455")]
    [InlineData("  ", "1000098455", "1000098455")]
    [InlineData("CC", null, "")]
    [InlineData("CC", "  ", "")]
    public void El_documento_va_con_su_tipo_separados_por_un_espacio(string? tipo, string? numero, string esperado)
    {
        var actor = new ActorValidacionDto("comprador", 1, "Persona", tipo, numero, "aprobado", null, 1);

        ActoresExcelColumns.Documento(actor).Should().Be(esperado);
        ActoresExcelColumns.Cells([actor], new MaxActoresPorRolDto(1, 0, 0))[1].Should().Be(esperado);
    }

    [Fact]
    public void Etiquetas_en_espanol_y_codigo_desconocido_sale_sin_validar()
    {
        ActorValidacionEstados.Etiqueta("esperando_captura").Should().Be("Esperando captura del cliente");
        ActorValidacionEstados.Etiqueta("pendiente_revision").Should().Be("Pendiente de revisión");
        // El código viejo ya no es de transporte: lo desconocido sale «Sin validar», nunca crudo.
        ActorValidacionEstados.Etiqueta("manual_activo").Should().Be("Sin validar");
        ActorValidacionEstados.Etiqueta("algo_raro").Should().Be("Sin validar");
        ActorValidacionEstados.Etiqueta(null).Should().Be("Sin validar");
    }

    [Fact]
    public void Solo_los_campos_elegidos_se_exportan()
    {
        var max = new MaxActoresPorRolDto(1, 0, 0);
        var campos = ActoresExcelColumns.Campos.Validacion | ActoresExcelColumns.Campos.Motivo;

        ActoresExcelColumns.Headers(max, campos).Should().Equal("Validación comprador", "Motivo rechazo comprador");
        ActoresExcelColumns.Cells([Actor("comprador", 1)], max, campos).Should().Equal("Aprobado", "");
    }

    // ── Excel del servidor del reporte detallado ─────────────────────────────────────────────

    private static DetailedProcedureRowDto Fila(IReadOnlyList<ActorValidacionDto>? actores) => new(
        Guid.NewGuid(), "REF-1", "Traspaso", "traspasos", "borrador", "Usuario", null, null, "1", "Persona",
        false, null, false, "", null, Actores: actores);

    private static async Task<byte[]> ExportBytesAsync(
        IReadOnlyList<DetailedProcedureRowDto> filas, MaxActoresPorRolDto max)
    {
        var repo = Substitute.For<IDetailedReportReadRepository>();
        repo.GetMaxActoresPorRolAsync(Arg.Any<DetailedReportFilter>(), Arg.Any<CancellationToken>()).Returns(max);
        repo.ExportProceduresAsync(Arg.Any<DetailedReportFilter>(),
                Arg.Any<Func<DetailedProcedureRowDto, CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var cb = call.Arg<Func<DetailedProcedureRowDto, CancellationToken, Task>>();
                foreach (var f in filas)
                    await cb(f, CancellationToken.None);
            });
        var exporter = new DetailedReportExcelExporter(repo, Substitute.For<INetworkDetailedReportReadRepository>());

        using var ms = new MemoryStream();
        var filter = new DetailedReportFilter(Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31),
            null, null, null, null, null, null, null, null, null);
        await exporter.ExportAsync(ms, filter, CancellationToken.None);
        return ms.ToArray();
    }

    private static async Task<List<List<string>>> ExportAsync(
        IReadOnlyList<DetailedProcedureRowDto> filas, MaxActoresPorRolDto max) =>
        Leer(await ExportBytesAsync(filas, max));

    private static List<List<string>> Leer(byte[] bytes)
    {
        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheet = doc.WorkbookPart!.WorksheetParts.Single().Worksheet;
        return sheet.Descendants<Row>()
            .Select(r => r.Elements<Cell>().Select(c => c.InnerText).ToList())
            .ToList();
    }

    private const int ColumnasBase = 16;

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(4, 4, 4)]
    public async Task Excel_detallado_pone_los_bloques_al_final_con_el_ancho_del_maximo(int c, int v, int p)
    {
        var max = new MaxActoresPorRolDto(c, v, p);
        var hoja = await ExportAsync([Fila(Actores(c, v, p))], max);

        hoja.Should().HaveCount(2);
        hoja[0].Should().HaveCount(ColumnasBase + 5 * (c + v + p));
        hoja[1].Should().HaveCount(hoja[0].Count);
        hoja[0].Take(ColumnasBase).Should().StartWith(["Referencia", "Tipo de trámite"]);
        hoja[0].Skip(ColumnasBase).Should().Equal(ActoresExcelColumns.Headers(max));
    }

    [Fact]
    public async Task Solo_las_celdas_de_intentos_son_numericas_en_el_xml_de_la_hoja()
    {
        var max = new MaxActoresPorRolDto(1, 1, 0);
        var bytes = await ExportBytesAsync(
            [Fila([Actor("comprador", 1, intentos: 3), Actor("vendedor", 1, "sin_validacion", 0)])], max);

        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var filas = doc.WorkbookPart!.WorksheetParts.Single().Worksheet.Descendants<Row>().ToList();
        var encabezados = filas[0].Elements<Cell>().Select(c => c.InnerText).ToList();
        var datos = filas[1].Elements<Cell>().ToList();

        var colsIntentos = encabezados
            .Select((h, i) => (h, i)).Where(x => x.h.StartsWith("Intentos ")).Select(x => x.i).ToList();
        colsIntentos.Should().HaveCount(2);
        foreach (var i in colsIntentos)
        {
            datos[i].DataType.Should().BeNull("Intentos es número, no inlineStr");
            datos[i].CellValue.Should().NotBeNull();
        }

        datos[colsIntentos[0]].CellValue!.Text.Should().Be("3");
        datos[colsIntentos[1]].CellValue!.Text.Should().Be("0");
        // El resto del archivo conserva su convención: texto en línea (incluidos los encabezados).
        datos.Where((_, i) => !colsIntentos.Contains(i)).Should()
            .OnlyContain(c => c.DataType != null && c.DataType.Value == CellValues.InlineString);
        filas[0].Elements<Cell>().Should().OnlyContain(c => c.DataType != null && c.DataType.Value == CellValues.InlineString);
    }

    [Fact]
    public async Task Dos_partes_de_un_export_con_maximos_distintos_salen_con_el_mismo_ancho()
    {
        // El máximo lo calcula el servidor sobre TODO el filtro: una parte con 1 actor y otra con 3 comparten ancho.
        var max = new MaxActoresPorRolDto(1, 3, 0);
        var parteA = await ExportAsync([Fila(Actores(1, 1, 0))], max);
        var parteB = await ExportAsync([Fila(Actores(1, 3, 0))], max);

        parteA[0].Should().Equal(parteB[0]);
        parteA[1].Should().HaveCount(parteB[1].Count);
        parteA[1].Skip(ColumnasBase + 10 + 5).Take(5).Should().OnlyContain(c => c == "", "el vendedor 2 no existe en la parte A");
    }

    [Fact]
    public async Task Ninguna_celda_menciona_el_mecanismo_de_validacion()
    {
        var actor = Actor("vendedor", 1, "rechazado", 2, "No se pudo verificar la identidad con las imágenes recibidas.");
        var max = new MaxActoresPorRolDto(0, 1, 0);
        var hoja = await ExportAsync([Fila([actor])], max);

        var todo = JsonSerializer.Serialize(hoja).ToLowerInvariant();
        foreach (var p in IdentityRejectionReasonText.Prohibidas)
            todo.Should().NotContain(p);
    }
}
