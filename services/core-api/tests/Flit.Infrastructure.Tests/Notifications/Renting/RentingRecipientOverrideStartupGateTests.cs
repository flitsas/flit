using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Flit.Infrastructure;
using Flit.Infrastructure.Notifications.Renting;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Renting;

/// <summary>
/// ADR-0044 — validación de arranque del canal Renting: envío real vs. buzón de control se decide
/// por <c>RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED</c> (interruptor afirmativo y propio del
/// DESPLIEGUE), nunca por <c>IHostEnvironment</c>. Cubre las seis filas de la tabla de falla rápida
/// del ADR. Invoca <c>InfrastructureExtensions.AddRentingChannel</c> por reflexión (mismo patrón que
/// <c>RentingClientCertificateLoaderTests</c> / <c>RentingChannelDependencyInjectionTests</c>);
/// desde ADR-0044 el método ya no recibe <see cref="Microsoft.Extensions.Hosting.IHostEnvironment"/>.
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

    // ── Fila 1 — canal deshabilitado: no se exige nada, ni la variable derogada (HU #11359 AC2) ──

    [Fact]
    public void CanalDeshabilitado_NoExigeNiLaVariableNuevaNiLaDerogada()
    {
        var values = new Dictionary<string, string?>
        {
            ["Notifications:Renting:Enabled"] = "false",
            // Derogada presente y con valor: igual no se evalúa, porque el canal está apagado.
            ["Notifications:Renting:SendEmailDevelopmentRecipientOverrideEnabled"] = "true",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var act = () => InvokeAddRentingChannel(new ServiceCollection(), configuration);

        act.Should().NotThrow();
    }

    // ── Fila 2 — canal habilitado, variable ausente o vacía ⇒ arranca desviando ────────────────

    [Fact]
    public void RealRecipientsAusente_ArrancaDesviando_ExigeElBuzonDeControl()
    {
        var configuration = BuildEnabledConfiguration(realRecipientsRaw: null, includeMailbox: true);

        var act = () => InvokeAddRentingChannel(new ServiceCollection(), configuration);

        act.Should().NotThrow();
    }

    [Fact]
    public void RealRecipientsAusente_SinElBuzonDeControl_FallaNombrandoLaVariableQueFalta()
    {
        var configuration = BuildEnabledConfiguration(realRecipientsRaw: null, includeMailbox: false);

        var act = () => InvokeAddRentingChannel(new ServiceCollection(), configuration);

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_EMAIL");
    }

    // ── Fila 3 — "false" explícito: idéntico a la fila 2 (declaración explícita del default) ──

    [Fact]
    public void RealRecipientsFalse_ArrancaDesviando_IdenticoAAusente()
    {
        var configuration = BuildEnabledConfiguration(realRecipientsRaw: "false", includeMailbox: true);

        var act = () => InvokeAddRentingChannel(new ServiceCollection(), configuration);

        act.Should().NotThrow();
    }

    [Fact]
    public void RealRecipientsFalse_RegistraElDesvioReal_NoElPassthrough()
    {
        var configuration = BuildEnabledConfiguration(realRecipientsRaw: "false", includeMailbox: true);
        var services = new ServiceCollection();
        services.AddLogging();

        InvokeAddRentingChannel(services, configuration);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        var overrideImpl = provider.GetRequiredService<IRentingRecipientOverride>();
        overrideImpl.Should().BeOfType<RentingRecipientOverride>();
    }

    // ── Fila 4 — "true": arranca enviando real, sin exigir el buzón de control ─────────────────

    [Fact]
    public void RealRecipientsTrue_ArrancaEnviandoReal_SinExigirElBuzonDeControl()
    {
        var configuration = BuildEnabledConfiguration(realRecipientsRaw: "true", includeMailbox: false);

        var act = () => InvokeAddRentingChannel(new ServiceCollection(), configuration);

        act.Should().NotThrow("con envío real el buzón de control puede quedar vacío: no hay desvío que necesite dónde caer");
    }

    // ── Fila 5 — valor ininteligible: falla el arranque, no degrada en silencio ─────────────────

    [Theory]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("ture")]
    public void RealRecipientsConValorIninteligible_FallaAlArrancar_NoDegradaEnSilencio(string valorInvalido)
    {
        var configuration = BuildEnabledConfiguration(realRecipientsRaw: valorInvalido, includeMailbox: true);

        var act = () => InvokeAddRentingChannel(new ServiceCollection(), configuration);

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED");
    }

    // ── Fila 6 — variable derogada presente y no vacía (con cualquier valor de la nueva) ⇒ falla ─

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("true")]
    public void VariableDerogadaPresente_FallaConMensajeDeMigracionQueNombraAmbasVariables(string? realRecipientsRaw)
    {
        var configuration = BuildEnabledConfiguration(
            realRecipientsRaw: realRecipientsRaw, includeMailbox: true, deprecatedOverrideRaw: "true");

        var act = () => InvokeAddRentingChannel(new ServiceCollection(), configuration);

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("RENTING_API_SEND_EMAIL_DEVELOPMENT_RECIPIENT_OVERRIDE_ENABLED");
        exception.Message.Should().Contain("RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED");
    }

    // ── El nombre del ambiente ya no participa en la decisión (regresión estructural) ──────────

    [Fact]
    public void AddRentingChannel_YaNoRecibeIHostEnvironment()
    {
        // ADR-0044 — si alguien reintrodujera IHostEnvironment en la firma, este test lo detecta
        // sin depender de ningún valor de EnvironmentName: la prueba es que el PARÁMETRO no existe.
        var method = typeof(InfrastructureExtensions).GetMethod(
            "AddRentingChannel", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        method!.GetParameters().Should().NotContain(
            p => typeof(Microsoft.Extensions.Hosting.IHostEnvironment).IsAssignableFrom(p.ParameterType),
            "el desvío/envío real se decide SOLO por RENTING_API_SEND_EMAIL_REAL_RECIPIENTS_ENABLED, " +
            "nunca por el nombre del ambiente");
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

    private IConfiguration BuildEnabledConfiguration(
        string? realRecipientsRaw, bool includeMailbox, string? deprecatedOverrideRaw = null)
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
        };

        if (realRecipientsRaw is not null)
            values["Notifications:Renting:SendEmailRealRecipientsEnabled"] = realRecipientsRaw;

        if (deprecatedOverrideRaw is not null)
            values["Notifications:Renting:SendEmailDevelopmentRecipientOverrideEnabled"] = deprecatedOverrideRaw;

        if (includeMailbox)
        {
            values["Notifications:Renting:SendEmailDevelopmentRecipientEmail"] = "desvio@example.test";
            values["Notifications:Renting:SendEmailDevelopmentRecipientUsername"] = "desvio";
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// Ver <c>RentingClientCertificateLoaderTests.InvokeAddRentingChannel</c> — mismo motivo.
    /// ADR-0044 — <c>AddRentingChannel</c> ya no recibe <see cref="Microsoft.Extensions.Hosting.IHostEnvironment"/>.
    /// </summary>
    private static void InvokeAddRentingChannel(IServiceCollection services, IConfiguration configuration)
    {
        var method = typeof(InfrastructureExtensions).GetMethod(
            "AddRentingChannel", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("InfrastructureExtensions.AddRentingChannel debe existir (HU #11359)");

        try
        {
            method!.Invoke(null, [services, configuration]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
