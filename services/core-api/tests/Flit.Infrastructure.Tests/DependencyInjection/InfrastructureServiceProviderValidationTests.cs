using Flit.Infrastructure;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Flit.Infrastructure.Tests.DependencyInjection;

/// <summary>
/// HU #11358 AC5 — el contenedor de la aplicación no debe tener dependencias cautivas: un
/// registro <c>Singleton</c> que dependa (directa o transitivamente) de un servicio
/// <c>Scoped</c>/<c>Transient</c>. Hoy es <c>IEmailSender</c> (Singleton) el que está en la
/// mira —mañana, con el adaptador HTTP de la HU #11361 debajo (los <c>AddHttpClient&lt;T&gt;</c>
/// del repo son todos Transient), sería captive dependency si el puerto siguiera siendo
/// instancia única—; por eso pasó a <c>AddScoped</c> en <c>InfrastructureExtensions</c>.
/// <para>
/// Levanta el host REAL (<c>Program</c>, el mismo <c>Program.cs</c> que arranca en producción)
/// vía <see cref="WebApplicationFactory{TEntryPoint}"/> —igual que
/// <c>Telemetry.UsageEventsEndpointTests</c>, sin PostgreSQL accesible (<c>TestEnvironment</c>
/// desactiva la automigración a nivel de ensamblado)— pero forzando
/// <c>ValidateScopes = true, ValidateOnBuild = true</c> en el proveedor de servicios. .NET
/// recorre entonces el grafo de TODOS los registros al construir el <c>IServiceProvider</c> y
/// lanza si alguno es irresoluble o cautivo, sin ejecutar ninguna fábrica ni abrir conexión real.
/// </para>
/// <para>Reutilizable por la HU #11360 para el resto de puertos del módulo de notificaciones.</para>
/// </summary>
public sealed class InfrastructureServiceProviderValidationTests
{
    [Fact]
    public void AppServiceProvider_BuildsWithoutCaptiveOrUnresolvableDependencies()
    {
        using var factory = new ValidatingWebApplicationFactory();

        // Acceder a Services fuerza la construcción del host (y por tanto del ServiceProvider);
        // si hay una dependencia cautiva o irresoluble, falla aquí con InvalidOperationException.
        var act = () => _ = factory.Services;

        act.Should().NotThrow(
            "ningún registro Singleton (p. ej. IEmailSender) debe depender de un servicio Scoped/Transient, "
            + "y todo el grafo declarado debe ser resoluble");
    }

    private sealed class ValidatingWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateScopes = true;
                options.ValidateOnBuild = true;
            });
        }
    }

    /// <summary>
    /// Complemento determinista a la prueba anterior: el grafo actual de <c>IEmailSender</c> no
    /// tiene HOY ninguna dependencia scoped/transient debajo (sus dos implementaciones dependen
    /// solo de <c>ILogger&lt;T&gt;</c>/<c>EmailSettings</c>, ambos singleton-compatibles), así que
    /// <c>ValidateOnBuild</c> pasaría igual si el registro fuera <c>AddSingleton</c> — esa prueba
    /// por sí sola NO habría detectado una regresión de AC5 hoy, solo lo hará el día que la HU
    /// #11361 meta un <c>AddHttpClient</c> (Transient) debajo. Esta prueba cierra ese hueco
    /// verificando el <see cref="ServiceLifetime"/> declarado explícitamente, hoy.
    /// </summary>
    [Fact]
    public void IEmailSender_IsRegisteredAsScoped()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        services.AddPostgresInfrastructure(
            connectionString: "Host=localhost;Database=flit_di_validation_only;Username=flit;Password=flit",
            configuration,
            new FakeEnvironment());

        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(IEmailSender));

        descriptor.Should().NotBeNull("AddPostgresInfrastructure debe registrar IEmailSender");
        descriptor!.Lifetime.Should().Be(
            ServiceLifetime.Scoped,
            "un puerto Singleton con un adaptador Transient debajo (AddHttpClient, HU #11361) sería captive dependency");
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
