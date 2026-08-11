using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Flit.Infrastructure;
using Flit.Infrastructure.Notifications.Renting;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Renting;

/// <summary>
/// HU #11364 AC3/AC4 — validación de arranque del desvío de destinatario. Invoca
/// <c>InfrastructureExtensions.AddRentingChannel</c> por reflexión (mismo patrón que
/// <c>RentingClientCertificateLoaderTests</c> / <c>RentingChannelDependencyInjectionTests</c>),
/// variando <see cref="IHostEnvironment.EnvironmentName"/> y el interruptor
/// <c>RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED</c>.
/// </summary>
public sealed class RentingRecipientOverrideStartupGateTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    // ── AC3 — en producción con el desvío activo, la aplicación no levanta ─────────

    [Fact]
    public void AddRentingChannel_EnProduccionConElDesvioActivo_FallaAlArrancar()
    {
        var configuration = BuildEnabledConfiguration(overrideEnabled: true);

        var act = () => InvokeAddRentingChannel(
            new ServiceCollection(), configuration, new FakeHostEnvironment(Environments.Production));

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED");
        exception.Message.Should().Contain("producción");
    }

    [Fact]
    public void AddRentingChannel_EnProduccionConElDesvioApagado_NoFalla()
    {
        // Complemento del AC3: producción con el interruptor en el valor correcto (apagado) arranca.
        var configuration = BuildEnabledConfiguration(overrideEnabled: false);

        var act = () => InvokeAddRentingChannel(
            new ServiceCollection(), configuration, new FakeHostEnvironment(Environments.Production));

        act.Should().NotThrow();
    }

    // ── AC4 — fuera de producción con el desvío apagado, la aplicación no levanta ──

    [Fact]
    public void AddRentingChannel_FueraDeProduccionConElCanalHabilitadoYElDesvioApagado_FallaAlArrancar()
    {
        var configuration = BuildEnabledConfiguration(overrideEnabled: false);

        var act = () => InvokeAddRentingChannel(
            new ServiceCollection(), configuration, new FakeHostEnvironment(Environments.Development));

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED");
        exception.Message.Should().Contain("obligatorio");
    }

    [Fact]
    public void AddRentingChannel_FueraDeProduccionConElCanalHabilitadoYElDesvioActivo_NoFallaYRegistraElDesvioReal()
    {
        var configuration = BuildEnabledConfiguration(overrideEnabled: true);
        var services = new ServiceCollection();
        services.AddLogging();

        InvokeAddRentingChannel(services, configuration, new FakeHostEnvironment(Environments.Development));

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        // El desvío real (no el Passthrough) es el que queda registrado cuando el canal está
        // habilitado — sin importar el valor del interruptor (que decide si Apply() desvía o no).
        provider.GetRequiredService<IRentingRecipientOverride>().Should().BeOfType<RentingRecipientOverride>();
    }

    // ── El AC4 solo exige el desvío cuando el canal está habilitado (verificación rápida) ──

    [Fact]
    public void AddRentingChannel_ConElCanalDeshabilitado_FueraDeProduccionYSinElDesvio_NoFalla()
    {
        // Es la comprobación más rápida de que la condición del AC4 está bien acotada: si el canal
        // Renting nace apagado (valor por defecto de despliegue) y el desvío también, CUALQUIER
        // ambiente de desarrollo sin Notifications:Renting:Enabled=true debe arrancar con
        // normalidad — igual que la suite Flit.Admin.Tests, que levanta el host real con
        // WebApplicationFactory<Program>.
        var values = new Dictionary<string, string?>
        {
            ["Notifications:Renting:Enabled"] = "false",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var act = () => InvokeAddRentingChannel(
            new ServiceCollection(), configuration, new FakeHostEnvironment(Environments.Development));

        act.Should().NotThrow();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private (string Path, string Passphrase, string Subject) CreateTestCertificate(string commonName)
    {
        const string passphrase = "passphrase-de-prueba-no-real";
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

        var pfxBytes = certificate.Export(X509ContentType.Pfx, passphrase);
        var path = Path.Combine(Path.GetTempPath(), $"renting-recipient-override-di-test-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, pfxBytes);
        _tempFiles.Add(path);

        return (path, passphrase, certificate.Subject);
    }

    private IConfiguration BuildEnabledConfiguration(bool overrideEnabled)
    {
        var (pfxPath, passphrase, subject) = CreateTestCertificate("renting-recipient-override-fixture");

        var values = new Dictionary<string, string?>
        {
            ["Notifications:Renting:Enabled"] = "true",
            ["Notifications:Renting:BaseUrl"] = "https://renting.example.test",
            ["Notifications:Renting:ApiKeyName"] = "Ocp-Apim-Subscription-Key",
            ["Notifications:Renting:ApiKeyValue"] = "clave-api-de-prueba-no-real",
            ["Notifications:Renting:PfxCertificatePath"] = pfxPath,
            ["Notifications:Renting:Passphrase"] = passphrase,
            ["Notifications:Renting:SecondsTimeout"] = "30",
            ["Notifications:Renting:LoginPath"] = "/auth/login",
            ["Notifications:Renting:LoginSecondsTimeout"] = "15",
            ["Notifications:Renting:LoginSubject"] = subject,
            ["Notifications:Renting:SendEmailPath"] = "/mail/send",
            ["Notifications:Renting:SendEmailSecondsTimeout"] = "20",
            ["Notifications:Renting:SendEmailSenderEmail"] = "no-reply@example.test",
            ["Notifications:Renting:SendEmailSenderUsername"] = "no-reply",
            ["Notifications:Renting:SendEmailDevelopmentRecipientOverrideEnabled"] = overrideEnabled ? "true" : "false",
        };

        if (overrideEnabled)
        {
            values["Notifications:Renting:SendEmailDevelopmentRecipientEmail"] = "desvio@example.test";
            values["Notifications:Renting:SendEmailDevelopmentRecipientUsername"] = "desvio";
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>Ver <c>RentingClientCertificateLoaderTests.InvokeAddRentingChannel</c> — mismo motivo.</summary>
    private static void InvokeAddRentingChannel(
        IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var method = typeof(InfrastructureExtensions).GetMethod(
            "AddRentingChannel", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("InfrastructureExtensions.AddRentingChannel debe existir (HU #11359)");

        try
        {
            method!.Invoke(null, [services, configuration, environment]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Flit.Infrastructure.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
