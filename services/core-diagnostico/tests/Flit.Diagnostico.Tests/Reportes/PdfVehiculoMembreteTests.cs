using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// Membrete del PDF del vehículo (oleada «ajustes-pdf-y-grilla-diagnostico», 2026-11-19, plantilla 9): cabecera centrada con
/// «DIAGNÓSTICO VEHICULAR», «Placa: …» (o «VIN: …») y «Fecha y hora de generación: dd/MM/yyyy HH:mm:ss», sin logo ni Diagnóstico
/// ID; el dictamen al final en un recuadro; todas las fechas en dd/MM/yyyy sin hora salvo la de generación; y el pie
/// «Certificado de Diagnóstico  Id. {GUID}   Página X/Y». Sin base de datos: reutiliza el contenido ficticio de
/// <see cref="PdfVehiculoAjustesPdfTests.Contenido"/>.
/// </summary>
/// <example>
/// <code>
/// scripts/dev/t.sh dotnet services/core-diagnostico/tests/Flit.Diagnostico.Tests/Flit.Diagnostico.Tests.csproj --sin-bd --filter "FullyQualifiedName~PdfVehiculoMembreteTests"
/// </code>
/// </example>
public sealed class PdfVehiculoMembreteTests
{
    private static readonly DateTimeOffset GeneradoEn = new(2026, 11, 12, 17, 9, 49, TimeSpan.Zero);

    private static readonly Guid Id = Guid.Parse("01a1d04b-3a74-74ad-a6c4-4a8fbb008981");

    // 1a — la cabecera trae solo placa (o VIN) y la fecha y hora de generación en hora Colombia; el ID ya no va ahí.
    [Fact]
    public void Cabecera_trae_placa_y_fecha_y_hora_de_generacion_sin_diagnostico_id()
    {
        var filas = PdfVehiculo.FilasCabecera(PdfVehiculoAjustesPdfTests.Contenido().Encabezado, GeneradoEn);

        filas.Should().Equal(("Placa", "TST319"), ("Fecha y hora de generación", "12/11/2026 12:09:49"));
        filas.Should().NotContain(f => f.Etiqueta == "Diagnóstico ID");
    }

    [Fact]
    public void Cabecera_sin_placa_trae_el_vin_o_el_identificador()
    {
        var e = PdfVehiculoAjustesPdfTests.Contenido().Encabezado;
        PdfVehiculo.FilasCabecera(e with { Placa = null }, GeneradoEn)[0].Should().Be(("VIN", "MBHZF63S0PG000001"));
        PdfVehiculo.FilasCabecera(e with { Placa = null, Vin = null, Identificador = "IDENT-0001" }, GeneradoEn)[0].Should().Be(("VIN", "IDENT-0001"));
    }

    [Fact]
    public void Cabecera_en_el_pdf_centrada_con_titulo_en_mayusculas_y_sin_id()
    {
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(PdfVehiculoAjustesPdfTests.Contenido(), GeneradoEn));

