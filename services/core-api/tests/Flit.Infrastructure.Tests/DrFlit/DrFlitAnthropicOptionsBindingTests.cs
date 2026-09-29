using Flit.DrFlit.Application.Abstractions;
using Flit.Infrastructure.Ocr;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Infrastructure.Tests.DrFlit;

/// <summary>
/// HU #12918 AC1/AC4 — las claves <c>Anthropic:DrFlit*</c> se leen de configuración con fallback a env
/// <c>ANTHROPIC_DRFLIT_*</c> (env cruda primero, mismo orden que el resto de <c>AddOcr</c>), y el puerto
/// del chat queda registrado. Uso de ejemplo:
/// <c>services.AddPostgresInfrastructure(cs, config, env)</c> → <c>IOptions&lt;AnthropicOptions&gt;.Value.DrFlitModel</c>.
/// </summary>
[Collection(DrFlitEnvVarsTestGroup.Name)]
public sealed class DrFlitAnthropicOptionsBindingTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgresInfrastructure(
            connectionString: "Host=localhost;Database=flit_di_validation_only;Username=flit;Password=flit",
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new FakeEnvironment());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AC1_SinConfiguracion_UsaLosDefaultsDocumentados()
    {
        using var sp = Build([]);

        var o = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;

        o.DrFlitModel.Should().Be("claude-haiku-4-5");
        o.DrFlitMaxTokens.Should().Be(600);
        o.DrFlitTimeoutSeconds.Should().Be(20);
        o.DrFlitDailyMessageLimit.Should().Be(30);
        o.DrFlitEnabled.Should().BeTrue();
    }

    [Fact]
    public void AC1_LeeLasClavesDrFlitDeLaSeccionAnthropic()
    {
        using var sp = Build(new()
        {
            ["Anthropic:DrFlitModel"] = "modelo-configurado",
            ["Anthropic:DrFlitMaxTokens"] = "777",
            ["Anthropic:DrFlitTimeoutSeconds"] = "9",
            ["Anthropic:DrFlitDailyMessageLimit"] = "5",
            ["Anthropic:DrFlitEnabled"] = "true",
        });

        var o = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;

        o.DrFlitModel.Should().Be("modelo-configurado");
        o.DrFlitMaxTokens.Should().Be(777);
        o.DrFlitTimeoutSeconds.Should().Be(9);
        o.DrFlitDailyMessageLimit.Should().Be(5);
        o.DrFlitEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("FALSE")]
    public void AC4_DrFlitEnabledFalse_ApagaElLlm(string value)
    {
        using var sp = Build(new() { ["Anthropic:DrFlitEnabled"] = value });

        sp.GetRequiredService<IOptions<AnthropicOptions>>().Value.DrFlitEnabled.Should().BeFalse();
    }

    [Fact]
    public void AC1_AC4_EnvAnthropicDrFlit_TienePrioridadSobreLaConfiguracion()
    {
        const string modelVar = "ANTHROPIC_DRFLIT_MODEL";
        const string enabledVar = "ANTHROPIC_DRFLIT_ENABLED";
        try
        {
            Environment.SetEnvironmentVariable(modelVar, "modelo-del-env");
            Environment.SetEnvironmentVariable(enabledVar, "false");

            using var sp = Build(new()
            {
                ["Anthropic:DrFlitModel"] = "modelo-de-appsettings",
                ["Anthropic:DrFlitEnabled"] = "true",
            });

            var o = sp.GetRequiredService<IOptions<AnthropicOptions>>().Value;
            o.DrFlitModel.Should().Be("modelo-del-env");
            o.DrFlitEnabled.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(modelVar, null);
            Environment.SetEnvironmentVariable(enabledVar, null);
        }
    }

    [Fact]
    public void PuertoDelChat_QuedaRegistradoScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgresInfrastructure(
            "Host=localhost;Database=flit_di_validation_only;Username=flit;Password=flit",
            new ConfigurationBuilder().Build(),
            new FakeEnvironment());

        services.LastOrDefault(d => d.ServiceType == typeof(IDrFlitChatModel))
            .Should().NotBeNull().And.Match<ServiceDescriptor>(d => d.Lifetime == ServiceLifetime.Scoped);
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Flit.Infrastructure.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

/// <summary>
/// Serializa los tests que tocan variables de entorno <c>ANTHROPIC_DRFLIT_*</c>, que son globales al
/// proceso: sin esto, un test en paralelo podría leer el valor que otro dejó a medio restaurar.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DrFlitEnvVarsTestGroup
{
    public const string Name = "DrFlit env vars";
}
