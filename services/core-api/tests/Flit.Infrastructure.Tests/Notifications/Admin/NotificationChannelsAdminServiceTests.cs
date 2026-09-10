using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications.Admin;
using Flit.Infrastructure.Notifications.Renting;
using Flit.Infrastructure.Notifications.Routing;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Admin;

/// <summary>
/// HU #11367 (Feature #11349) — remitente de cada canal resuelto LEYENDO configuración
/// (<see cref="EmailSettings"/> / <see cref="RentingChannelOptions"/>). Cubre AC1/AC2/AC3. HU #11371
/// añade que <c>IsConfigured</c> de <c>TENANT_API</c> usa la MISMA regla de disponibilidad que el
/// envío (<see cref="IExplicitChannelEmailSender.IsChannelAvailable"/>).
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// var explicitSender = Substitute.For&lt;IExplicitChannelEmailSender&gt;();
/// explicitSender.IsChannelAvailable(NotificationChannel.TenantApi).Returns(true);
/// var service = new NotificationChannelsAdminService(emailSettings, Options.Create(rentingOptions), explicitSender);
/// var channels = await service.GetAsync();
/// </code>
/// </remarks>
public sealed class NotificationChannelsAdminServiceTests
{
    private static IExplicitChannelEmailSender ExplicitSender(bool tenantApiAvailable)
    {
        var sender = Substitute.For<IExplicitChannelEmailSender>();
        sender.IsChannelAvailable(NotificationChannel.FlitSmtp).Returns(true);
        sender.IsChannelAvailable(NotificationChannel.TenantApi).Returns(tenantApiAvailable);
        return sender;
    }

    // ── AC1 — dos canales, el por defecto es Colas FLIT con el remitente de la config SMTP ──

    [Fact]
    public async Task AC1_GetAsync_ConSmtpPoblado_DevuelveDosCanalesYFlitSmtpEsElDefaultConSuRemitente()
    {
        var emailSettings = new EmailSettings
        {
            Host = "smtp.office365.com",
            DefaultSenderEmail = "tramitesvehiculos@flitsas.com",
            DefaultSenderName = "FLIT Trámites",
        };
        var rentingOptions = Options.Create(new RentingChannelOptions());

        var service = new NotificationChannelsAdminService(emailSettings, rentingOptions, ExplicitSender(tenantApiAvailable: false));
        var channels = await service.GetAsync(TestContext.Current.CancellationToken);

        channels.Should().HaveCount(2);

        var flitSmtp = channels.Single(c => c.Channel == "FLIT_SMTP");
        flitSmtp.IsDefault.Should().BeTrue();
        flitSmtp.IsConfigured.Should().BeTrue();
        flitSmtp.SenderEmail.Should().Be("tramitesvehiculos@flitsas.com");
        flitSmtp.SenderName.Should().Be("FLIT Trámites");

        var tenantApi = channels.Single(c => c.Channel == "TENANT_API");
        tenantApi.IsDefault.Should().BeFalse();
    }

    // ── AC2 — el canal del cliente expone su propio remitente, distinto del de Colas FLIT ────

    [Fact]
    public async Task AC2_GetAsync_ConVariablesDelClienteDefinidasYAdaptadorRegistrado_TenantApiExponeSuPropioRemitenteDistinto()
    {
        var emailSettings = new EmailSettings
        {
            DefaultSenderEmail = "tramitesvehiculos@flitsas.com",
            DefaultSenderName = "FLIT Trámites",
        };
        var rentingOptions = Options.Create(new RentingChannelOptions
        {
            SendEmailSenderEmail = "no-reply@renting-cliente.test",
            SendEmailSenderUsername = "renting-notificaciones",
        });

        var service = new NotificationChannelsAdminService(emailSettings, rentingOptions, ExplicitSender(tenantApiAvailable: true));
        var channels = await service.GetAsync(TestContext.Current.CancellationToken);

        var tenantApi = channels.Single(c => c.Channel == "TENANT_API");
        tenantApi.IsConfigured.Should().BeTrue();
        tenantApi.SenderEmail.Should().Be("no-reply@renting-cliente.test");
        tenantApi.SenderName.Should().Be("renting-notificaciones");

        var flitSmtp = channels.Single(c => c.Channel == "FLIT_SMTP");
        tenantApi.SenderEmail.Should().NotBe(flitSmtp.SenderEmail);
    }

    // ── AC3 — canal sin configurar: remitente vacío + marca de sin configurar, sigue siendo 200 ──

    [Fact]
    public async Task AC3_GetAsync_SinVariablesDelCliente_TenantApiDevuelveRemitenteVacioYSinConfigurar()
    {
        var emailSettings = new EmailSettings
        {
            DefaultSenderEmail = "tramitesvehiculos@flitsas.com",
            DefaultSenderName = "FLIT Trámites",
        };
        var rentingOptions = Options.Create(new RentingChannelOptions());

        var service = new NotificationChannelsAdminService(emailSettings, rentingOptions, ExplicitSender(tenantApiAvailable: false));
        var channels = await service.GetAsync(TestContext.Current.CancellationToken);

        var tenantApi = channels.Single(c => c.Channel == "TENANT_API");
        tenantApi.IsConfigured.Should().BeFalse();
        tenantApi.SenderEmail.Should().BeNull();
        tenantApi.SenderName.Should().BeNull();

        // AC3 — "no es un error: es información". El resultado del use case no lanza ni marca fallo.
        channels.Should().HaveCount(2);
    }

    // ── HU #11371 — IsConfigured de TENANT_API sigue la MISMA regla de disponibilidad del envío ──

    [Fact]
    public async Task HU11371_ConVariablesDelClientePobladasPeroAdaptadorNoRegistrado_TenantApiNoEstaConfigurado()
    {
        // Reproduce la divergencia que esta HU cierra: las variables del canal SÍ están pobladas en
        // configuración, pero el adaptador Renting NO está registrado en este ambiente
        // (RENTING_API_ENABLED=false, como se despliega hoy) — IsConfigured debe seguir a la
        // disponibilidad real, no a si las variables están pobladas.
        var emailSettings = new EmailSettings
        {
            DefaultSenderEmail = "tramitesvehiculos@flitsas.com",
            DefaultSenderName = "FLIT Trámites",
        };
        var rentingOptions = Options.Create(new RentingChannelOptions
        {
            SendEmailSenderEmail = "no-reply@renting-cliente.test",
            SendEmailSenderUsername = "renting-notificaciones",
        });

        var service = new NotificationChannelsAdminService(emailSettings, rentingOptions, ExplicitSender(tenantApiAvailable: false));
        var channels = await service.GetAsync(TestContext.Current.CancellationToken);

        var tenantApi = channels.Single(c => c.Channel == "TENANT_API");
        tenantApi.IsConfigured.Should().BeFalse(
            "el adaptador Renting no está registrado en este ambiente, aunque las variables del canal estén pobladas");
        // El remitente informativo sigue viniendo de la configuración, sin cambios.
        tenantApi.SenderEmail.Should().Be("no-reply@renting-cliente.test");
    }
}
