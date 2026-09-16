using Flit.Infrastructure.Notifications.Preview;
using Flit.Infrastructure.Notifications.Tramites;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tests.Shared;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Tramites;

/// <summary>
/// HU #12428 AC2/AC3/AC6 — congela ASUNTO y CUERPO de <c>tramites.aprobado</c> y
/// <c>tramites.asignacion-placa</c> renderizados con un tema de marca FIJO, carácter a carácter.
/// No toca ningún golden de <see cref="TramiteEmailGoldenTests"/> (variante <c>Flit</c>/Renting).
/// <para>
/// <b>Si un refactor obliga a editar un <c>.golden.txt</c>, el refactor está mal.</b> Solo un
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
    public void Aprobado_brand_conserva_asunto_y_cuerpo()
    {
        var (subject, html) = TramiteCambioEstadoEmailComposer.ComposeFlit(
            TramiteEmailPreviewSample.SampleAprobado, "https://cdn.flit.test/email-assets", BrandTheme);

        EmailGolden.Assert(
            new EmailMessage(Guid.Empty, TramiteCambioEstadoEmailComposer.TemplateIdAprobado, "destinatario@ejemplo.test", "Destinatario", subject, html),
            "tramites-aprobado-brand");
    }

    [Fact]
    public void AsignacionPlaca_brand_conserva_asunto_y_cuerpo()
    {
        var (subject, html) = AsignacionPlacaEmailComposer.ComposeFlit(
            AsignacionPlacaEmailPreviewSample.Sample, "https://cdn.flit.test/email-assets", BrandTheme);

        EmailGolden.Assert(
            new EmailMessage(Guid.Empty, AsignacionPlacaEmailComposer.TemplateId, "destinatario@ejemplo.test", "Destinatario", subject, html),
            "tramites-asignacion-placa-brand");
    }
}
