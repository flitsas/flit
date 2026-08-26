using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Flit.Infrastructure.Notifications.Renting;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Renting;

/// <summary>
/// HU #11359 — AC1 (validación de presencia de variables al arrancar), AC3 (certificado cliente
/// adjunto + traza de expiración), AC4 (falla rápida sin material TLS, sin filtrar secretos) y
/// AC5 (identidad de login vs. Subject del certificado). Ningún PFX se guarda en el repo: cada
/// prueba genera el suyo en memoria/temp con <see cref="CertificateRequest"/> y una passphrase de
/// prueba (nunca la real de despliegue).
/// <para>
/// Uso de ejemplo del componente bajo prueba:
/// <code>
/// var options = new RentingChannelOptions { PfxCertificatePath = "...", Passphrase = "...", LoginSubject = "CN=..." };
/// var certificate = RentingClientCertificateLoader.Load(options, logger);
/// </code>
/// </para>
/// </summary>
public sealed class RentingClientCertificateLoaderTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { File.Delete(path); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    // ── AC3 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_ConPfxValidoYSubjectCoincidente_AdjuntaElCertificadoCliente()
    {
        var (path, passphrase, subject) = CreateTestCertificate("renting-mtls-fixture-ac3");
        var options = NewOptions(path, passphrase, loginSubject: subject);
        var logger = new CapturingLogger();

        var certificate = RentingClientCertificateLoader.Load(options, logger);

        // AC3 — el certificado cargado ES el certificado cliente (mismo Subject/miniatura), listo
        // para adjuntarse a SslOptions.ClientCertificates (RentingHttpMessageHandlerFactory).
        certificate.Subject.Should().Be(subject);
        certificate.HasPrivateKey.Should().BeTrue("un certificado cliente para mTLS necesita la clave privada del PFX");
    }

    [Fact]
    public void Load_ConPfxValido_RegistraEnLaTrazaLaFechaDeExpiracion()
    {
        var (path, passphrase, subject) = CreateTestCertificate("renting-mtls-fixture-expiry");
        var options = NewOptions(path, passphrase, loginSubject: subject);
        var logger = new CapturingLogger();

        var certificate = RentingClientCertificateLoader.Load(options, logger);

        // AC3 — "la traza de arranque registra la fecha de expiración del certificado cargado": es
        // la única señal disponible el día que el certificado venza y el canal deje de funcionar.
        logger.Messages.Should().ContainSingle(m =>
            m.Contains("Vence el", StringComparison.Ordinal)
            && m.Contains(certificate.NotAfter.ToString("yyyy-MM-dd"), StringComparison.Ordinal));
    }

    // ── AC4 (dos casos) ──────────────────────────────────────────────────────────

