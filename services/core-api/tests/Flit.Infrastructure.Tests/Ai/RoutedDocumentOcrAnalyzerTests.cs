using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Routing;
using Flit.Infrastructure.Ocr;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Ai;

/// <summary>HU #13510 — OCR routed mapea unavailable a DocumentOcrAnalysis 503.</summary>
public sealed class RoutedDocumentOcrAnalyzerTests
{
    [Fact]
    public async Task Unavailable_devuelve_503_con_codigo_estructurado()
    {
        var router = Substitute.For<IAiProviderRouter>();
        router.InvokeVisionAsync(
                AiToolCodes.Ocr,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<int?>())
            .Returns(AiRouterResult.ProviderUnavailable());

        var analyzer = new RoutedDocumentOcrAnalyzer(router, NullLogger<RoutedDocumentOcrAnalyzer>.Instance);
        var result = await analyzer.AnalyzeAsync(
            "soat",
            new byte[] { 1, 2, 3 },
            "image/jpeg",
            TestContext.Current.CancellationToken);

        result.Ok.Should().BeFalse();
        result.Status.Should().Be(503);
        result.Message.Should().Be(AiRouterResult.UnavailableErrorCode);
    }

    [Fact]
    public async Task Exito_parsea_json_del_modelo()
    {
        var router = Substitute.For<IAiProviderRouter>();
        router.InvokeVisionAsync(
                AiToolCodes.Ocr,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>(),
                Arg.Any<int?>(),
                Arg.Any<int?>())
            .Returns(AiRouterResult.Success(
                """{"numeroPoliza":"123","placa":"ABC123"}""",
                AiProviderCodes.OpenAi));

        var analyzer = new RoutedDocumentOcrAnalyzer(router, NullLogger<RoutedDocumentOcrAnalyzer>.Instance);
        var result = await analyzer.AnalyzeAsync(
            "soat",
            new byte[] { 1, 2, 3 },
            "image/jpeg",
            TestContext.Current.CancellationToken);

        result.Ok.Should().BeTrue();
        result.Data!["placa"]!.GetValue<string>().Should().Be("ABC123");
    }
}
