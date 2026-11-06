using System.Reflection;
using Flit.Comparendos.Api.Modulos.Catalogos;
using Flit.Comparendos.Api.Modulos.Contraventor;
using Flit.Comparendos.Api.Modulos.Ocr;
using Flit.Comparendos.Api.Semillas;
using FluentAssertions;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// Certificación GDC Soluciones ya cubiertas en develop (Feature #14363 / HUs #14369–#14373).
/// No reimplementan lógica: fijan invariantes del código vigente.
/// </summary>
public sealed class GdcCertificacionTests
{
    /// <summary>HU #14370 / GDC ID10: límites de lote OCR (20 MB / 200 archivos).</summary>
    [Fact]
    public void ID10_LimitesOcr_Son20MbY200Archivos()
    {
        ServicioOcr.MaximoDocumentos.Should().Be(200);
        ServicioOcr.MaximoBytes.Should().Be(20L * 1024 * 1024);
    }

    /// <summary>HU #14372 / GDC ID14: prompt semilla exige extracción literal sin inventar ceros.</summary>
    [Fact]
    public void ID14_PromptOcr_ExigeLiteralSinInventarCeros()
    {
        PromptOcrSemilla.Texto.Should().Contain("No inventes nada");
        PromptOcrSemilla.Texto.Should().Contain("sin quitar ceros a la izquierda");
        PromptOcrSemilla.Texto.Should().Contain("numero_comparendo");
    }

    /// <summary>HU #14371 / GDC ID13: el procesador OCR solo encola; duplicados viven en ServicioOcr.</summary>
    [Fact]
    public void ID13_Duplicados_NoVivenEnProcesadorOcr()
    {
        var metodos = typeof(ProcesadorOcr).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        metodos.Should().NotContain(n => n.Contains("Duplicado", StringComparison.OrdinalIgnoreCase));
        typeof(ServicioOcr).GetMethod(nameof(ServicioOcr.ProcesarAsync)).Should().NotBeNull();
        typeof(ServicioOcr).GetMethod(nameof(ServicioOcr.RegistrarAsync)).Should().NotBeNull();
    }

    /// <summary>HU #14373 / GDC ID20: topes de 3 intentos OCR y reproceso; job de reintento de contraventor existe.</summary>
    [Fact]
    public void ID20_TopesDeReintento_SonTres()
    {
        ServicioOcr.MaximoIntentos.Should().Be(3);
        ServicioOcr.MaximoReprocesos.Should().Be(3);
        typeof(ReintentoContraventor).Should().NotBeNull();
        typeof(ReintentoContraventor).GetInterfaces().Should().Contain(i => i.Name.Contains("Ejecutor", StringComparison.Ordinal));
    }

    /// <summary>HU #14369 / GDC ID3: MapeadorOt resuelve por texto/número; SincronizacionOrganismos no mapea comparendos.</summary>
    [Fact]
    public void ID3_MapeadorOt_TieneResolucionPorTextoYNumero()
    {
        typeof(MapeadorOt).GetMethod(nameof(MapeadorOt.MapearAsync))!.GetParameters().Select(p => p.Name).Should().Contain("numeroComparendo");
        typeof(MapeadorOt).GetMethod(nameof(MapeadorOt.PorNumeroAsync)).Should().NotBeNull();
        typeof(SincronizacionOrganismos).GetMethod("MapearAsync").Should().BeNull();
    }
}
