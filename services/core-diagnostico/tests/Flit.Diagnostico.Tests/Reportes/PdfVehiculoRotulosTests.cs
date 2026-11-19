using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Plataforma;
using FluentAssertions;
using Xunit;

namespace Flit.Diagnostico.Tests.Reportes;

/// <summary>
/// Punto 5 (ajustes 2026-11-11) en el PDF del vehículo: el dictamen pendiente dice «Análisis en preparación» (no nombra la IA),
/// una sección de una fuente sin título cae al rótulo genérico del diccionario (nunca al código) y los estados que entregan las
/// fuentes en mayúsculas (VIGENTE, PENDIENTE, NO VIGENTE…) salen en tipo oración por <see cref="RotulosDiagnostico"/>.
/// Sin base de datos: genera el PDF en memoria.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~PdfVehiculoRotulosTests
/// </code>
/// </example>
public sealed class PdfVehiculoRotulosTests
{
    private static readonly DateTimeOffset GeneradoEn = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Dictamen_pendiente_dice_analisis_en_preparacion_y_no_nombra_la_ia()
    {
        // HU-3: «Análisis en preparación» solo mientras el dictamen sigue en la cola de redacción.
        var contenido = Contenido() with { Dictamen = new DictamenEnHuella("BLOQUEADO", "PENDIENTE", "qa51 parrafo de prueba", EnCola: true) };
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(contenido, GeneradoEn));

        PdfDePrueba.Tiene(texto, "Análisis en preparación").Should().BeTrue(texto);
        texto.Should().NotContainAny(PdfDePrueba.Plano("redacción IA"), PdfDePrueba.Plano("Redacción IA"));
    }

    /// <summary>HU-3: en cola → «Análisis en preparación» y nunca «Sin análisis». El texto del modelo sale literal (SP-8).</summary>
    [Fact]
    public void HU3_pendiente_en_cola_dice_analisis_en_preparacion()
    {
        var contenido = Contenido() with { Dictamen = new DictamenEnHuella("VIABLE", "PENDIENTE", "<b>parrafo</b> **hu3**", EnCola: true) };
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(contenido, GeneradoEn));

        PdfDePrueba.Tiene(texto, RotulosDiagnostico.AnalisisEnPreparacion).Should().BeTrue(texto);
        PdfDePrueba.Tiene(texto, "Sin análisis").Should().BeFalse(texto);
        PdfDePrueba.Tiene(texto, "<b>parrafo</b> **hu3**").Should().BeTrue("el texto no se interpreta como HTML ni markdown: " + texto);
    }

    /// <summary>HU-3: PENDIENTE fuera de cola (nunca se redactará) → literal exacto «Sin análisis», sin «Análisis en preparación».</summary>
    [Fact]
    public void HU3_pendiente_fuera_de_cola_dice_sin_analisis()
    {
        var contenido = Contenido() with { Dictamen = new DictamenEnHuella("BLOQUEADO", "PENDIENTE", "parrafo hu3", EnCola: false) };
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(contenido, GeneradoEn));

        PdfDePrueba.Tiene(texto, "Sin análisis").Should().BeTrue(texto);
        PdfDePrueba.Tiene(texto, RotulosDiagnostico.AnalisisEnPreparacion).Should().BeFalse(texto);
    }

    /// <summary>HU-3: COMPLETADA → sin rótulo, esté o no la marca de cola.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HU3_completada_no_lleva_rotulo(bool enCola)
    {
        var contenido = Contenido() with { Dictamen = new DictamenEnHuella("VIABLE", "COMPLETADA", "parrafo hu3", EnCola: enCola) };
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(contenido, GeneradoEn));

        PdfDePrueba.Tiene(texto, "Sin análisis").Should().BeFalse(texto);
        PdfDePrueba.Tiene(texto, RotulosDiagnostico.AnalisisEnPreparacion).Should().BeFalse(texto);
        PdfDePrueba.Tiene(texto, "parrafo hu3").Should().BeTrue(texto);
    }

    /// <summary>HU-3 no suma su propia subida: la versión queda en la 8 de los ajustes del PDF; el cambio de rótulo lo invalida la huella.</summary>
    [Fact]
    public void HU3_no_sube_la_version_de_plantilla() => PdfVehiculo.VersionPlantilla.Should().Be(8);

    [Fact]
    public void Seccion_de_fuente_sin_titulo_usa_el_rotulo_generico_y_nunca_el_codigo()
    {
        var texto = PdfDePrueba.Texto(PdfVehiculo.Generar(Contenido(), GeneradoEn));

        texto.Should().NotContain("FUENTE_P5_RARA");
        PdfDePrueba.Tiene(texto, PdfVehiculo.TituloVisible(RotulosDiagnostico.SinClasificar)).Should().BeTrue(texto);
    }

    [Fact]
    public void Estados_en_mayusculas_de_la_fuente_salen_en_tipo_oracion()
    {
        var texto = PdfDePrueba.SinPies(PdfDePrueba.Texto(PdfVehiculo.Generar(Contenido(), GeneradoEn)));

        PdfDePrueba.Tiene(texto, "Pendiente").Should().BeTrue(texto);
        PdfDePrueba.Tiene(texto, "Pagado").Should().BeTrue(texto);
        PdfDePrueba.Tiene(texto, "PENDIENTE").Should().BeFalse(texto);
        PdfDePrueba.Tiene(texto, "PAGADO").Should().BeFalse(texto);
    }

    [Fact]
    public void La_version_de_plantilla_sube_por_el_cambio_de_rotulos() => PdfVehiculo.VersionPlantilla.Should().BeGreaterThanOrEqualTo(7);

    private static ContenidoReporteVehiculo Contenido()
    {
        var dato = new DateTimeOffset(2026, 11, 11, 14, 0, 0, TimeSpan.Zero);
        var encabezado = new EncabezadoReporte(Guid.Parse("0192ab34-5c6d-7e8f-9a0b-1c2d3e4f5a6c"), "Lote rótulos", "Empresa de prueba", "Ana Prueba",
            dato, "Diagnóstico Simple", "TST551", "PLACA_CARRO", "TST551", null);
        ComparendoDetalle[] comparendos =
        [
            new("TST-C-5501", "2026-10-01", "468500", "PENDIENTE", "Organismo de prueba"),
            new("TST-C-5502", "2026-10-02", "200000", "PAGADO", "Organismo de prueba"),
        ];
        return new ContenidoReporteVehiculo(encabezado,
        [
            new("SIMIT_VEHICULO", "POR_VEHICULO", EstadoSeccion.Completado, null, null, dato, new ResultadoSimitDetalle("AMARILLO", 2, comparendos, false), null),
            new("FUENTE_P5_RARA", "POR_VEHICULO", EstadoSeccion.Pendiente, null, null, null, null, null),
        ]);
    }
}
