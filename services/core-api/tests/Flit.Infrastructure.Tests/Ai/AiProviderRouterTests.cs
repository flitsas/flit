using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Domain.Plataforma.Ai;
using Flit.Infrastructure.Ai.Routing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Ai;

/// <summary>HU #13510 — failover, cola vacía y RecordFailover.</summary>
public sealed class AiProviderRouterTests
{
    private static readonly AiToolProviderSettings OcrSettings = new(
        Guid.CreateVersion7(),
        AiToolCodes.Ocr,
        AiProviderCodes.Anthropic,
        AutoReturnToPrimary: true,
        Providers:
        [
            new AiProviderSlot(AiProviderCodes.Anthropic, true, 1),
            new AiProviderSlot(AiProviderCodes.OpenAi, true, 2),
        ],
        RowVersion: 1,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: null,
        UpdatedBy: null);

    [Fact]
    public async Task Failover_primer_falla_segundo_ok_llama_RecordFailover()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(AiToolCodes.Ocr, Arg.Any<CancellationToken>()).Returns(OcrSettings);

        var probe = Substitute.For<IAiApiKeyEnvironmentProbe>();
        probe.HasApiKey(Arg.Any<string>()).Returns(true);

        var anthropic = Substitute.For<IAiVisionProvider>();
        anthropic.ProviderCode.Returns(AiProviderCodes.Anthropic);
        anthropic.SendVisionAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(AiProviderCallResult.Fail(503, "timeout"));

        var openai = Substitute.For<IAiVisionProvider>();
        openai.ProviderCode.Returns(AiProviderCodes.OpenAi);
        openai.SendVisionAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(AiProviderCallResult.Success("{\"placa\":\"ABC123\"}"));

        var audit = Substitute.For<IAdminAuditWriter>();
        var router = new AiProviderRouter(
            repo,
            probe,
            [anthropic, openai],
            Array.Empty<IAiChatProvider>(),
            audit,
            NullLogger<AiProviderRouter>.Instance);

        var result = await router.InvokeVisionAsync(
            AiToolCodes.Ocr,
            "YmFzZTY0",
            "image/jpeg",
            "prompt",
            TestContext.Current.CancellationToken);

        result.Ok.Should().BeTrue();
        result.ProviderCode.Should().Be(AiProviderCodes.OpenAi);
        result.Text.Should().Contain("ABC123");

        await repo.Received(1).RecordFailoverAsync(
            AiToolCodes.Ocr,
            AiProviderCodes.Anthropic,
            AiProviderCodes.OpenAi,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        await audit.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e => e.Operation == AuditVocabulary.Operations.Failover),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cola_vacia_devuelve_unavailable_sin_excepcion()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(AiToolCodes.Ocr, Arg.Any<CancellationToken>()).Returns(OcrSettings);

        var probe = Substitute.For<IAiApiKeyEnvironmentProbe>();
        probe.HasApiKey(Arg.Any<string>()).Returns(false);

        var audit = Substitute.For<IAdminAuditWriter>();
        var router = new AiProviderRouter(
            repo,
            probe,
            Array.Empty<IAiVisionProvider>(),
            Array.Empty<IAiChatProvider>(),
            audit,
            NullLogger<AiProviderRouter>.Instance);

        var result = await router.InvokeVisionAsync(
            AiToolCodes.Ocr,
            "YmFzZTY0",
            "image/jpeg",
            "prompt",
            TestContext.Current.CancellationToken);

        result.Ok.Should().BeFalse();
        result.Unavailable.Should().BeTrue();
        result.Status.Should().Be(503);
        result.Message.Should().Be(AiRouterResult.UnavailableErrorCode);

        await repo.DidNotReceive().RecordFailoverAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Primer_ok_no_registra_failover()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(AiToolCodes.Ocr, Arg.Any<CancellationToken>()).Returns(OcrSettings);