        PdfDePrueba.Tiene(texto, "DIAGNÓSTICO VEHICULAR Placa: TST319 Fecha y hora de generación: 12/11/2026 12:09:49 DATOS DEL VEHÍCULO")
            .Should().BeTrue(texto);
        PdfDePrueba.Tiene(texto, "Diagnóstico ID").Should().BeFalse(texto);
    }

    // 1c — Fecha() sin hora; «Consultado el …» sin «(hora Colombia)».
    [Fact]
    public void Fechas_sin_hora_salvo_la_de_generacion()
    {
        PdfVehiculo.Fecha(new DateTimeOffset(2026, 11, 13, 3, 30, 0, TimeSpan.Zero)).Should().Be("12/11/2026", "hora Colombia, sin hora");
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(PdfVehiculoAjustesPdfTests.Contenido(), GeneradoEn));

        PdfDePrueba.Tiene(texto, "Consultado el 12/11/2026").Should().BeTrue(texto);
        texto.Should().NotContain(PdfDePrueba.Plano("(hora Colombia)"));
        System.Text.RegularExpressions.Regex.Matches(texto, @"\d{2}:\d{2}").Should().ContainSingle("solo la fecha de generación lleva hora: " + texto);
    }

    // 1c — las fechas crudas de las grillas RUNT ampliadas y de SIMIT pasan por FechaCorta.
    [Fact]
    public void Fechas_crudas_de_runt_y_simit_salen_dd_MM_yyyy()
    {
        var c = PdfVehiculoAjustesPdfTests.Contenido();
        var secciones = c.Secciones.ToList();
        var i = secciones.FindIndex(s => s.Fuente == "SIMIT_VEHICULO");
        secciones[i] = secciones[i] with
        {
            Resultado = new ResultadoSimitDetalle("AMARILLO", 1, [new ComparendoDetalle("C-0001", "2026-09-30T08:00:00.000-05:00", "468500", "PENDIENTE", "ORG")], false),
        };
        var contenido = c with
        {
            Secciones = secciones,
            DetalleRunt = c.DetalleRunt! with
            {
                Soat = [new DetalleRuntSoat("VIGENTE", "SOAT-9", "2026-02-03", "2026-02-02T10:00:00", "2027-02-02", "ASEGURADORA")],
                Solicitudes = [new DetalleRuntSolicitud("APROBADA", "STRIA", "S-1", "2025-07-14T09:12:00.000-05:00", "TRASPASO")],
            },
        };
        var texto = PdfDePrueba.SinPies(PdfDePrueba.Texto(PdfVehiculo.Generar(contenido, GeneradoEn)));

        foreach (var fecha in new[] { "03/02/2026", "02/02/2026", "02/02/2027", "14/07/2025", "30/09/2026" })
            PdfDePrueba.Tiene(texto, fecha).Should().BeTrue($"«{fecha}» en el PDF: {texto}");
        texto.Should().NotContainAny("2026-02-03", "2025-07-14", "2026-09-30", "T08:00", "T09:12");
    }

    // 1d — pie: «Certificado de Diagnóstico  Id. {GUID}   Página X/Y» en una línea.
    [Fact]
    public void Pie_trae_certificado_id_completo_y_pagina_x_de_y_con_barra()
    {
        PdfVehiculo.TextoIdPie(PdfVehiculoAjustesPdfTests.Contenido().Encabezado).Should().Be($"  Id. {Id}");
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(PdfVehiculoAjustesPdfTests.Contenido(), GeneradoEn));

        System.Text.RegularExpressions.Regex.IsMatch(texto, $@"CertificadodeDiagnósticoId\.{Id}Página1/\d+").Should().BeTrue(texto);
        texto.Should().NotMatchRegex(@"Página\d+de\d+");
    }

    // 1b — el dictamen va al final, después de la última sección, con «DICTAMEN:», el veredicto, la marca y viñetas.
    [Fact]
    public void Dictamen_va_al_final_en_su_recuadro()
    {
        const string primero = "mb11 alfa parrafo del dictamen";
        const string segundo = "mb22 beta parrafo del dictamen";
        var contenido = PdfVehiculoAjustesPdfTests.Contenido() with
        {
            Dictamen = new DictamenEnHuella("BLOQUEADO", "COMPLETADA", primero + "\n" + segundo, Version: 2, AjustadoEn: GeneradoEn),
        };
        var texto = PdfDePrueba.SinPies(PdfDePrueba.Texto(PdfVehiculo.Generar(contenido, GeneradoEn)));

        var orden = new[] { "AVALÚO COMERCIAL", "DICTAMEN:", "Bloqueado", "Ajustado por FLIT el 12/11/2026", "•" + primero, "•" + segundo };
        var desde = 0;
        foreach (var literal in orden)
        {
            var i = texto.IndexOf(PdfDePrueba.Plano(literal), desde, StringComparison.Ordinal);
            i.Should().BeGreaterThanOrEqualTo(0, $"«{literal}» aparece después del anterior: {texto}");
            desde = i;
        }

        texto.IndexOf("DICTAMEN:", StringComparison.Ordinal).Should().BeGreaterThan(texto.IndexOf("DATOSDELVEHÍCULO", StringComparison.Ordinal));
    }

    [Fact]
    public void Iconos_y_colores_del_veredicto()
    {
        PdfVehiculo.IconoVeredicto("BLOQUEADO").Svg.Should().Contain("<line");
        PdfVehiculo.IconoVeredicto("BLOQUEADO").Color.Should().Be("#E5484D");
        PdfVehiculo.IconoVeredicto("VIABLE").Svg.Should().Contain("<path");
        PdfVehiculo.IconoVeredicto("VIABLE").Color.Should().Be(Flit.Diagnostico.Api.Modulos.Reportes.Marca.FlitDocumentTheme.Verde);
        PdfVehiculo.IconoVeredicto("OTRO").Svg.Should().BeNull();
        PdfVehiculo.RecuadroSvg(200, 80).Should().Contain("rx=\"8\"").And.Contain("stroke-width=\"1\"");
    }

    [Fact]
    public void Version_de_plantilla_es_9() => PdfVehiculo.VersionPlantilla.Should().Be(9);
}
