using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tests.Shared;
using Xunit;

namespace Flit.Modules.Security.Application.Tests.Auth;

/// <summary>
/// HU #12428 AC2/AC3/AC6 — congela ASUNTO y CUERPO de <c>security.invitation</c> renderizado con
/// un tema de marca (<see cref="EmailThemeKind.Brand"/>) FIJO, carácter a carácter — igual criterio
/// que <see cref="SecurityEmailGoldenTests"/> pero para la variante de marca (no existía antes de
/// esta historia; no toca ningún golden de <c>Flit</c>).
/// <para>
/// <b>Si un refactor obliga a editar este <c>.golden.txt</c>, el refactor está mal.</b> Solo un
/// cambio DELIBERADO del chrome de marca justifica regenerarlo, con <c>GOLDEN_UPDATE=1</c> en un
/// commit aparte que diga por qué.
/// </para>
/// </summary>
public sealed class BrandEmailGoldenTests
{
    private const string ActivateUrlBase = "https://app.flit.test/invite/activate";
    private const string RawToken = "GOLDEN-TOKEN-0000";
    private const string FullName = "Ana María Restrepo";

    private static readonly EmailTheme BrandTheme = new(
        EmailThemeKind.Brand,
        "Movilidad Andina",
        "https://dev.flitsas.online/api/v1/public/branding/logos/11111111-1111-4111-8111-111111111111",
        "#0B3D91",
        "#1FA2FF",
        "#FFFFFF",
        7);

    [Fact]
    public void Invitacion_brand_conserva_asunto_y_cuerpo()
    {
        var link = InvitationEmailTemplate.BuildActivateLink(ActivateUrlBase, RawToken);
        var composed = InvitationEmailTemplate.Compose(FullName, link, theme: BrandTheme);

        EmailGolden.Assert(
            new EmailMessage(Guid.Empty, "security.invitation", "destinatario@ejemplo.test", FullName, composed.Subject, composed.HtmlBody),
            "security-invitation-brand");
    }
}
