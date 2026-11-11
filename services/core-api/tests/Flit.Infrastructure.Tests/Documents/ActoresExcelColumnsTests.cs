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
using static Flit.Infrastructure.Documents.ActoresExcelColumns;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Contrato v3 — columnas de validación de identidad POR ROL en el Excel del servidor: grupo comprador (incluye al
/// propietario) y grupo vendedor, agrupado por persona, sin documento, mínimo un bloque por rol, ancho común a todas las
/// partes y ninguna mención al mecanismo de validación.
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
    public void Sin_actores_sale_un_bloque_por_rol_con_los_encabezados_exactos()
    {
        var max = new MaxActoresPorRolDto(0, 0, 0);

        Headers(max, Grupo.Comprador).Should().Equal(
            "Comprador", "Validación comprador", "Intentos comprador", "Motivo rechazo comprador");
        Headers(max, Grupo.Vendedor).Should().Equal(
            "Vendedor", "Validación vendedor", "Intentos vendedor", "Motivo rechazo vendedor");
        Cells([], max, Grupo.Comprador).Should().Equal("", "", "", "");
    }

    [Fact]
    public void Traspaso_dos_compradores_y_un_vendedor_numera_desde_el_segundo()
    {
        var max = new MaxActoresPorRolDto(2, 1, 0);

        Headers(max, Grupo.Comprador).Should().Equal(
            "Comprador", "Validación comprador", "Intentos comprador", "Motivo rechazo comprador",
            "Comprador 2", "Validación comprador 2", "Intentos comprador 2", "Motivo rechazo comprador 2");
        Headers(max, Grupo.Vendedor).Should().HaveCount(4);

        var fila = Actores(2, 1, 0);
        Cells(fila, max, Grupo.Comprador).Should().Equal(
            "comprador 1", "Aprobado", "1", "", "comprador 2", "Aprobado", "1", "");
        Cells(fila, max, Grupo.Vendedor).Should().Equal("vendedor 1", "Aprobado", "1", "");
    }

    [Fact]
    public void Matricula_el_propietario_va_en_el_grupo_comprador_y_el_vendedor_queda_vacio()
    {
        var max = new MaxActoresPorRolDto(0, 0, 1);
        var fila = new[] { Actor("propietario", 1, "rechazado", 2, "Imagen borrosa.") };

        Cells(fila, max, Grupo.Comprador).Should().Equal("propietario 1", "Rechazado", "2", "Imagen borrosa.");
        Cells(fila, max, Grupo.Vendedor).Should().Equal("", "", "", "");
    }

    [Fact]
    public void En_el_grupo_comprador_el_comprador_sale_antes_que_el_propietario_aunque_su_ordinal_sea_mayor()
    {
        // Ancho 2: el servidor lo da como max(comprador, propietario) sobre todo el filtro.
        var max = new MaxActoresPorRolDto(2, 0, 1);
        var fila = new[] { Actor("propietario", 1), Actor("comprador", 2) };

        Cells(fila, max, Grupo.Comprador, Campos.Nombre).Should().Equal("comprador 2", "propietario 1");
    }

    [Theory]
    [InlineData(0, 0, 0, 1, 1)]
    [InlineData(2, 0, 1, 2, 1)]
    [InlineData(1, 0, 3, 3, 1)]
    [InlineData(4, 4, 4, 4, 4)]
    public void El_ancho_del_grupo_comprador_es_max_de_comprador_y_propietario(int c, int v, int p, int anchoC, int anchoV)
    {
        var max = new MaxActoresPorRolDto(c, v, p);

        Ancho(max, Grupo.Comprador).Should().Be(anchoC);
        Ancho(max, Grupo.Vendedor).Should().Be(anchoV);
        Headers(max, Grupo.Comprador).Should().HaveCount(4 * anchoC);
        Cells(Actores(c, v, p), max, Grupo.Comprador).Should().HaveCount(4 * anchoC);
    }

    [Fact]
    public void Cuatro_por_rol_no_trunca_y_no_hay_columna_de_documento()
    {
        var max = new MaxActoresPorRolDto(4, 4, 0);
        var headers = Headers(max, Grupo.Comprador).Concat(Headers(max, Grupo.Vendedor)).ToList();

        headers.Should().HaveCount(32);
        headers.Should().Contain(["Comprador 4", "Motivo rechazo vendedor 4"]);
        headers.Should().NotContain(h => h.StartsWith("Documento"));
    }

    [Fact]
    public void Actor_ausente_deja_celdas_vacias_y_sin_validar_sale_con_cero_intentos()
    {
        var max = new MaxActoresPorRolDto(2, 0, 0);
        var cells = Cells([Actor("comprador", 1, "sin_validacion", 0)], max, Grupo.Comprador);

        cells.Take(4).Should().Equal("comprador 1", "Sin validar", "0", "");
        cells.Skip(4).Should().OnlyContain(c => c == "");
    }

    [Fact]
    public void Sin_actores_la_columna_cae_al_nombre_viejo_del_tramite()
    {
        Cells([], new MaxActoresPorRolDto(0, 0, 0), Grupo.Comprador, Campos.Todos, "Ana Pérez")
            .Should().Equal("Ana Pérez", "", "", "");
        // Con actores en la fila el nombre viejo ya no se usa.
        Cells([Actor("vendedor", 1)], new MaxActoresPorRolDto(0, 1, 0), Grupo.Comprador, Campos.Todos, "Ana Pérez")
            .Should().OnlyContain(c => c == "");
    }

    [Fact]
    public void Solo_los_campos_elegidos_se_exportan()
    {
        var max = new MaxActoresPorRolDto(1, 0, 0);
        var campos = Campos.Validacion | Campos.Motivo;

        Headers(max, Grupo.Comprador, campos).Should().Equal("Validación comprador", "Motivo rechazo comprador");
        Cells([Actor("comprador", 1)], max, Grupo.Comprador, campos).Should().Equal("Aprobado", "");
        Headers(max, Grupo.Comprador, Campos.Nombre).Should().Equal("Comprador");
    }

    [Fact]
    public void Etiquetas_en_espanol_y_codigo_desconocido_sale_sin_validar()
    {
        ActorValidacionEstados.Etiqueta("esperando_captura").Should().Be("Esperando captura del cliente");
        ActorValidacionEstados.Etiqueta("pendiente_revision").Should().Be("Pendiente de revisión");
        ActorValidacionEstados.Etiqueta("manual_activo").Should().Be("Sin validar");
        ActorValidacionEstados.Etiqueta("algo_raro").Should().Be("Sin validar");
        ActorValidacionEstados.Etiqueta(null).Should().Be("Sin validar");
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
    [InlineData(0, 0, 0, 8)]
    [InlineData(2, 1, 0, 12)]
    [InlineData(0, 0, 1, 8)]
    [InlineData(4, 4, 4, 32)]
    public async Task Excel_detallado_pone_el_bloque_por_rol_despues_de_las_columnas_de_siempre(int c, int v, int p, int extra)
    {
        var max = new MaxActoresPorRolDto(c, v, p);
        var hoja = await ExportAsync([Fila(Actores(c, v, p))], max);

        hoja.Should().HaveCount(2);
        hoja[0].Should().HaveCount(ColumnasBase + extra);
        hoja[1].Should().HaveCount(hoja[0].Count);
        hoja[0].Take(ColumnasBase).Should().StartWith(["Referencia", "Tipo de trámite"]);
        hoja[0][ColumnasBase - 2].Should().Be("Canal");
        hoja[0][ColumnasBase].Should().Be("Comprador");
        hoja[0].Skip(ColumnasBase).Should().Equal(
            [.. Headers(max, Grupo.Comprador), .. Headers(max, Grupo.Vendedor)]);
    }

    [Fact]
    public async Task Excel_detallado_matricula_con_propietario_lo_pone_en_comprador_y_deja_vacio_el_vendedor()
    {
        var max = new MaxActoresPorRolDto(0, 0, 1);
        var hoja = await ExportAsync([Fila([Actor("propietario", 1, "aprobado", 1)])], max);

        hoja[1].Skip(ColumnasBase).Take(4).Should().Equal("propietario 1", "Aprobado", "1", "");
        hoja[1].Skip(ColumnasBase + 4).Should().OnlyContain(c => c == "");
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
        datos.Where((_, i) => !colsIntentos.Contains(i)).Should()
            .OnlyContain(c => c.DataType != null && c.DataType.Value == CellValues.InlineString);
        filas[0].Elements<Cell>().Should().OnlyContain(c => c.DataType != null && c.DataType.Value == CellValues.InlineString);
    }

    [Fact]
    public async Task Dos_partes_de_un_export_con_maximos_distintos_salen_con_el_mismo_ancho()
    {
        // El máximo lo calcula el servidor sobre TODO el filtro: una parte con 1 vendedor y otra con 3 comparten ancho.
        var max = new MaxActoresPorRolDto(1, 3, 0);
        var parteA = await ExportAsync([Fila(Actores(1, 1, 0))], max);
        var parteB = await ExportAsync([Fila(Actores(1, 3, 0))], max);

        parteA[0].Should().Equal(parteB[0]);
        parteA[1].Should().HaveCount(parteB[1].Count);
        // El vendedor 2 y 3 no existen en la parte A: celdas vacías.
        parteA[1].Skip(ColumnasBase + 4 + 4 + 4).Should().OnlyContain(c => c == "");
    }

    [Fact]
    public async Task Ninguna_celda_menciona_el_mecanismo_de_validacion()
    {
        var actor = Actor("vendedor", 1, "esperando_captura", 2, "No se pudo verificar la identidad con las imágenes recibidas.");
        var max = new MaxActoresPorRolDto(0, 1, 0);
        var hoja = await ExportAsync([Fila([actor])], max);

        var todo = JsonSerializer.Serialize(hoja).ToLowerInvariant();
        foreach (var p in IdentityRejectionReasonText.Prohibidas)
            todo.Should().NotContain(p);
    }
}
