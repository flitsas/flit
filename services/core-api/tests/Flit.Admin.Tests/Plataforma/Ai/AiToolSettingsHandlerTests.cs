using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Domain.Plataforma.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Ai;

public sealed class AiToolSettingsHandlerTests
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
    public async Task List_mapea_hasApiKey_y_mascara_sin_exponer_secreto()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns([OcrSettings]);

        var probe = Substitute.For<IAiApiKeyEnvironmentProbe>();
        probe.HasApiKey(AiProviderCodes.Anthropic).Returns(true);
        probe.ReadKeyForMasking(AiProviderCodes.Anthropic).Returns("sk-ant-api03-abcdefghijklmnop");
        probe.HasApiKey(AiProviderCodes.OpenAi).Returns(false);
        probe.ReadKeyForMasking(AiProviderCodes.OpenAi).Returns((string?)null);

        var handler = new ListAiToolsHandler(repo, probe);
        var view = await handler.HandleAsync(TestContext.Current.CancellationToken);

        view.Tools.Should().HaveCount(1);
        var anthropic = view.Tools[0].Providers.Single(p => p.ProviderCode == AiProviderCodes.Anthropic);
        anthropic.HasApiKey.Should().BeTrue();
        anthropic.ApiKeyMask.Should().Be("sk-***mnop");
        anthropic.ApiKeyMask.Should().NotContain("api03");

        var openAi = view.Tools[0].Providers.Single(p => p.ProviderCode == AiProviderCodes.OpenAi);
        openAi.HasApiKey.Should().BeFalse();
        openAi.ApiKeyMask.Should().BeNull();
    }

    [Fact]
    public async Task Activate_con_key_registra_cambio_y_devuelve_vista()
    {
        var updated = OcrSettings with { ActiveProvider = AiProviderCodes.OpenAi, UpdatedAt = DateTimeOffset.UtcNow };
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(AiToolCodes.Ocr, Arg.Any<CancellationToken>()).Returns(OcrSettings);
        repo.ActivateProviderAsync(
                AiToolCodes.Ocr,
                AiProviderCodes.OpenAi,
                Arg.Any<Guid?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns((ActivateProviderResult.Ok, updated));

        var probe = Substitute.For<IAiApiKeyEnvironmentProbe>();
        probe.HasApiKey(AiProviderCodes.OpenAi).Returns(true);
        probe.ReadKeyForMasking(AiProviderCodes.OpenAi).Returns("sk-openai-test-key-12345678");

        var audit = Substitute.For<IAdminAuditWriter>();
        var handler = new ActivateAiProviderHandler(repo, probe, audit);

        var result = await handler.HandleAsync(
            AiToolCodes.Ocr,
            new ActivateAiProviderCommand(AiProviderCodes.OpenAi, "manual QA", Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ActivateAiProviderOutcome.Ok);
        result.View!.ActiveProvider.Should().Be(AiProviderCodes.OpenAi);
        await audit.Received(1).WriteAsync(
            Arg.Is<AdminAuditEntry>(e => e.Operation == AuditVocabulary.Operations.Activate),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Activate_sin_key_devuelve_ProviderNoApiKey()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(AiToolCodes.Ocr, Arg.Any<CancellationToken>()).Returns(OcrSettings);

        var probe = Substitute.For<IAiApiKeyEnvironmentProbe>();
        probe.HasApiKey(AiProviderCodes.Kimi).Returns(false);

        var audit = Substitute.For<IAdminAuditWriter>();
        var handler = new ActivateAiProviderHandler(repo, probe, audit);

        var result = await handler.HandleAsync(
            AiToolCodes.Ocr,
            new ActivateAiProviderCommand(AiProviderCodes.Kimi, null, Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(ActivateAiProviderOutcome.ProviderNoApiKey);
        await repo.DidNotReceive().ActivateProviderAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Guid?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }
}
