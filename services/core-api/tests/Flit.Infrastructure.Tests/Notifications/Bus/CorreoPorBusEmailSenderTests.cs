using System.Text.Json;
using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Bus;
using Flit.Infrastructure.Notifications.Routing;
using Flit.Infrastructure.Persistence;
using Flit.Modules.Notificaciones;
using Flit.Modules.Security.Domain.Auth;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Bus;

/// <summary>
/// HU #13355 (Epic #13316) — con Notificaciones remoto, el <see cref="IEmailSender"/> de core-api deja el correo armado
/// como trabajo en la outbox en vez de enviarlo (AC1). Como todos los flujos (cambio de estado, placa, revocación,
/// reportes, invitaciones, recuperación) envían por este puerto, basta con probarlo aquí y en su registro; con la bandera
/// apagada el puerto es el envío en proceso de siempre (AC2).
/// </summary>
public sealed class CorreoPorBusEmailSenderTests
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AC1_UnCorreoDeEmpresa_QuedaComoTrabajoConElCanalDeLaEmpresa_YNoSaleEnProceso()
    {
        var (sender, enProceso, dbName, provider) = Crear(NotificationChannel.TenantApi);
        await using var _ = provider;

        var r = await sender.SendAsync(Mensaje("tramites.aprobado", Empresa), Ct);

        r.Success.Should().BeTrue();
        r.Channel.Should().Be("tenant_api");
        enProceso.Enviados.Should().BeEmpty();
        var trabajo = await TrabajoAsync(dbName);
        trabajo.Exchange.Should().Be("flit.notificaciones");
        trabajo.RoutingKey.Should().Be(TrabajoCorreo.Tipo);
        var sobre = EventEnvelope.FromJson(System.Text.Encoding.UTF8.GetBytes(trabajo.Payload));
        sobre.TenantId.Should().Be(Empresa);
        sobre.Producer.Should().Be("tramites");
        var datos = sobre.DataAs<TrabajoCorreo>();
        datos.Canal.Should().Be("tenant_api");
        datos.Html.Should().Be("<p>armado con la marca</p>", "el correo viaja ya armado");
        datos.Destinatario.Email.Should().Be("cliente@prueba.test");
    }

    [Fact]
    public async Task LosCorreosDeCuenta_VanSiemprePorFlit_AunqueLaEmpresaTengaSuApi()
    {
        var (sender, _, dbName, provider) = Crear(NotificationChannel.TenantApi);
        await using var _ = provider;

        await sender.SendAsync(Mensaje("security.invitation", Empresa), Ct);

        var sobre = EventEnvelope.FromJson(System.Text.Encoding.UTF8.GetBytes((await TrabajoAsync(dbName)).Payload));
        sobre.DataAs<TrabajoCorreo>().Canal.Should().Be("flit_smtp");
    }

    [Fact]
    public async Task UnCorreoSinEmpresa_SaleEnProceso()
    {
        var (sender, enProceso, dbName, provider) = Crear(NotificationChannel.FlitSmtp);
        await using var _ = provider;

        await sender.SendAsync(Mensaje("admin.mandato-simulacion", null), Ct);

        enProceso.Enviados.Should().ContainSingle();
        await using var db = NewContext(dbName);
        (await db.Set<OutboxMessage>().CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task SiNoSePuedeEncolar_RespondeProveedorNoDisponible_YElFlujoReintenta()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // Sin outbox registrada: encolar falla.
        await using var provider = services.BuildServiceProvider();
        var sender = new CorreoPorBusEmailSender(new EnProcesoFalso(), new CanalFijo(NotificationChannel.FlitSmtp),
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CorreoPorBusEmailSender>.Instance);

        var r = await sender.SendAsync(Mensaje("tramites.aprobado", Empresa), Ct);

        r.Success.Should().BeFalse();
        r.Outcome.Should().Be(EmailSendOutcome.ProviderUnavailable);
    }

    [Theory]
    [InlineData(true, nameof(CorreoPorBusEmailSender))]
    [InlineData(false, "NotificationDeliveryLoggingEmailSender")]
    public void ConLaBandera_ElPuertoDeCorreoEsElDelBus_YSinEllaElDeSiempre(bool remoto, string esperado)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPostgresInfrastructure(
            "Host=localhost;Database=flit_13355;Username=flit;Password=flit",
            Config(new()
            {
                ["Notificaciones:Remoto:Habilitado"] = remoto.ToString(),
                ["Tramites:Bus:Habilitado"] = remoto.ToString(),
                ["Platform:Messaging:Producer"] = "tramites",
                ["Platform:Messaging:ConnectionString"] = "amqp://tramites:x@127.0.0.1:5672/flit",
            }),
            new FakeEnvironment());
        services.AddScoped(_ => NSubstitute.Substitute.For<ITenantSettingsRepository>()); // lo registra Admin
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEmailSender>().GetType().Name.Should().Be(esperado);
    }

    [Fact]
    public void LaBanderaSinElBusDeTramites_NoArranca()
    {
        var registrar = () => new ServiceCollection().AddPostgresInfrastructure(
            "Host=localhost;Database=flit_13355;Username=flit;Password=flit",
            Config(new() { ["Notificaciones:Remoto:Habilitado"] = "true" }),
            new FakeEnvironment());

        registrar.Should().Throw<InvalidOperationException>().WithMessage("*Notificaciones:Remoto:Habilitado*Tramites:Bus:Habilitado*");
    }

    // ── Apoyo ────────────────────────────────────────────────────────────────────────────────────────────────────

    private static (CorreoPorBusEmailSender Sender, EnProcesoFalso EnProceso, string DbName, ServiceProvider Provider) Crear(NotificationChannel canal)
    {
        var dbName = $"flit-13355-{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => NewContext(dbName));
        services.AddScoped<IIdentityDb>(sp => sp.GetRequiredService<FlitDbContext>());
        services.AddFlitOutbox<FlitDbContext>(Config(new()
        {
            ["Platform:Messaging:Producer"] = "tramites",
            ["Platform:Messaging:ConnectionString"] = "amqp://tramites:x@127.0.0.1:5672/flit",
        }));
        var provider = services.BuildServiceProvider();
        var enProceso = new EnProcesoFalso();
        var sender = new CorreoPorBusEmailSender(enProceso, new CanalFijo(canal),
            provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CorreoPorBusEmailSender>.Instance);
        return (sender, enProceso, dbName, provider);
    }

    private static async Task<OutboxMessage> TrabajoAsync(string dbName)
    {
        await using var db = NewContext(dbName);
        return await db.Set<OutboxMessage>().SingleAsync(Ct);
    }

    private static EmailMessage Mensaje(string plantilla, Guid? empresa) =>
        new(empresa, plantilla, "cliente@prueba.test", "Cliente", "Asunto", "<p>armado con la marca</p>");

    private static IConfiguration Config(Dictionary<string, string?> valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

    private static FlitDbContext NewContext(string dbName) =>
        new(new DbContextOptionsBuilder<FlitDbContext>().UseInMemoryDatabase(dbName).Options);

    private sealed class EnProcesoFalso : IEmailSender
    {
        public List<EmailMessage> Enviados { get; } = [];

        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Enviados.Add(message);
            return Task.FromResult(EmailSendResult.Sent);
        }
    }

    private sealed class CanalFijo(NotificationChannel canal) : INotificationChannelResolver
    {
        public Task<NotificationChannel> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken) => Task.FromResult(canal);
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
