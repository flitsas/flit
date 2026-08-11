using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Flit.Infrastructure;
using Flit.Infrastructure.Notifications.Renting;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Renting;

/// <summary>
/// HU #11360 AC7 — el contenedor no debe tener dependencias cautivas con los registros NUEVOS de
/// esta HU (login, caché de token, ejecutor autenticado) sumados a los de la HU #11359 (cliente
/// mTLS). Complementa (no sustituye)
/// <c>DependencyInjection.InfrastructureServiceProviderValidationTests</c> —esa prueba levanta el
/// host REAL completo con <c>ValidateOnBuild</c>, pero con <c>RENTING_API_ENABLED</c> apagado (el
/// valor por defecto en pruebas) el canal Renting no llega a registrar NADA de esta HU, así que no
/// ejercita este código. Aquí se invoca <c>AddRentingChannel</c> directamente con el canal
/// habilitado (mismo patrón por reflexión que
/// <c>RentingClientCertificateLoaderTests.InvokeAddRentingChannel</c>) y un certificado de prueba
/// real, para que el <c>AddSingleton</c> de <see cref="RentingClientCertificateProvider"/> también
/// se ejecute en <c>ValidateOnBuild</c>.
/// </summary>
public sealed class RentingChannelDependencyInjectionTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void AddRentingChannel_Habilitado_ConstruyeElProveedorSinDependenciasCautivasNiIrresolubles()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = BuildEnabledConfiguration();

        InvokeAddRentingChannel(services, configuration);

        // ValidateScopes=true + ValidateOnBuild=true: recorre TODO el grafo de estos registros
        // (incluida la carga real del certificado en el factory Singleton) al construir el
        // proveedor — igual que InfrastructureServiceProviderValidationTests, pero acotado a lo
        // que esta HU registra.
        var act = () => services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        act.Should().NotThrow(
            "IRentingTokenCache (Singleton) depende de IRentingLoginClient (Transient) — permitido — "
            + "y de IOptions<RentingChannelOptions>/TimeProvider (ambos singleton-compatibles); "
            + "ningún registro debe depender de un servicio Scoped desde un Singleton");
    }

    [Theory]
    [InlineData(typeof(IRentingTokenCache), ServiceLifetime.Singleton)]
    [InlineData(typeof(IRentingLoginClient), ServiceLifetime.Transient)]
    [InlineData(typeof(RentingAuthenticatedRequestExecutor), ServiceLifetime.Scoped)]
    public void AddRentingChannel_Habilitado_RegistraElServicioConElLifetimeEsperado(Type serviceType, ServiceLifetime expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = BuildEnabledConfiguration();

        InvokeAddRentingChannel(services, configuration);

        var descriptor = services.LastOrDefault(d => d.ServiceType == serviceType);

        descriptor.Should().NotBeNull($"AddRentingChannel debe registrar {serviceType.Name}");
        descriptor!.Lifetime.Should().Be(expected);
    }

    // ── Helpers (mismo patrón que RentingClientCertificateLoaderTests) ─────────────

    private (string Path, string Passphrase, string Subject) CreateTestCertificate(string commonName)
    {
        const string passphrase = "passphrase-de-prueba-no-real";
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

        var pfxBytes = certificate.Export(X509ContentType.Pfx, passphrase);
        var path = Path.Combine(Path.GetTempPath(), $"renting-di-test-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, pfxBytes);
        _tempFiles.Add(path);

        return (path, passphrase, certificate.Subject);
    }

    private IConfiguration BuildEnabledConfiguration()
    {
        var (pfxPath, passphrase, subject) = CreateTestCertificate("renting-di-fixture-ac7");

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

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>Ver <c>RentingClientCertificateLoaderTests.InvokeAddRentingChannel</c> — mismo motivo.</summary>
    private static void InvokeAddRentingChannel(IServiceCollection services, IConfiguration configuration)
    {
        var method = typeof(InfrastructureExtensions).GetMethod(
            "AddRentingChannel", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

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
