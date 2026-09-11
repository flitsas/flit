using Flit.Admin.Application.Ict;
using Flit.Admin.Domain.Ict;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Ict;

/// <summary>HU #12512 — clamps y ventana coherente del PUT ict.job_settings.</summary>
public sealed class IctJobSettingsRulesTests
{
    [Fact]
    public void Defaults_son_validos()
    {
        IctJobSettingsRules.Validate(IctJobSettings.Defaults()).Should().BeEmpty();
    }

    [Fact]
    public void Poll_menor_que_uno_es_invalido()
    {
        var settings = IctJobSettings.Defaults() with { BusinessPollSeconds = 0 };

        var errors = IctJobSettingsRules.Validate(settings);

        errors.Should().Contain(e => e.Field == "businessPollSeconds");
    }

    [Fact]
    public void Ventana_start_igual_end_es_incoherente()
    {
        var settings = IctJobSettings.Defaults() with { WindowStartHour = 8, WindowEndHour = 8 };

        var errors = IctJobSettingsRules.Validate(settings);

        errors.Should().Contain(e => e.Field == "windowEndHour");
    }

    [Fact]
    public void Hora_fuera_de_0_23_es_invalida()
    {
        var settings = IctJobSettings.Defaults() with { WindowStartHour = 24 };

        var errors = IctJobSettingsRules.Validate(settings);

        errors.Should().Contain(e => e.Field == "windowStartHour");
    }
}

public sealed class SaveIctJobSettingsHandlerTests
{
    [Fact]
    public async Task Valores_validos_persisten_y_devuelven_vista()
    {
        var repo = Substitute.For<IIctJobSettingsRepository>();
        var saved = IctJobSettings.Defaults() with
        {
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        };
        repo.SaveAsync(Arg.Any<IctJobSettings>(), Arg.Any<CancellationToken>())
            .Returns(saved);

        var handler = new SaveIctJobSettingsHandler(repo);
        var result = await handler.HandleAsync(ValidCommand(), TestContext.Current.CancellationToken);

        result.IsValid.Should().BeTrue();
        result.Settings.Should().NotBeNull();
        result.Settings!.BusinessBatchSize.Should().Be(500);
        await repo.Received(1).SaveAsync(Arg.Any<IctJobSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clamps_invalidos_no_llaman_al_repositorio()
    {
        var repo = Substitute.For<IIctJobSettingsRepository>();
        var handler = new SaveIctJobSettingsHandler(repo);

        var result = await handler.HandleAsync(
            ValidCommand() with { OrchestratorConcurrency = 0 },
            TestContext.Current.CancellationToken);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "orchestratorConcurrency");
        await repo.DidNotReceive().SaveAsync(Arg.Any<IctJobSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_expone_batch_sizes_sin_secretos()
    {
        var repo = Substitute.For<IIctJobSettingsRepository>();
        repo.GetAsync(Arg.Any<CancellationToken>()).Returns(IctJobSettings.Defaults() with
        {
            BusinessBatchSize = 400,
            ExternalBatchSize = 450,
            UpdatedAt = DateTimeOffset.Parse("2026-09-11T15:00:00Z"),
        });

        var handler = new GetIctJobSettingsHandler(repo);
        var view = await handler.HandleAsync(TestContext.Current.CancellationToken);

        view.BusinessBatchSize.Should().Be(400);
        view.ExternalBatchSize.Should().Be(450);
        view.UpdatedAt.Should().NotBeNull();
    }

    private static SaveIctJobSettingsCommand ValidCommand() => new(
        WindowStartHour: 8,
        WindowEndHour: 20,
        BusinessPollSeconds: 45,
        ExternalPollSeconds: 45,
        OrchestratorPollSeconds: 20,
        OrchestratorConcurrency: 10,
        OrchestratorBatchSize: 50,
        SendPollSeconds: 20,
        SendConcurrency: 5,
        SendBatchSize: 50,
        WebhookPollSeconds: 10,
        WebhookBatchSize: 50,
        BusinessBatchSize: 500,
        ExternalBatchSize: 500,
        UpdatedBy: Guid.Parse("11111111-1111-1111-1111-111111111111"));
}
