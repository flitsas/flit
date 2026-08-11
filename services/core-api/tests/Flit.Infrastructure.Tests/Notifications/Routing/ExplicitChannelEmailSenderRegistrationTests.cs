using System.Reflection;
using Flit.Infrastructure.Notifications.Admin;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Routing;

/// <summary>
/// HU #11371 (Feature #11349, cierra el retorno-temprano fijo del banco de pruebas de
/// notificaciones) — <see cref="TenantChannelEmailRouter"/> se registra como servicio Scoped propio
/// (ya no se construye inline dentro de la fábrica de <see cref="IEmailSender"/>), y
/// <see cref="IExplicitChannelEmailSender"/> es la ÚNICA puerta de acceso nueva: el resto del
/// pipeline de producción (decorador de bitácora envolviendo al router) no cambia.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var services = new ServiceCollection();
/// services.AddPostgresInfrastructure(connectionString, configuration, environment);
/// var provider = services.BuildServiceProvider();
/// using var scope = provider.CreateScope();
/// var router = scope.ServiceProvider.GetRequiredService&lt;TenantChannelEmailRouter&gt;();
/// var explicitSender = scope.ServiceProvider.GetRequiredService&lt;IExplicitChannelEmailSender&gt;();
/// </code>
/// </remarks>
public sealed class ExplicitChannelEmailSenderRegistrationTests
{
    // "Único consumidor" (punto 2 del diseño): estos son los ÚNICOS tipos del ensamblado cuyo
    // constructor pide IExplicitChannelEmailSender. NotificationTestSendAdminService lo usa para
    // enviar; NotificationChannelsAdminService lo usa SOLO para IsChannelAvailable (alineación de
    // GET .../canales, punto 4 del diseño) — ninguno de los 6 puntos de envío de producción lo
    // consume: ellos siguen dependiendo únicamente de IEmailSender.
    private static readonly HashSet<Type> KnownConsumers =
    [
        typeof(NotificationTestSendAdminService),
        typeof(NotificationChannelsAdminService),
    ];

    [Fact]
    public void IExplicitChannelEmailSender_HasNoConsumersOutsideTheKnownBaseline()
    {
        var assembly = typeof(TenantChannelEmailRouter).Assembly;

        var consumers = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(ctor => ctor.GetParameters()
                    .Any(p => p.ParameterType == typeof(IExplicitChannelEmailSender))))
            .ToHashSet();

        consumers.Should().BeEquivalentTo(
            KnownConsumers,
            "IExplicitChannelEmailSender es una puerta de acceso deliberadamente angosta (HU #11371): "
                + "solo el banco de pruebas y el endpoint de canales pueden usarla — ningún punto de "
                + "envío de producción debe saltarse la resolución por política de tenant a través de "
                + "esta interfaz");
    }

    [Fact]
    public void TenantChannelEmailRouter_IsRegisteredAsItsOwnScopedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        services.AddPostgresInfrastructure(
            connectionString: "Host=localhost;Database=flit_hu11371_router;Username=flit;Password=flit",
            configuration,
            new FakeEnvironment());

        var routerDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TenantChannelEmailRouter));
        routerDescriptor.Should().NotBeNull("el router debe poder resolverse por su tipo concreto, no solo detrás de IEmailSender");
        routerDescriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);

        var explicitSenderDescriptors = services.Where(d => d.ServiceType == typeof(IExplicitChannelEmailSender)).ToList();
        explicitSenderDescriptors.Should().HaveCount(1);
        explicitSenderDescriptors[0].Lifetime.Should().Be(ServiceLifetime.Scoped);

        // Línea base sin cambios respecto al pipeline de producción (ver
        // NotificationChannelsAdminServiceIEmailSenderRegistrationTests): un único descriptor para
        // IEmailSender, el decorador de bitácora envolviendo al router.
        var emailSenderDescriptors = services.Where(d => d.ServiceType == typeof(IEmailSender)).ToList();
        emailSenderDescriptors.Should().HaveCount(1);
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
