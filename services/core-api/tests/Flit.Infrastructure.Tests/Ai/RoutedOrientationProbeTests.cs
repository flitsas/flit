using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Providers;
using Flit.Infrastructure.Ai.Routing;
using Flit.Infrastructure.Ocr;
using Flit.Tramites.Application.Ocr;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Ai;

/// <summary>
/// HU #12036 — la sonda de orientación va por el router de la herramienta <c>ocr</c>: usa el proveedor
/// activo del OCR y su respaldo, no Anthropic fijo. Con Anthropic caído (QA, 2026-11-26) el OCR cambiaba
/// a OpenAI pero la página se analizaba sin enderezar.
/// </summary>
public sealed class RoutedOrientationProbeTests
{
    private static IAiProviderRouter Router(AiRouterResult resultado)
    {
        var router = Substitute.For<IAiProviderRouter>();
        router.InvokeVisionAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<AiVisionPerfil>())
            .Returns(resultado);
        return router;
    }

    private static Task<PageOrientation> Sondear(IAiProviderRouter router) =>
        new RoutedOrientationProbe(router, NullLogger<RoutedOrientationProbe>.Instance)
            .ProbeAsync(new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Pregunta_por_el_router_del_ocr_con_perfil_sonda_y_sin_fijar_topes()
    {
        var router = Router(AiRouterResult.Success("""{"derecha":true}""", AiProviderCodes.OpenAi));

        await Sondear(router);

        // Sin topes explícitos: cada proveedor pone los suyos para el perfil (kimi-k3 necesita más que 100).
        await router.Received(1).InvokeVisionAsync(
            AiToolCodes.Ocr, Arg.Any<string>(), "application/pdf", DocumentOcrPrompts.OrientationProbePrompt,
            Arg.Any<CancellationToken>(), null, null, null, AiVisionPerfil.Sonda);
    }

    [Theory]
    [InlineData("""{"derecha":true}""", PageOrientation.Upright)]
    [InlineData("""{"derecha":false}""", PageOrientation.Rotated)]
    [InlineData("""```json\n{"derecha": false}\n```""", PageOrientation.Rotated)]
    [InlineData("""{"derecha":"no"}""", PageOrientation.Unknown)]
    [InlineData("""{"otra":true}""", PageOrientation.Unknown)]
    [InlineData("no es json", PageOrientation.Unknown)]
    public async Task Interpreta_la_respuesta_de_cualquier_proveedor(string texto, PageOrientation esperada)
    {
        (await Sondear(Router(AiRouterResult.Success(texto, AiProviderCodes.Kimi)))).Should().Be(esperada);
    }

    [Fact]
    public async Task Si_ningun_proveedor_responde_deja_el_documento_como_esta()
    {
        (await Sondear(Router(AiRouterResult.ProviderUnavailable()))).Should().Be(PageOrientation.Unknown);
    }

    [Fact]
    public void Perfil_sonda_usa_los_topes_de_orientacion_del_proveedor()
    {
        var kimi = new KimiProviderOptions { OrientacionMaxTokens = 2000, OrientacionTimeoutSeconds = 45 };

        kimi.Limites(AiVisionPerfil.Sonda, null, null).Should().Be(((int?)2000, (int?)45));
        kimi.Limites(AiVisionPerfil.Sonda, 50, 10).Should().Be(((int?)50, (int?)10), "lo explícito del llamador gana");
        kimi.Limites(AiVisionPerfil.Lectura, null, null).Should().Be(((int?)null, (int?)null), "la lectura usa los topes generales");
    }
}
