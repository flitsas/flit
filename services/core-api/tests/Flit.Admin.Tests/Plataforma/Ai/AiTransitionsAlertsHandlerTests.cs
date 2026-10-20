using Flit.Admin.Application.Plataforma.Ai;
using Flit.Admin.Domain.Plataforma.Ai;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Plataforma.Ai;

/// <summary>HU #13512 — handlers de transitions y alerts.</summary>
public sealed class AiTransitionsAlertsHandlerTests
{
    [Fact]
    public async Task ListTransitions_pagina_y_ordena_meta()
    {
        var t1 = new AiProviderTransition(
            Guid.CreateVersion7(),
            AiToolCodes.Ocr,
            AiProviderCodes.Anthropic,
            AiProviderCodes.OpenAi,
            "failover_auto",
            "timeout",
            null,
            DateTimeOffset.UtcNow);

        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.ListTransitionsAsync(AiToolCodes.Ocr, 1, 20, Arg.Any<CancellationToken>())
            .Returns(([t1], 21L));

        var handler = new ListAiProviderTransitionsHandler(repo);
        var view = await handler.HandleAsync(AiToolCodes.Ocr, page: 1, pageSize: 20, TestContext.Current.CancellationToken);

        view.Should().NotBeNull();
        view!.Items.Should().HaveCount(1);
        view.Items[0].FromProvider.Should().Be(AiProviderCodes.Anthropic);
        view.Items[0].ToProvider.Should().Be(AiProviderCodes.OpenAi);
        view.TotalCount.Should().Be(21);
        view.Page.Should().Be(1);
        view.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task ListTransitions_pageSize_mayor_a_100_se_clampa()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.ListTransitionsAsync(AiToolCodes.Ocr, 1, 100, Arg.Any<CancellationToken>())
            .Returns((Array.Empty<AiProviderTransition>(), 0L));

        var handler = new ListAiProviderTransitionsHandler(repo);
        var view = await handler.HandleAsync(AiToolCodes.Ocr, 1, 500, TestContext.Current.CancellationToken);

        view!.PageSize.Should().Be(100);
        await repo.Received(1).ListTransitionsAsync(
            AiToolCodes.Ocr,
            1,
            100,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListTransitions_tool_desconocida_devuelve_null()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        var handler = new ListAiProviderTransitionsHandler(repo);

        var view = await handler.HandleAsync("desconocida", 1, 20, TestContext.Current.CancellationToken);

        view.Should().BeNull();
        await repo.DidNotReceive().ListTransitionsAsync(
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAlerts_filtra_acked_false()
    {
        var alert = new AiFailoverAlert(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            AiToolCodes.Ocr,
            AiProviderCodes.Anthropic,
            AiProviderCodes.Kimi,
            DateTimeOffset.UtcNow,
            AckedAt: null,
            AckedBy: null);

        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.ListAlertsAsync(false, Arg.Any<CancellationToken>()).Returns([alert]);

        var handler = new ListAiFailoverAlertsHandler(repo);
        var view = await handler.HandleAsync(acked: false, TestContext.Current.CancellationToken);

        view.Items.Should().HaveCount(1);
        view.Items[0].Acked.Should().BeFalse();
        await repo.Received(1).ListAlertsAsync(false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AckAlert_marca_acked_y_devuelve_vista()
    {
        var alertId = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();
        var ackedAt = DateTimeOffset.UtcNow;
        var alert = new AiFailoverAlert(
            alertId,
            Guid.CreateVersion7(),
            AiToolCodes.DrFlit,
            AiProviderCodes.Anthropic,
            AiProviderCodes.OpenAi,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            ackedAt,
            actor);

        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.AcknowledgeAlertAsync(alertId, actor, Arg.Any<CancellationToken>()).Returns(alert);

        var handler = new AcknowledgeAiFailoverAlertHandler(repo);
        var view = await handler.HandleAsync(alertId, actor, TestContext.Current.CancellationToken);

        view.Should().NotBeNull();
        view!.Acked.Should().BeTrue();
        view.AckedBy.Should().Be(actor);
        view.AckedAt.Should().Be(ackedAt);
    }

    [Fact]
    public async Task AckAlert_inexistente_devuelve_null()
    {
        var repo = Substitute.For<IAiToolProviderSettingsRepository>();
        repo.AcknowledgeAlertAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((AiFailoverAlert?)null);

        var handler = new AcknowledgeAiFailoverAlertHandler(repo);
        var view = await handler.HandleAsync(Guid.CreateVersion7(), Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        view.Should().BeNull();
    }
}