    [Fact]
    public void Load_ConRutaDeCertificadoInexistente_FallaNombrandoLaVariableSinFiltrarLaPassphrase()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"renting-no-existe-{Guid.NewGuid():N}.pfx");
        const string secretPassphrase = "passphrase-de-prueba-no-real";
        var options = NewOptions(missingPath, secretPassphrase, loginSubject: "CN=irrelevante-para-este-caso");
        var logger = new CapturingLogger();

        var act = () => RentingClientCertificateLoader.Load(options, logger);

        // AC4, caso 1 — ruta inexistente: el mensaje identifica LA variable de la ruta.
        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("RENTING_API_PFX_CERTIFICATE_PATH");
        exception.Message.Should().NotContain(secretPassphrase, "el mensaje nunca debe filtrar la passphrase");
    }

    [Fact]
    public void Load_CuandoLaRutaEsUnDirectorio_LoDiceEnVezDeReportarArchivoNoEncontrado()
    {
        // Reproduce el fallo real de DEV (2026-08-11): con el .pfx ausente en el host, el bind
        // mount de Docker monta un DIRECTORIO vacío en su lugar. `File.Exists` devuelve false, así
        // que sin este caso el operador lee "no se encontró el archivo", hace `ls`, ve algo en esa
        // ruta y se queda sin pista de qué corregir.
        var directoryPath = Path.Combine(Path.GetTempPath(), $"renting-dir-{Guid.NewGuid():N}.pfx");
        Directory.CreateDirectory(directoryPath);
        const string secretPassphrase = "passphrase-de-prueba-no-real";
        var options = NewOptions(directoryPath, secretPassphrase, loginSubject: "CN=irrelevante-para-este-caso");
        var logger = new CapturingLogger();

        try
        {
            var act = () => RentingClientCertificateLoader.Load(options, logger);

            var exception = act.Should().Throw<InvalidOperationException>().Which;
            exception.Message.Should().Contain("DIRECTORIO", "el operador tiene que distinguir este caso del archivo ausente");
            exception.Message.Should().Contain("RENTING_API_PFX_CERTIFICATE_PATH");
            exception.Message.Should().Contain(directoryPath, "sin la ruta concreta no se puede comprobar cuál es la mala");
            exception.Message.Should().NotContain(secretPassphrase, "el mensaje nunca debe filtrar la passphrase");
        }
        finally
        {
            try { Directory.Delete(directoryPath); } catch (IOException) { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void Load_ConPassphraseQueNoAbreElArchivo_FallaNombrandoLaVariableSinFiltrarLaPassphrase()
    {
        const string realPassphrase = "passphrase-correcta-de-prueba";
        const string wrongPassphrase = "passphrase-incorrecta-de-prueba";
        var (path, _, subject) = CreateTestCertificate("renting-mtls-fixture-ac4b", realPassphrase);
        var options = NewOptions(path, wrongPassphrase, loginSubject: subject);
        var logger = new CapturingLogger();

        var act = () => RentingClientCertificateLoader.Load(options, logger);

        // AC4, caso 2 — PFX válido pero passphrase que no lo abre: el mensaje identifica la
        // variable de la passphrase (y no filtra NINGUNA de las dos: ni la correcta ni la usada).
        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("RENTING_API_PASSPHRASE");
        exception.Message.Should().NotContain(realPassphrase);
        exception.Message.Should().NotContain(wrongPassphrase);
    }

    // ── AC5 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_ConLoginSubjectDistintoAlDelCertificado_FallaPorDiscrepanciaDeIdentidadSinFiltrarSecretos()
    {
        // CN de prueba explícito — NUNCA el CN real de un certificado de producción.
        var (path, passphrase, certificateSubject) = CreateTestCertificate("renting-mtls-fixture-ac5-certificado");
        const string configuredSubject = "CN=renting-mtls-fixture-ac5-distinto-de-prueba";
        const string secretApiKeyValue = "clave-api-de-prueba-no-real";
        var options = NewOptions(path, passphrase, loginSubject: configuredSubject);
        options.ApiKeyValue = secretApiKeyValue;
        var logger = new CapturingLogger();

        certificateSubject.Should().NotBe(configuredSubject, "el fixture debe garantizar la discrepancia que se está probando");

        var act = () => RentingClientCertificateLoader.Load(options, logger);

        // AC5 — el arranque falla indicando la discrepancia de identidad (nombra la variable de
        // login), sin revelar passphrase ni clave de API.
        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("RENTING_API_LOGIN_SUBJECT");
        exception.Message.Should().NotContain(passphrase);
        exception.Message.Should().NotContain(secretApiKeyValue);
    }

    // ── AC1 (dos casos representativos) ─────────────────────────────────────────

    [Fact]
    public void AddRentingChannel_HabilitadoSinBaseUrl_FallaNombrandoEsaVariable()
    {
        var configuration = BuildEnabledConfiguration(remove: "Notifications:Renting:BaseUrl");
        var services = new ServiceCollection();

        var act = () => InvokeAddRentingChannel(services, configuration);

        // AC1 — con el canal habilitado, falta RENTING_API_BASE_URL ⇒ el arranque falla nombrándola.
        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("RENTING_API_BASE_URL");
    }

    [Fact]
    public void AddRentingChannel_HabilitadoSinDatosDelRemitente_FallaNombrandoEsaVariable()
    {
        var configuration = BuildEnabledConfiguration(remove: "Notifications:Renting:SendEmailSenderEmail");
        var services = new ServiceCollection();

        var act = () => InvokeAddRentingChannel(services, configuration);

        // AC1 — "datos del remitente" es una de las categorías explícitas del AC; se prueba con
        // RENTING_API_SEND_EMAIL_SENDER_EMAIL como representante de esa categoría.
        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("RENTING_API_SEND_EMAIL_SENDER_EMAIL");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static RentingChannelOptions NewOptions(string pfxPath, string passphrase, string loginSubject) => new()
    {
        Enabled = true,
        BaseUrl = "https://renting.example.test",
        ApiKeyName = "Ocp-Apim-Subscription-Key",
        ApiKeyValue = "clave-api-de-prueba-no-real",
        PfxCertificatePath = pfxPath,
        Passphrase = passphrase,
        SecondsTimeout = 30,
        LoginPath = "/auth/login",
        LoginSecondsTimeout = 15,
        LoginSubject = loginSubject,
        SendEmailPath = "/mail/send",
        SendEmailSecondsTimeout = 20,
        SendEmailSenderEmail = "no-reply@example.test",
        SendEmailSenderUsername = "no-reply",
    };

    /// <summary>
    /// Genera un PFX de prueba al vuelo (nunca se persiste en el repo) con un CN de prueba
    /// explícito. Devuelve la ruta del archivo temporal, la passphrase usada y el <c>Subject</c>
    /// resultante (formato <c>X500DistinguishedName</c>, el mismo que compara
    /// <see cref="RentingClientCertificateLoader"/>).
    /// </summary>
    private (string Path, string Passphrase, string Subject) CreateTestCertificate(
        string commonName, string passphrase = "passphrase-de-prueba-no-real")
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365));

        var pfxBytes = certificate.Export(X509ContentType.Pfx, passphrase);
        var path = Path.Combine(Path.GetTempPath(), $"renting-test-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, pfxBytes);
        _tempFiles.Add(path);

        return (path, passphrase, certificate.Subject);
    }

    /// <summary>
    /// Configuración con el canal habilitado y TODAS las variables requeridas por AC1 presentes,
    /// menos la indicada en <paramref name="remove"/> — aísla el caso de "falta esta variable
    /// específica" sin depender de ningún archivo de certificado real (la ruta es una cadena
    /// cualquiera: estos dos casos nunca llegan a <see cref="RentingClientCertificateLoader"/>).
    /// </summary>
    private static IConfiguration BuildEnabledConfiguration(string remove)
    {
        var values = new Dictionary<string, string?>
        {
            ["Notifications:Renting:Enabled"] = "true",
            ["Notifications:Renting:BaseUrl"] = "https://renting.example.test",
            ["Notifications:Renting:ApiKeyName"] = "Ocp-Apim-Subscription-Key",
            ["Notifications:Renting:ApiKeyValue"] = "clave-api-de-prueba-no-real",
            ["Notifications:Renting:PfxCertificatePath"] = "C:/renting/certificado-de-prueba.pfx",
            ["Notifications:Renting:Passphrase"] = "passphrase-de-prueba-no-real",
            ["Notifications:Renting:SecondsTimeout"] = "30",
            ["Notifications:Renting:LoginPath"] = "/auth/login",
            ["Notifications:Renting:LoginSecondsTimeout"] = "15",
            ["Notifications:Renting:LoginSubject"] = "CN=renting-test",
            ["Notifications:Renting:SendEmailPath"] = "/mail/send",
            ["Notifications:Renting:SendEmailSecondsTimeout"] = "20",
            ["Notifications:Renting:SendEmailSenderEmail"] = "no-reply@example.test",
            ["Notifications:Renting:SendEmailSenderUsername"] = "no-reply",
        };

        values.Remove(remove);

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// <c>InfrastructureExtensions.AddRentingChannel</c> es <c>private static</c> a propósito (es
    /// un detalle de cableado de <c>AddPostgresInfrastructure</c>, igual que <c>AddRues</c>/
    /// <c>AddOcr</c>): se invoca por reflexión para probar el AC1 sin acoplarse al resto del grafo
    /// de <c>AddPostgresInfrastructure</c> (DbContext, otros módulos), que no participa en este AC.
    /// Desenvuelve <see cref="TargetInvocationException"/> para que la aserción vea la excepción
    /// real que lanza <c>Require</c>. ADR-0044 — ya no recibe <see cref="IHostEnvironment"/>.
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

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
