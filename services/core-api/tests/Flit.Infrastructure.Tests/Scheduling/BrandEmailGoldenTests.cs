using Flit.Analytics.Application.Dtos;
using Flit.Infrastructure.Analytics.Scheduling;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tests.Shared;
using Xunit;

namespace Flit.Infrastructure.Tests.Scheduling;

/// <summary>
/// HU #12428 AC2/AC3/AC6/AC8 — congela ASUNTO y CUERPO de <c>analytics.scheduled-report</c>
/// renderizado con un tema de marca FIJO (cuarto punto de inyección, Analítica). No toca ningún
/// golden de <see cref="AnalyticsEmailGoldenTests"/> (variante <c>Flit</c>).
/// <para>
/// <b>Si un refactor obliga a editar este <c>.golden.txt</c>, el refactor está mal.</b> Solo un
/// cambio DELIBERADO del chrome de marca justifica regenerarlo, con <c>GOLDEN_UPDATE=1</c> en un
/// commit aparte que diga por qué.
/// </para>
/// </summary>
public sealed class BrandEmailGoldenTests
{
    private static readonly EmailTheme BrandTheme = new(
        EmailThemeKind.Brand,
        "Movilidad Andina",
        "https://dev.flitsas.online/api/v1/public/branding/logos/11111111-1111-4111-8111-111111111111",
        "#0B3D91",
        "#1FA2FF",
        "#FFFFFF",
        7);

    [Fact]
    public void InformeProgramado_brand_conserva_asunto_y_cuerpo()
    {
        var overview = new List<CategoryMetricsDto>
        {
            new("matriculas", 7, new List<StatusCountDto> { new("aprobado", 5), new("rechazado", 2) }),
        };
        var topProducers = new List<TopProducerDto>
        {
            new(Guid.Empty, "Radicador de prueba", 6, 5, 1),
        };

        var (subject, html) = SchedulerEmailComposer.BuildScheduledReport(
            "Informe semanal", "resumen", "01/01/2026 - 07/01/2026", overview, topProducers, BrandTheme);

        EmailGolden.Assert(
            new EmailMessage(Guid.Empty, "analytics.scheduled-report", "destinatario@ejemplo.test", "Destinatario", subject, html),
            "analytics-scheduled-report-brand");
    }
}
