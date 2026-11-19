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

    /// <summary>
    /// Review HU-6 · CR-5 — el rechazo <c>proveedor_no_aplica</c> al guardar es solo de <c>diagnostico_dictamen</c>: en ocr se puede
    /// deshabilitar deepseek y volver a habilitarlo como antes de ADR-0091.
    /// </summary>
    [Fact]
    public async Task Save_ocr_rehabilitar_deepseek_tras_deshabilitarlo_es_OK()
    {
        var ocrSinDeepSeek = OcrSettings with
        {
            Providers =
            [
                new AiProviderSlot(AiProviderCodes.Anthropic, true, 1),
                new AiProviderSlot(AiProviderCodes.OpenAi, true, 2),
                new AiProviderSlot(AiProviderCodes.DeepSeek, false, 3),
            ],
        };
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(AiToolCodes.Ocr, Arg.Any<CancellationToken>()).Returns(ocrSinDeepSeek);
        repo.SaveSettingsAsync(AiToolCodes.Ocr, Arg.Any<bool>(), Arg.Any<IReadOnlyList<AiProviderSlot>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(OcrSettings);
        var audit = Substitute.For<IAdminAuditWriter>();
        var handler = new SaveAiToolSettingsHandler(repo, Substitute.For<IAiApiKeyEnvironmentProbe>(), audit);

        var result = await handler.HandleAsync(
            AiToolCodes.Ocr,
            new SaveAiToolSettingsCommand(
                true,
                [
                    new AiProviderSlotCommand(AiProviderCodes.Anthropic, true, 1),
                    new AiProviderSlotCommand(AiProviderCodes.OpenAi, true, 2),
                    new AiProviderSlotCommand(AiProviderCodes.DeepSeek, true, 3),
                ],
                Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.ErrorCode.Should().BeNull();
        await repo.Received(1).SaveSettingsAsync(AiToolCodes.Ocr, true, Arg.Any<IReadOnlyList<AiProviderSlot>>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Review HU-6 · CR-5 — en <c>diagnostico_dictamen</c> sigue el rechazo: habilitar openai ⇒ <c>proveedor_no_aplica</c>.</summary>
    [Fact]
    public async Task Save_diagnostico_dictamen_habilitar_openai_es_proveedor_no_aplica()
    {
        var dictamen = OcrSettings with
        {
            ToolCode = AiToolCodes.DiagnosticoDictamen,
            Providers =
            [
                new AiProviderSlot(AiProviderCodes.Anthropic, true, 1),
                new AiProviderSlot(AiProviderCodes.OpenAi, false, 2),
            ],
        };
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.GetByToolCodeAsync(AiToolCodes.DiagnosticoDictamen, Arg.Any<CancellationToken>()).Returns(dictamen);
        var audit = Substitute.For<IAdminAuditWriter>();
        var handler = new SaveAiToolSettingsHandler(repo, Substitute.For<IAiApiKeyEnvironmentProbe>(), audit);

        var result = await handler.HandleAsync(
            AiToolCodes.DiagnosticoDictamen,
            new SaveAiToolSettingsCommand(
                true,
                [
                    new AiProviderSlotCommand(AiProviderCodes.Anthropic, true, 1),
                    new AiProviderSlotCommand(AiProviderCodes.OpenAi, true, 2),
                ],
                Guid.CreateVersion7()),
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("proveedor_no_aplica");
        await repo.DidNotReceiveWithAnyArgs().SaveSettingsAsync(default!, default, default!, default, TestContext.Current.CancellationToken);
    }
}
