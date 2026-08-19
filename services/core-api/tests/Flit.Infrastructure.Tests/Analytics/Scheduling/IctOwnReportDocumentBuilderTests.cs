using Flit.Infrastructure.Analytics.Scheduling;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Analytics.Scheduling;

/// <summary>
/// Reportes 2.0 (HU-D, cuarta ola) — piezas puras de <see cref="IctOwnReportDocumentBuilder"/>:
/// clasificación de novedades por causa, estado de un webhook y el cálculo de porcentaje del
/// resumen. La carga cross-schema (<c>BuildNovedadesAsync</c>/<c>BuildAtascadosAsync</c>/
/// <c>BuildJobsAsync</c>/<c>BuildWebhooksAsync</c>) no es testeable sin Postgres real — mismo
/// límite ya documentado en <c>IctQueryRepositoryTests</c> para <c>AlertMetricsReadRepository</c>
/// e <c>IctQueryRepository</c>.
/// </summary>
public sealed class IctOwnReportDocumentBuilderTests
{
    [Theory]
    [InlineData("El vehículo no tiene SOAT vigente", "SOAT")]
    [InlineData("soat vencido hace 3 meses", "SOAT")]
    [InlineData("RTM no vigente para la antigüedad del vehículo", "RTM")]
    [InlineData("Sanciones activas en RNMC", "RNMC")]
    [InlineData("Falta el documento del comprador", "Documento faltante")]
    public void ClassifyCausa_ComentarioConocido_DevuelveLaCausa(string comentario, string causaEsperada)
    {
        IctOwnReportDocumentBuilder.ClassifyCausa(comentario).Should().Be(causaEsperada);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Placa con formato inválido")]
    public void ClassifyCausa_SinCoincidencia_DevuelveNull(string? comentario)
    {
        IctOwnReportDocumentBuilder.ClassifyCausa(comentario).Should().BeNull();
    }

    [Fact]
    public void ClassifyCausa_ConVariasCausasEnElMismoTexto_DevuelveLaPrimeraDeLaListaConocida()
    {
        // "SOAT" está antes que "RTM" en CausasConocidas: un comentario que combina ambas se cuenta
        // una sola vez (no doble), por la primera que matchea — evita que un pre-trámite con dos
        // problemas infle el total del resumen.
        IctOwnReportDocumentBuilder.ClassifyCausa("Sin SOAT vigente y con RTM vencida")
            .Should().Be("SOAT");
    }

    [Theory]
    [InlineData(true, true, "Entregado")]
    [InlineData(true, false, "Fallido")]
    [InlineData(false, true, "Pendiente")]
    [InlineData(false, false, "Pendiente")]
    public void EstadoWebhook_CombinacionesDeNotificadoYRespuesta_DevuelveElEstadoCorrecto(
        bool isNotified, bool responseOk, string esperado)
    {
        IctOwnReportDocumentBuilder.EstadoWebhook(isNotified, responseOk).Should().Be(esperado);
    }

    [Theory]
    [InlineData(0, 0, "0")]
    [InlineData(0, 10, "0")]
    [InlineData(5, 10, "50")]
    [InlineData(1, 3, "33.33")]
    public void Pct_CasosConocidos_RedondeaADosDecimalesEnCulturaInvariante(int part, int total, string esperado)
    {
        IctOwnReportDocumentBuilder.Pct(part, total).Should().Be(esperado);
    }
}
