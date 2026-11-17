using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Flit.Analytics.Application.CompanyQueries;
using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Infrastructure.Documents.Reports;
using Flit.Tramites.Domain.Identity;
using Xunit;

namespace Flit.Infrastructure.Tests.Scheduling;

/// <summary>Contrato v3 — reportes programados de Consulta: bloques de validación de identidad por rol, en su sitio.</summary>
public sealed class CompanyQueryReportColumnsActoresTests
{
    private static CompanyQueryRowDto Row(IReadOnlyList<ActorValidacionDto> actores, string? comprador = null,
        string? vendedor = null) => new(
        Guid.NewGuid(), "REF-1", "ABC123", null, null, null, Guid.NewGuid(), "Empresa", Guid.NewGuid(), "Traspaso",
        "aprobado", false, false, 0, comprador, vendedor, false, null, false, [], false, null, "bilateral", "Usuario",
        DateTimeOffset.UtcNow, null, null, null, null, null, null, 0, actores);

    private static ActorValidacionDto Actor(string rol, int ordinal, string estado = "aprobado", int intentos = 1,
        string? motivo = null) => new(rol, ordinal, $"{rol} {ordinal}", "CC", $"{ordinal}00", estado, motivo, intentos);

    private static readonly string[] Todas =
    [
        "referencia", "comprador", "validacion_comprador", "intentos_comprador", "motivo_comprador",
        "vendedor", "validacion_vendedor", "intentos_vendedor", "motivo_vendedor", "estado",
    ];

    private static List<string> Headers(TabularWorkbookWriter.Sheet s) => s.Columns.Select(c => c.Header).ToList();

    [Fact]
    public void Traspaso_dos_compradores_y_un_vendedor_el_bloque_ocupa_el_lugar_de_la_columna_y_agrupa_por_persona()
    {
        var actores = new[] { Actor("comprador", 1), Actor("comprador", 2, "rechazado", 2, "Imagen borrosa."), Actor("vendedor", 1) };
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", Todas, [Row(actores)], new MaxActoresPorRolDto(2, 1, 0));

        Headers(sheet).Should().Equal(
            "Radicado",
            "Comprador", "Validación comprador", "Intentos comprador", "Motivo rechazo comprador",
            "Comprador 2", "Validación comprador 2", "Intentos comprador 2", "Motivo rechazo comprador 2",
            "Vendedor", "Validación vendedor", "Intentos vendedor", "Motivo rechazo vendedor",
            "Estado");
        var fila = sheet.Rows.Single();
        fila.Should().HaveCount(14);
        fila[5].Should().Be(TabularWorkbookWriter.Cell.Of("comprador 2"));
        fila[6].Should().Be(TabularWorkbookWriter.Cell.Of("Rechazado"));
        fila[7].Should().Be(TabularWorkbookWriter.Cell.Of(2), "Intentos va como número");
        fila[8].Should().Be(TabularWorkbookWriter.Cell.Of("Imagen borrosa."));
    }

    [Fact]
    public void Matricula_el_propietario_va_en_comprador_y_el_vendedor_queda_vacio()
    {
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", Todas,
            [Row([Actor("propietario", 1)])], new MaxActoresPorRolDto(0, 0, 1));

        var fila = sheet.Rows.Single();
        fila[1].Should().Be(TabularWorkbookWriter.Cell.Of("propietario 1"));
        fila[2].Should().Be(TabularWorkbookWriter.Cell.Of("Aprobado"));
        fila.Skip(5).Take(4).Should().OnlyContain(c => c is TabularWorkbookWriter.Cell.Vacia);
    }

    [Fact]
    public void Consulta_vieja_solo_con_comprador_sale_con_nombres_comprador_comprador_2()
    {
        var actores = new[] { Actor("comprador", 1), Actor("comprador", 2) };
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", ["referencia", "comprador"],
            [Row(actores)], new MaxActoresPorRolDto(2, 0, 0));

        Headers(sheet).Should().Equal("Radicado", "Comprador", "Comprador 2");
        sheet.Rows.Single().Skip(1).Should().Equal(
            TabularWorkbookWriter.Cell.Of("comprador 1"), TabularWorkbookWriter.Cell.Of("comprador 2"));
    }

    [Fact]
    public void Consulta_vieja_sin_actores_cae_al_nombre_del_tramite()
    {
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", ["comprador", "vendedor"],
            [Row([], "Ana Pérez", "Víctor Vega")], default);

        Headers(sheet).Should().Equal("Comprador", "Vendedor");
        sheet.Rows.Single().Should().Equal(
            TabularWorkbookWriter.Cell.Of("Ana Pérez"), TabularWorkbookWriter.Cell.Of("Víctor Vega"));
    }

    [Fact]
    public void Una_columna_oculta_no_sale_en_ningun_bloque()
    {
        var actores = new[] { Actor("comprador", 1), Actor("comprador", 2) };
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta",
            ["comprador", "validacion_comprador", "motivo_comprador"], [Row(actores)], new MaxActoresPorRolDto(2, 0, 0));

        Headers(sheet).Should().Equal(
            "Comprador", "Validación comprador", "Motivo rechazo comprador",
            "Comprador 2", "Validación comprador 2", "Motivo rechazo comprador 2");
        Headers(sheet).Should().NotContain(h => h.StartsWith("Intentos"));
    }

    [Fact]
    public void Si_solo_se_pide_un_campo_el_bloque_ocupa_el_lugar_de_esa_columna()
    {
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", ["estado", "intentos_vendedor", "referencia"],
            [Row([Actor("vendedor", 1, intentos: 4)])], new MaxActoresPorRolDto(0, 1, 0));

        Headers(sheet).Should().Equal("Estado", "Intentos vendedor", "Radicado");
        sheet.Rows.Single()[1].Should().Be(TabularWorkbookWriter.Cell.Of(4));
    }

    [Fact]
    public void Los_ids_viejos_validacion_persona_se_ignoran()
    {
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta",
            ["referencia", "validacion_persona", "validacion_estado", "validacion_intentos", "validacion_motivo"],
            [Row([Actor("comprador", 1)])], new MaxActoresPorRolDto(1, 0, 0));

        Headers(sheet).Should().Equal("Radicado");
    }

    [Fact]
    public void Dos_partes_de_un_export_por_lotes_con_maximos_distintos_comparten_el_ancho()
    {
        var max = new MaxActoresPorRolDto(1, 2, 0);
        var a = CompanyQueryReportColumns.BuildSheet("Consulta", Todas, [Row([Actor("comprador", 1)])], max);
        var b = CompanyQueryReportColumns.BuildSheet("Consulta", Todas,
            [Row([Actor("comprador", 1), Actor("vendedor", 1), Actor("vendedor", 2)])], max);

        a.Columns.Should().Equal(b.Columns);
        a.Rows.Single().Should().HaveCount(b.Rows.Single().Count);
    }

    [Fact]
    public void Ninguna_celda_ni_encabezado_menciona_el_mecanismo_de_validacion()
    {
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", Todas,
            [Row([Actor("vendedor", 1, "pendiente_revision", 2, "No se pudo verificar la identidad con las imágenes recibidas.")])],
            new MaxActoresPorRolDto(0, 1, 0));

        var todo = JsonSerializer.Serialize(new { h = Headers(sheet), r = sheet.Rows }).ToLowerInvariant();
        foreach (var p in IdentityRejectionReasonText.Prohibidas)
            todo.Should().NotContain(p);
    }
}
