using Flit.Infrastructure.Notifications.Tramites;
using Flit.Tramites.Domain.RevocationRequests;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Tramites;

/// <summary>HU #12579 — composer de los 3 hitos del sub-flujo de revocatoria.</summary>
public sealed class RevocationRequestEmailComposerTests
{
    private const string AssetsBaseUrl = "https://cdn.flit.test/email-assets";

    private static RevocationRequestEmailModel Model(string? motivo = null) => new(
        DestinatarioNombre: "Ana Radicadora",
        Placa: "ABC123",
        Radicado: "FT1-0000099",
        Motivo: motivo);

    [Fact]
    public void ComposeFlit_Solicitada_IncluyeRadicadoYPlacaSinMotivo()
    {
        var (subject, html) = RevocationRequestEmailComposer.ComposeFlit(
            RevocationRequestEmailMilestone.Solicitada, Model(), AssetsBaseUrl);

        subject.Should().Contain("FT1-0000099");
        html.Should().Contain("Ana Radicadora");
        html.Should().Contain("FT1-0000099");
        html.Should().Contain("ABC123");
        html.Should().NotContain("Motivo:");
    }

    [Fact]
    public void ComposeFlit_Aprobada_MencionaRevocado()
    {
        var (_, html) = RevocationRequestEmailComposer.ComposeFlit(
            RevocationRequestEmailMilestone.Aprobada, Model(), AssetsBaseUrl);

        html.Should().Contain("Revocado");
        html.Should().NotContain("Motivo:");
    }

    [Fact]
    public void ComposeFlit_Rechazada_MuestraMotivoCuandoViene()
    {
        var (_, html) = RevocationRequestEmailComposer.ComposeFlit(
            RevocationRequestEmailMilestone.Rechazada, Model("El soporte no es legible."), AssetsBaseUrl);

        html.Should().Contain("Motivo:");
        html.Should().Contain("El soporte no es legible.");
    }

    [Fact]
    public void ComposeFlit_Rechazada_SinMotivo_NoRompeYOmiteElBloque()
    {
        var (_, html) = RevocationRequestEmailComposer.ComposeFlit(
            RevocationRequestEmailMilestone.Rechazada, Model(motivo: null), AssetsBaseUrl);

        html.Should().NotContain("Motivo:");
    }

    [Fact]
    public void ComposeRenting_Solicitada_UsaChromeRenting()
    {
        var (_, html) = RevocationRequestEmailComposer.ComposeRenting(
            RevocationRequestEmailMilestone.Solicitada, Model(), AssetsBaseUrl);

        html.Should().Contain("Renting Colombia");
        html.Should().Contain("FT1-0000099");
    }

    [Fact]
    public void ComposeFlit_HitoDesconocido_LanzaArgumentOutOfRange()
    {
        var act = () => RevocationRequestEmailComposer.ComposeFlit("no-existe", Model(), AssetsBaseUrl);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ComposeFlit_TresHitos_TienenAsuntosDistintos()
    {
        var solicitada = RevocationRequestEmailComposer.ComposeFlit(
            RevocationRequestEmailMilestone.Solicitada, Model(), AssetsBaseUrl).Subject;
        var aprobada = RevocationRequestEmailComposer.ComposeFlit(
            RevocationRequestEmailMilestone.Aprobada, Model(), AssetsBaseUrl).Subject;
        var rechazada = RevocationRequestEmailComposer.ComposeFlit(
            RevocationRequestEmailMilestone.Rechazada, Model("x"), AssetsBaseUrl).Subject;

        new[] { solicitada, aprobada, rechazada }.Should().OnlyHaveUniqueItems();
    }
}