        var probe = Substitute.For<IAiApiKeyEnvironmentProbe>();
        probe.HasApiKey(Arg.Any<string>()).Returns(true);

        var anthropic = Substitute.For<IAiVisionProvider>();
        anthropic.ProviderCode.Returns(AiProviderCodes.Anthropic);
        anthropic.SendVisionAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(AiProviderCallResult.Success("{\"ok\":true}"));

        var openai = Substitute.For<IAiVisionProvider>();
        openai.ProviderCode.Returns(AiProviderCodes.OpenAi);

        var audit = Substitute.For<IAdminAuditWriter>();
        var router = new AiProviderRouter(
            repo,
            probe,
            [anthropic, openai],
            Array.Empty<IAiChatProvider>(),
            audit,
            NullLogger<AiProviderRouter>.Instance);

        var result = await router.InvokeVisionAsync(
            AiToolCodes.Ocr,
            "YmFzZTY0",
            "image/jpeg",
            "prompt",
            TestContext.Current.CancellationToken);

        result.Ok.Should().BeTrue();
        result.ProviderCode.Should().Be(AiProviderCodes.Anthropic);

        await openai.DidNotReceive().SendVisionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
            Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>());

        await repo.DidNotReceive().RecordFailoverAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sin_fila_settings_usa_default_y_filtra_por_key()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(AiToolCodes.Ocr, Arg.Any<CancellationToken>())
            .Returns((AiToolProviderSettings?)null);

        var probe = Substitute.For<IAiApiKeyEnvironmentProbe>();
        probe.HasApiKey(AiProviderCodes.OpenAi).Returns(true);
        probe.HasApiKey(Arg.Is<string>(c => c != AiProviderCodes.OpenAi)).Returns(false);

        var openai = Substitute.For<IAiVisionProvider>();
        openai.ProviderCode.Returns(AiProviderCodes.OpenAi);
        openai.SendVisionAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(AiProviderCallResult.Success("ok"));

        var audit = Substitute.For<IAdminAuditWriter>();
        var router = new AiProviderRouter(
            repo,
            probe,
            [openai],
            Array.Empty<IAiChatProvider>(),
            audit,
            NullLogger<AiProviderRouter>.Instance);

        var result = await router.InvokeVisionAsync(
            AiToolCodes.Ocr,
            "YmFzZTY0",
            "image/jpeg",
            "prompt",
            TestContext.Current.CancellationToken);

        result.Ok.Should().BeTrue();
        result.ProviderCode.Should().Be(AiProviderCodes.OpenAi);
    }

    [Fact]
    public async Task Todos_fallan_devuelve_unavailable()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(AiToolCodes.Ocr, Arg.Any<CancellationToken>()).Returns(OcrSettings);

        var probe = Substitute.For<IAiApiKeyEnvironmentProbe>();
        probe.HasApiKey(Arg.Any<string>()).Returns(true);

        var anthropic = Substitute.For<IAiVisionProvider>();
        anthropic.ProviderCode.Returns(AiProviderCodes.Anthropic);
        anthropic.SendVisionAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(AiProviderCallResult.Fail(503, "down"));

        var openai = Substitute.For<IAiVisionProvider>();
        openai.ProviderCode.Returns(AiProviderCodes.OpenAi);
        openai.SendVisionAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(AiProviderCallResult.Fail(500, "error"));

        var audit = Substitute.For<IAdminAuditWriter>();
        var router = new AiProviderRouter(
            repo,
            probe,
            [anthropic, openai],
            Array.Empty<IAiChatProvider>(),
            audit,
            NullLogger<AiProviderRouter>.Instance);

        var result = await router.InvokeVisionAsync(
            AiToolCodes.Ocr,
            "YmFzZTY0",
            "image/jpeg",
            "prompt",
            TestContext.Current.CancellationToken);

        result.Unavailable.Should().BeTrue();
        result.Message.Should().Be(AiRouterResult.UnavailableErrorCode);
    }
}
