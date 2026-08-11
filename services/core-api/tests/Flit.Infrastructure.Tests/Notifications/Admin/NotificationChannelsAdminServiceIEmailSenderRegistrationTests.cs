using System.Reflection;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications.DeliveryLog;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Admin;

/// <summary>
/// HU #11367 (Feature #11349) AC4 — la resolución del remitente por canal LEE configuración; no
/// registra ningún transporte nuevo debajo del puerto <see cref="IEmailSender"/>. Fija como línea
/// base el estado dejado por la HU #11363 (el decorador <see cref="NotificationDeliveryLoggingEmailSender"/>
/// envolviendo a <see cref="ConsoleEmailSender"/>/<see cref="SmtpEmailSender"/>), ampliada por la HU
/// #11362 (Feature #11348, cierra Bug #11311) con <see cref="TenantChannelEmailRouter"/> —el
/// enrutamiento por canal, NO un transporte nuevo: sigue delegando en los dos transportes de siempre
/// o en el adaptador Renting ya registrado por otra HU— y falla si aparece una QUINTA implementación
/// de <see cref="IEmailSender"/> en el ensamblado, o si el contenedor registra más de un
/// <see cref="ServiceDescriptor"/> para el puerto.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var implementations = typeof(SmtpEmailSender).Assembly.GetTypes()
///     .Where(t => t.IsClass &amp;&amp; !t.IsAbstract &amp;&amp; typeof(IEmailSender).IsAssignableFrom(t));
/// </code>
/// </remarks>
public sealed class NotificationChannelsAdminServiceIEmailSenderRegistrationTests
{
    // Línea base conocida y documentada (HU #11358 + HU #11363 + HU #11362): dos transportes
    // concretos, el enrutador por canal del tenant y el decorador que envuelve al enrutador.
    // Cualquier clase NUEVA que implemente IEmailSender en este ensamblado es, por definición, un
    // transporte/adaptador nuevo — justo lo que el AC4 de la HU #11367 prohíbe.
    private static readonly HashSet<string> BaselineImplementationNames =
    [
        nameof(ConsoleEmailSender),
        nameof(SmtpEmailSender),
        nameof(TenantChannelEmailRouter),
        nameof(NotificationDeliveryLoggingEmailSender),
    ];

    [Fact]
    public void IEmailSender_ImplementationsInInfrastructureAssembly_MatchTheKnownBaseline()
    {
        var implementations = typeof(SmtpEmailSender).Assembly
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IEmailSender).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToHashSet();

        implementations.Should().BeEquivalentTo(
            BaselineImplementationNames,
            "la HU #11367 resuelve el remitente leyendo configuración — no debe aparecer ningún "
                + "adaptador/transporte nuevo debajo de IEmailSender (eso es alcance de otra HU)");
    }

    [Fact]
    public void IEmailSender_ServiceCollection_RegistersExactlyOneDescriptorForThePort()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder().Build();

        services.AddPostgresInfrastructure(
            connectionString: "Host=localhost;Database=flit_ac4_only;Username=flit;Password=flit",
            configuration,
            new FakeEnvironment());

        var descriptors = services.Where(d => d.ServiceType == typeof(IEmailSender)).ToList();

        descriptors.Should().HaveCount(
            1,
            "un único puerto IEmailSender (el decorador de bitácora envolviendo el transporte real) — "
                + "ningún registro adicional de un transporte nuevo (AC4)");
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
