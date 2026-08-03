using Flit.Ict.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Jobs;

public sealed class IctJobSettingsProviderTests
{
    [Fact]
    public void Current_falls_back_to_option_defaults_before_any_refresh()
    {
        // Sin fila en BD (o antes del primer refresco) el provider debe servir los defaults del código,
        // para que los jobs nunca se queden sin cadencia/lote válidos.
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var options = Options.Create(new IctJobOptions
        {
            BusinessPollSeconds = 45,
            OrchestratorConcurrency = 10,
            OrchestratorBatchSize = 50,
            SendPollSeconds = 20,
            SendConcurrency = 5,
            SendBatchSize = 50,
            WebhookBatchSize = 50,
            WindowStartHour = 8,
            WindowEndHour = 20,
        });

        var provider = new IctJobSettingsProvider(scopeFactory, options);

        provider.Current.BusinessPollSeconds.Should().Be(45);
        provider.Current.OrchestratorConcurrency.Should().Be(10);
        provider.Current.OrchestratorBatchSize.Should().Be(50);
        provider.Current.SendConcurrency.Should().Be(5);
        provider.Current.SendBatchSize.Should().Be(50);
        provider.Current.WebhookBatchSize.Should().Be(50);
        provider.Current.WindowStartHour.Should().Be(8);
        provider.Current.WindowEndHour.Should().Be(20);
    }

    [Fact]
    public void FromOptions_maps_every_field()
    {
        var settings = IctJobSettings.FromOptions(new IctJobOptions
        {
            WindowStartHour = 6,
            WindowEndHour = 22,
            BusinessPollSeconds = 30,
            ExternalPollSeconds = 31,
            OrchestratorPollSeconds = 15,
            OrchestratorConcurrency = 12,
            OrchestratorBatchSize = 200,
            SendPollSeconds = 12,
            SendConcurrency = 8,
            SendBatchSize = 150,
            WebhookPollSeconds = 5,
            WebhookBatchSize = 100,
        });

        settings.WindowStartHour.Should().Be(6);
        settings.WindowEndHour.Should().Be(22);
        settings.ExternalPollSeconds.Should().Be(31);
        settings.OrchestratorPollSeconds.Should().Be(15);
        settings.OrchestratorConcurrency.Should().Be(12);
        settings.OrchestratorBatchSize.Should().Be(200);
        settings.SendConcurrency.Should().Be(8);
        settings.SendBatchSize.Should().Be(150);
        settings.WebhookPollSeconds.Should().Be(5);
        settings.WebhookBatchSize.Should().Be(100);
    }
}
