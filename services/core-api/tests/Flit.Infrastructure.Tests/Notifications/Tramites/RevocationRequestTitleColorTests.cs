using System.Text.RegularExpressions;
using Flit.Infrastructure.Notifications.Preview;
using Flit.Modules.Security.Domain.Auth;
using Flit.Tramites.Domain.RevocationRequests;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Tramites;

/// <summary>
/// Correos 2.0 ronda 2 — el título FLIT de la revocatoria lleva el color de su hito (los mismos de
/// develop 86e584e2d): ámbar al recibirla, verde al aprobarla, rojo al rechazarla. La marca blanca
/// no cambia: su título sigue en el color principal de la marca.
/// </summary>
public sealed class RevocationRequestTitleColorTests
{
    private const string Assets = "https://cdn.flit.test/email-assets";

    private static readonly EmailTheme BrandTheme = new(
        EmailThemeKind.Brand, "Movilidad Andina", "https://cdn.marca.test/logo.png",
        "#0B3D91", "#1FA2FF", "#FFFFFF", 7);

    private static readonly string[] ColoresDeHito = ["#8A6D00", "#2E7D32", "#C62828"];

    [Theory]
    [InlineData(RevocationRequestEmailMilestone.Solicitada, "#8A6D00")]
    [InlineData(RevocationRequestEmailMilestone.Aprobada, "#2E7D32")]
    [InlineData(RevocationRequestEmailMilestone.Rechazada, "#C62828")]
    public void Flit_el_titulo_lleva_el_color_de_su_hito(string milestone, string color)
    {
        var (_, html) = RevocationRequestEmailPreviewSample.BuildFlit(milestone, Assets);

        TituloColor(html).Should().Be(color);
    }

    [Theory]
    [InlineData(RevocationRequestEmailMilestone.Solicitada)]
    [InlineData(RevocationRequestEmailMilestone.Aprobada)]
    [InlineData(RevocationRequestEmailMilestone.Rechazada)]
    public void Brand_el_titulo_sigue_en_el_color_de_la_marca(string milestone)
    {
        var (_, html) = RevocationRequestEmailPreviewSample.BuildFlit(milestone, Assets, BrandTheme);

        TituloColor(html).Should().Be(BrandTheme.Primary);
        html.Should().NotContainAny(ColoresDeHito, "el color por hito es solo del tema FLIT");
    }

    private static string TituloColor(string html)
    {
        var h1 = Regex.Match(html, "<h1 style=\"[^\"]*?color:(#[0-9A-Fa-f]{6})");
        h1.Success.Should().BeTrue("el correo debe tener un <h1> con color en línea");
        return h1.Groups[1].Value.ToUpperInvariant();
    }
}
