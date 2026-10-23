using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Flit.Infrastructure.Tests.Configuration;

/// <summary>
/// HU #13162 — se retira la clave <c>TramiteValidations:VehiclePrendaRequired</c> (nunca tuvo propiedad en
/// <see cref="TramiteValidationPolicyOptions"/>) de <c>appsettings.json</c> y de <c>docker-compose.prod.yml</c>
/// (variable <c>TRAMITE_VALIDATION_PRENDA_MODE</c>). Las claves vigentes se siguen leyendo, el gate del
/// mandatario no cambia y una variable sobrante en un VPS no rompe el arranque.
/// <para>Uso de ejemplo: <c>TramiteValidations:MandatarioRequerido:Mode = warn</c> en appsettings.json ⇒
/// <c>TramiteValidationPolicy.Resolve(options).MandatarioRequerido == Warn</c>.</para>
/// </summary>
public sealed class TramiteValidationsClavesVigentesTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.prod.yml")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private static IConfigurationRoot AppSettings() =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "services", "core-api", "src", "Flit.Api", "appsettings.json"), optional: false)
            .Build();

    private static TramiteValidationPolicyOptions Bind(IConfiguration configuration)
    {
        var options = new TramiteValidationPolicyOptions();
        configuration.GetSection(TramiteValidationPolicyOptions.SectionName).Bind(options);
        return options;
    }

    [Fact]
    public void AC3_AppsettingsYaNoTrae_VehiclePrendaRequired_YLasClavesVigentesSiguenLeyendose()
    {
        var configuration = AppSettings();

        configuration.GetSection("TramiteValidations:VehiclePrendaRequired").Exists().Should().BeFalse();

        var policy = TramiteValidationPolicy.Resolve(Bind(configuration));
        policy.DuplicateActiveProcedure.Should().Be(TramiteValidationMode.Block);
        policy.VehicleRegistrationState.Should().Be(TramiteValidationMode.Block);
        policy.VehicleBodyTypeRequired.Should().Be(TramiteValidationMode.Block);
        policy.MandatarioRequerido.Should().Be(TramiteValidationMode.Warn, "el gate del mandatario de F4 no cambia");
    }

    [Fact]
    public void AC3_ElComposeDeProduccionYaNoDefineLaVariableDePrenda()
    {
        var compose = File.ReadAllText(Path.Combine(RepoRoot(), "docker-compose.prod.yml"));

        compose.Should().NotContain("VehiclePrendaRequired");
        compose.Should().NotContain("TRAMITE_VALIDATION_PRENDA_MODE");
        compose.Should().Contain("TramiteValidations__MandatarioRequerido__Mode", "las claves vigentes siguen en el compose");
    }

    [Fact]
    public void AC6_UnaVariableSobranteDeUnVps_NoRompeLaLecturaNiCambiaLaPolitica()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "services", "core-api", "src", "Flit.Api", "appsettings.json"), optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Lo que inyectaría un compose anterior en un VPS que aún define TRAMITE_VALIDATION_PRENDA_MODE.
                ["TramiteValidations:VehiclePrendaRequired:Mode"] = "block",
            })
            .Build();

        var act = () => TramiteValidationPolicy.Resolve(Bind(configuration));

        act.Should().NotThrow();
        var policy = act();
        policy.VehicleBodyTypeRequired.Should().Be(TramiteValidationMode.Block);
        policy.MandatarioRequerido.Should().Be(TramiteValidationMode.Warn);
    }
}
