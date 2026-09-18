using Flit.Infrastructure.Documents;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Documents;

/// <summary>
/// Épica #12552 — fechas de los documentos generados. <b>Revierte el criterio de la HU #11049</b>
/// (AÑO/MES/DÍA sin hora en todo el consolidado) por decisión del negocio, no por un defecto de
/// aquella historia.
///
/// <para>
/// Lo que la HU #11049 sí acertó y aquí se conserva intacto es la distinción entre los dos casos: la
/// fecha propia del sistema es un INSTANTE y la del proveedor es una fecha de CALENDARIO. Ahora la
/// diferencia además se ve, porque solo la primera lleva hora.
/// </para>
/// </summary>
public sealed class FlitDocumentDateTests
{
    [Fact]
    public void FechaDelSistema_LlevaHoraYVaEnHoraDeColombia()
    {
        // 15:42 UTC son las 10:42 en Colombia.
        var momento = new DateTimeOffset(2026, 7, 29, 15, 42, 11, TimeSpan.Zero);

        FlitDocumentDate.Format(momento).Should().Be("29/07/2026 10:42");
    }

    [Fact]
    public void FechaDelSistema_NoLlevaSegundos()
    {
        var momento = new DateTimeOffset(2026, 7, 29, 15, 42, 59, TimeSpan.Zero);

        FlitDocumentDate.Format(momento).Should().Be("29/07/2026 10:42");
    }

    [Fact]
    public void FechaDelSistemaComoDateTime_TambienSeConvierte()
    {
        // 23:59 UTC del día 5 son las 18:59 del mismo día en Colombia.
        FlitDocumentDate.Format(new DateTime(2026, 1, 5, 23, 59, 0, DateTimeKind.Utc))
            .Should().Be("05/01/2026 18:59");
    }

    [Fact]
    public void FechaDelSistema_CruzandoLaMedianocheDeColombia()
    {
        // 02:30Z del día 30 es todavía el 29 a las 21:30 en Colombia.
        var momento = new DateTimeOffset(2026, 7, 30, 2, 30, 0, TimeSpan.Zero);

        FlitDocumentDate.Format(momento).Should().Be("29/07/2026 21:30");
    }

    // Formatos que entregan los proveedores: ISO con y sin hora, día-primero y barra.
    [Theory]
    [InlineData("2026-07-29", "29/07/2026")]
    [InlineData("2026-07-29 15:42", "29/07/2026")]
    [InlineData("2026-07-29 15:42:11", "29/07/2026")]
    [InlineData("2026-07-29T15:42:11", "29/07/2026")]
    [InlineData("2026-07-29T15:42:11Z", "29/07/2026")]
    [InlineData("29/07/2026", "29/07/2026")]
    [InlineData("29/07/2026 15:42", "29/07/2026")]
    [InlineData("29-07-2026", "29/07/2026")]
    [InlineData("2026/07/29", "29/07/2026")]
    public void FechaDelProveedor_SeNormalizaAlFormatoDeCalendario(string entrada, string esperado)
    {
        FlitDocumentDate.Normalize(entrada).Should().Be(esperado);
    }

    // Día primero, no mes: 12/07 es 12 de julio (proveedores colombianos), no 7 de diciembre.
    [Fact]
    public void FechaAmbigua_SeInterpretaConElDiaPrimero()
    {
        FlitDocumentDate.Normalize("12/07/2026").Should().Be("12/07/2026");
    }

    [Fact]
    public void FechaDelProveedor_NoSeCorreDeDia()
    {
        // RN-08 y regresión de la HU #11194: el vencimiento de una póliza es una fecha de
        // calendario. Convertirla a Colombia devolvería el día anterior, y una póliza que vence el
        // 1 de enero aparecería venciendo el 31 de diciembre.
        FlitDocumentDate.Normalize("2027-01-01").Should().Be("01/01/2027");
        FlitDocumentDate.Normalize("2026-07-01").Should().Be("01/07/2026");
    }

    [Fact]
    public void ValorNoInterpretable_SeImprimeTalCual()
    {
        // Un certificado externo puede traer texto libre: preferimos mostrarlo a perderlo.
        FlitDocumentDate.Normalize("SIN INFORMACIÓN").Should().Be("SIN INFORMACIÓN");
        FlitDocumentDate.Normalize("N/A").Should().Be("N/A");
    }

    [Fact]
    public void ValorAusente_SeDevuelveIgual()
    {
        FlitDocumentDate.Normalize(null).Should().BeNull();
        FlitDocumentDate.Normalize("").Should().BeEmpty();
        FlitDocumentDate.Normalize("   ").Should().Be("   ");
    }

    [Fact]
    public void LaFechaDelProveedorSigueSinLlevarHora()
    {
        // Es lo único de la HU #11049 que NO se revierte: una vigencia no tiene hora que mostrar.
        foreach (var entrada in new[]
                 {
                     "2026-07-29 15:42:11", "29/07/2026 08:00", "2026-07-29T23:59:59Z",
                 })
        {
            FlitDocumentDate.Normalize(entrada).Should().NotContain(":", $"entrada '{entrada}'");
        }
    }
}
