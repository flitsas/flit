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

/// <summary>Reportes programados de Consulta: bloques de validación de identidad por persona al final de la hoja.</summary>
public sealed class CompanyQueryReportColumnsActoresTests
{
    private static CompanyQueryRowDto Row(IReadOnlyList<ActorValidacionDto> actores) => new(
        Guid.NewGuid(), "REF-1", "ABC123", null, null, null, Guid.NewGuid(), "Empresa", Guid.NewGuid(), "Traspaso",
        "aprobado", false, false, 0, null, null, false, null, false, [], false, null, "bilateral", "Usuario",
        DateTimeOffset.UtcNow, null, null, null, null, null, null, 0, actores);

    private static ActorValidacionDto Actor(string rol, int ordinal, string estado = "aprobado", int intentos = 1,
        string? motivo = null) => new(rol, ordinal, $"{rol} {ordinal}", "CC", $"{ordinal}00", estado, motivo, intentos);

    private static readonly string[] Todas =
        ["referencia", "validacion_persona", "validacion_estado", "validacion_intentos", "validacion_motivo"];

    [Fact]
    public void Sin_columnas_de_validacion_elegidas_no_agrega_bloques()
    {
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", ["referencia"],
            [Row([Actor("comprador", 1)])], new MaxActoresPorRolDto(1, 0, 0));

        sheet.Columns.Select(c => c.Header).Should().Equal("Radicado");
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(4, 4, 4)]
    public void Los_bloques_van_al_final_y_crecen_con_el_maximo(int c, int v, int p)
    {
        var actores = new List<ActorValidacionDto>();
        actores.AddRange(Enumerable.Range(1, c).Select(i => Actor("comprador", i)));
        actores.AddRange(Enumerable.Range(1, v).Select(i => Actor("vendedor", i)));
        actores.AddRange(Enumerable.Range(1, p).Select(i => Actor("propietario", i)));

        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", Todas, [Row(actores)], new MaxActoresPorRolDto(c, v, p));

        var headers = sheet.Columns.Select(x => x.Header).ToList();
        headers.Should().HaveCount(1 + 5 * (c + v + p));
        headers[0].Should().Be("Radicado");
        sheet.Rows.Single().Should().HaveCount(headers.Count);
        if (c > 0)
            headers.Skip(1).Take(5).Should().Equal(
                "Comprador", "Documento comprador", "Validación comprador", "Intentos comprador", "Motivo rechazo comprador");
    }

    [Fact]
    public void Intentos_sale_como_numero_y_el_actor_sin_validar_con_cero()
    {
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", Todas,
            [Row([Actor("comprador", 1, "sin_validacion", 0)])], new MaxActoresPorRolDto(1, 0, 0));

        var fila = sheet.Rows.Single();
        fila[4].Should().Be(TabularWorkbookWriter.Cell.Of(0));
        fila[3].Should().Be(TabularWorkbookWriter.Cell.Of("Sin validar"));
    }

    [Fact]
    public void El_documento_va_con_su_tipo_y_los_intentos_son_numero_en_los_reportes_programados()
    {
        var actores = new List<ActorValidacionDto>
        {
            new("comprador", 1, "Ana", "CC", "1000098455", "aprobado", null, 2),
            new("vendedor", 1, "Banco", "NIT", "890903938", "aprobado", null, 1),
            new("vendedor", 2, "Sin tipo", null, "55", "aprobado", null, 1),
        };
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", Todas, [Row(actores)], new MaxActoresPorRolDto(1, 2, 0));

        var fila = sheet.Rows.Single();
        fila[2].Should().Be(TabularWorkbookWriter.Cell.Of("CC 1000098455"));
        fila[4].Should().Be(TabularWorkbookWriter.Cell.Of(2), "Intentos va como número");
        fila[7].Should().Be(TabularWorkbookWriter.Cell.Of("NIT 890903938"));
        fila[12].Should().Be(TabularWorkbookWriter.Cell.Of("55"));
    }

    [Fact]
    public void Dos_paginas_con_maximos_distintos_comparten_el_ancho_que_les_da_el_servidor()
    {
        var max = new MaxActoresPorRolDto(1, 2, 0);
        var a = CompanyQueryReportColumns.BuildSheet("Consulta", Todas, [Row([Actor("comprador", 1)])], max);
        var b = CompanyQueryReportColumns.BuildSheet("Consulta", Todas,
            [Row([Actor("comprador", 1), Actor("vendedor", 1), Actor("vendedor", 2)])], max);

        a.Columns.Should().Equal(b.Columns);
        a.Rows.Single().Should().HaveCount(b.Rows.Single().Count);
    }

    [Fact]
    public void Solo_los_campos_marcados_se_exportan()
    {
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", ["validacion_estado"],
            [Row([Actor("comprador", 1)])], new MaxActoresPorRolDto(1, 0, 0));

        sheet.Columns.Select(c => c.Header).Should().Contain("Validación comprador");
        sheet.Columns.Select(c => c.Header).Should().NotContain("Intentos comprador").And.NotContain("Comprador");
    }

    [Fact]
    public void Ninguna_celda_menciona_el_mecanismo_de_validacion()
    {
        var sheet = CompanyQueryReportColumns.BuildSheet("Consulta", Todas,
            [Row([Actor("vendedor", 1, "rechazado", 2, "No se pudo verificar la identidad con las imágenes recibidas.")])],
            new MaxActoresPorRolDto(0, 1, 0));

        var todo = JsonSerializer.Serialize(sheet.Rows).ToLowerInvariant();
        foreach (var p in IdentityRejectionReasonText.Prohibidas)
            todo.Should().NotContain(p);
    }
}
