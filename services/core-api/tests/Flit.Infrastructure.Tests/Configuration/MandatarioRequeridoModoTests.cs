using Flit.Infrastructure;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Infrastructure.Tests.Configuration;

/// <summary>
/// HU #13143 (ADR-0066) — modo block/warn/off de la validación del mandatario al radicar, por ambiente:
/// fail-safe a Block en código, <c>warn</c> explícito en la configuración entregada y log de arranque.
/// </summary>
public sealed class MandatarioRequeridoModoTests
{
    private static TramiteValidationPolicyOptions Bind(IConfiguration configuration)
    {
        var options = new TramiteValidationPolicyOptions();
        configuration.GetSection(TramiteValidationPolicyOptions.SectionName).Bind(options);
        return options;
    }

    private static IConfiguration InMemory(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docker-compose.prod.yml")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    // AC1 — modo por ambiente.
    [Theory]
    [InlineData("warn", TramiteValidationMode.Warn)]
    [InlineData("block", TramiteValidationMode.Block)]
    [InlineData("off", TramiteValidationMode.Off)]
    [InlineData("  WARN ", TramiteValidationMode.Warn)]
    public void Ac1_LaPoliticaResueltaExponeElModoConfigurado(string raw, TramiteValidationMode esperado)
    {
        var options = Bind(InMemory(("TramiteValidations:MandatarioRequerido:Mode", raw)));

        var policy = TramiteValidationPolicy.Resolve(options);

        policy.MandatarioRequerido.Should().Be(esperado);
    }

    [Fact]
    public void Ac1_LaVariableDeEntornoDoblePisoLlegaALaSeccion()
    {
        // TramiteValidations__MandatarioRequerido__Mode es lo que inyecta el compose; el proveedor de variables
        // de entorno la traduce a la clave con dos puntos.
        const string variable = "TramiteValidations__MandatarioRequerido__Mode";
        var anterior = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "warn");
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();

            TramiteValidationPolicy.Resolve(Bind(configuration)).MandatarioRequerido
                .Should().Be(TramiteValidationMode.Warn);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, anterior);
        }
    }

    [Fact]
    public void Ac1_ElLogDeArranqueRegistraElModoJuntoAlosDemas()
    {
        var logger = new CapturingLogger();
        var policy = new TramiteValidationPolicy(
            TramiteValidationMode.Block, TramiteValidationMode.Warn, TramiteValidationMode.Off,
            TramiteValidationMode.Warn);

        TramiteValidationLog.PolicyResolved(
            logger, policy.DuplicateActiveProcedure, policy.VehicleRegistrationState,
            policy.VehicleBodyTypeRequired, policy.MandatarioRequerido);

        logger.Messages.Should().ContainSingle().Which.Should()
            .Contain("DuplicateActiveProcedure=Block")
            .And.Contain("VehicleRegistrationState=Warn")
            .And.Contain("VehicleBodyTypeRequired=Off")
            .And.Contain("MandatarioRequerido=Warn");
    }

    // AC2 — fail-safe a Block en el código.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ac2_AusenteOVacio_ResuelveABlock_SinAvisar(string? raw)
    {
        var avisos = new List<(string, string?)>();
        var options = new TramiteValidationPolicyOptions
        {
            MandatarioRequerido = new TramiteValidationSetting { Mode = raw },
        };

        var policy = TramiteValidationPolicy.Resolve(options, (n, r) => avisos.Add((n, r)));

        policy.MandatarioRequerido.Should().Be(TramiteValidationMode.Block);
        avisos.Should().BeEmpty();
    }

    [Fact]
    public void Ac2_SeccionAusente_ResuelveABlock()
    {
        var options = Bind(InMemory());

        TramiteValidationPolicy.Resolve(options).MandatarioRequerido.Should().Be(TramiteValidationMode.Block);
    }

    [Theory]
    [InlineData("desactivado")]
    [InlineData("false")]
    [InlineData("0")]
    public void Ac2_ValorNoReconocido_ResuelveABlock_YQuedaAvisadoEnElArranque(string raw)
    {
        var avisos = new List<(string Validacion, string? Valor)>();
        var options = new TramiteValidationPolicyOptions
        {
            MandatarioRequerido = new TramiteValidationSetting { Mode = raw },
        };

        var policy = TramiteValidationPolicy.Resolve(options, (n, r) => avisos.Add((n, r)));

        policy.MandatarioRequerido.Should().Be(TramiteValidationMode.Block);
        avisos.Should().ContainSingle().Which.Should()
            .Be((nameof(TramiteValidationPolicyOptions.MandatarioRequerido), raw));

        var logger = new CapturingLogger();
        TramiteValidationLog.UnrecognizedMode(logger, avisos[0].Validacion, avisos[0].Valor);
        logger.Messages.Should().ContainSingle().Which.Should()
            .Contain("MandatarioRequerido").And.Contain(raw).And.Contain("block");
    }

    // AC3 — valores iniciales explícitos.
    [Fact]
    public void Ac3_AppsettingsJson_FijaWarnExplicito_YElResultadoEsWarn()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "services", "core-api", "src", "Flit.Api", "appsettings.json"))
            .Build();

        configuration["TramiteValidations:MandatarioRequerido:Mode"].Should().Be("warn");
        TramiteValidationPolicy.Resolve(Bind(configuration)).MandatarioRequerido
            .Should().Be(TramiteValidationMode.Warn);
    }

    [Fact]
    public void Ac3_DockerComposeProd_FijaWarnPorDefecto_ConLaVariableDelEnvDelVps()
    {
        var compose = File.ReadAllText(Path.Combine(RepoRoot(), "docker-compose.prod.yml"));

        compose.Should().Contain(
            "TramiteValidations__MandatarioRequerido__Mode: ${TRAMITE_VALIDATION_MANDATARIO_MODE:-warn}");
    }

    [Fact]
    public void Ac3_ElPasoABlockEsUnCambioDeVariable_SinDespliegueDeCodigo()
    {
        // El valor de appsettings es el de la entrega; la variable de entorno lo reemplaza sin tocar código.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "services", "core-api", "src", "Flit.Api", "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TramiteValidations:MandatarioRequerido:Mode"] = "block",
            })
            .Build();

        TramiteValidationPolicy.Resolve(Bind(configuration)).MandatarioRequerido
            .Should().Be(TramiteValidationMode.Block);
    }

    // AC4 — las validaciones existentes no cambian.
    [Fact]
    public void Ac4_ElConstructorConservaLosDefaultsYElNuevoParametroEsOpcionalBlock()
    {
        var policy = new TramiteValidationPolicy(TramiteValidationMode.Warn, TramiteValidationMode.Off);

        policy.DuplicateActiveProcedure.Should().Be(TramiteValidationMode.Warn);
        policy.VehicleRegistrationState.Should().Be(TramiteValidationMode.Off);
        policy.VehicleBodyTypeRequired.Should().Be(TramiteValidationMode.Block);
        policy.MandatarioRequerido.Should().Be(TramiteValidationMode.Block);
    }

    [Fact]
    public void Ac4_BlockAll_IncluyeElNuevoModo_YLasTresExistentesSiguenEnBlock()
    {
        var p = TramiteValidationPolicy.BlockAll;

        p.DuplicateActiveProcedure.Should().Be(TramiteValidationMode.Block);
        p.VehicleRegistrationState.Should().Be(TramiteValidationMode.Block);
        p.VehicleBodyTypeRequired.Should().Be(TramiteValidationMode.Block);
        p.MandatarioRequerido.Should().Be(TramiteValidationMode.Block);
    }

    [Fact]
    public void Ac4_ConfigurarElMandatarioNoAlteraLasOtrasTresValidaciones()
    {
        var options = Bind(InMemory(
            ("TramiteValidations:MandatarioRequerido:Mode", "off"),
            ("TramiteValidations:DuplicateActiveProcedure:Mode", "warn")));

        var policy = TramiteValidationPolicy.Resolve(options);

        policy.MandatarioRequerido.Should().Be(TramiteValidationMode.Off);
        policy.DuplicateActiveProcedure.Should().Be(TramiteValidationMode.Warn);
        policy.VehicleRegistrationState.Should().Be(TramiteValidationMode.Block);
        policy.VehicleBodyTypeRequired.Should().Be(TramiteValidationMode.Block);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
