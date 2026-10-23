using Flit.Admin.Domain.Companies.Settings;
using Flit.Infrastructure.Notifications.Admin;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Xunit;

namespace Flit.Infrastructure.Tests.Notifications.Admin;

/// <summary>
/// HU #11367 (Feature #11349) — remitente de cada canal (AC1/AC2/AC3). HU #11371: <c>IsConfigured</c> de
/// <c>TENANT_API</c> sigue la MISMA regla de disponibilidad que el envío. HU #13359: los dos datos vienen de
/// core-notificaciones (<see cref="ICanalesDeNotificaciones"/>).
/// </summary>
public sealed class NotificationChannelsAdminServiceTests
{
    private static readonly CanalDeNotificacion Flit = new(NotificationChannel.FlitSmtp, true, "tramitesvehiculos@flitsas.com", "FLIT Trámites", Consola: false);

    // ── AC1 — dos canales, el por defecto es Colas FLIT con el remitente de la config SMTP ──

    [Fact]
    public async Task AC1_GetAsync_ConSmtpPoblado_DevuelveDosCanalesYFlitSmtpEsElDefaultConSuRemitente()
    {
        var channels = await Servicio(Flit, new(NotificationChannel.TenantApi, false, null, null, false)).GetAsync(TestContext.Current.CancellationToken);

        channels.Should().HaveCount(2);
        var flitSmtp = channels.Single(c => c.Channel == "FLIT_SMTP");
        flitSmtp.IsDefault.Should().BeTrue();
        flitSmtp.IsConfigured.Should().BeTrue();
        flitSmtp.SenderEmail.Should().Be("tramitesvehiculos@flitsas.com");
        flitSmtp.SenderName.Should().Be("FLIT Trámites");
        channels.Single(c => c.Channel == "TENANT_API").IsDefault.Should().BeFalse();
    }

    // ── AC2 — el canal del cliente expone su propio remitente, distinto del de Colas FLIT ────

    [Fact]
    public async Task AC2_ConElCanalDelClienteDisponible_TenantApiExponeSuPropioRemitenteDistinto()
    {
        var channels = await Servicio(Flit, new(NotificationChannel.TenantApi, true, "no-reply@renting-cliente.test", "renting-notificaciones", false))
            .GetAsync(TestContext.Current.CancellationToken);

        var tenantApi = channels.Single(c => c.Channel == "TENANT_API");
        tenantApi.IsConfigured.Should().BeTrue();
        tenantApi.SenderEmail.Should().Be("no-reply@renting-cliente.test");
        tenantApi.SenderName.Should().Be("renting-notificaciones");
        tenantApi.SenderEmail.Should().NotBe(channels.Single(c => c.Channel == "FLIT_SMTP").SenderEmail);
    }

    // ── AC3 — canal sin configurar: remitente vacío + marca de sin configurar, sigue siendo 200 ──

    [Fact]
    public async Task AC3_SinVariablesDelCliente_TenantApiDevuelveRemitenteVacioYSinConfigurar()
    {
        var channels = await Servicio(Flit, new(NotificationChannel.TenantApi, false, null, null, false)).GetAsync(TestContext.Current.CancellationToken);

        var tenantApi = channels.Single(c => c.Channel == "TENANT_API");
        tenantApi.IsConfigured.Should().BeFalse();
        tenantApi.SenderEmail.Should().BeNull();
        tenantApi.SenderName.Should().BeNull();
        channels.Should().HaveCount(2, "no es un error: es información");
    }

    // ── HU #11371 — IsConfigured de TENANT_API sigue la disponibilidad real, no si hay remitente ──

    [Fact]
    public async Task HU11371_ConRemitentePobladoPeroCanalNoDisponible_TenantApiNoEstaConfigurado()
    {
        var channels = await Servicio(Flit, new(NotificationChannel.TenantApi, false, "no-reply@renting-cliente.test", "renting-notificaciones", false))
            .GetAsync(TestContext.Current.CancellationToken);

        var tenantApi = channels.Single(c => c.Channel == "TENANT_API");
        tenantApi.IsConfigured.Should().BeFalse("la API del cliente no está habilitada en Notificaciones, aunque tenga remitente");
        tenantApi.SenderEmail.Should().Be("no-reply@renting-cliente.test");
    }

    // ── HU #13359 — Notificaciones caído: la pantalla responde, con los dos canales sin configurar ──

    [Fact]
    public async Task HU13359_NotificacionesCaido_DosCanalesSinConfigurar()
    {
        var channels = await new NotificationChannelsAdminService(new CanalesFalsos(caido: true)).GetAsync(TestContext.Current.CancellationToken);

        channels.Should().HaveCount(2).And.OnlyContain(c => !c.IsConfigured && c.SenderEmail == null);
    }

    private static NotificationChannelsAdminService Servicio(params CanalDeNotificacion[] canales) => new(new CanalesFalsos(canales));

    private sealed class CanalesFalsos(CanalDeNotificacion[]? canales = null, bool caido = false) : ICanalesDeNotificaciones
    {
        public CanalesFalsos(bool caido)
            : this(null, caido)
        {
        }

        public Task<IReadOnlyList<CanalDeNotificacion>> ListarAsync(CancellationToken ct) =>
            caido
                ? throw new NotificacionesNoDisponibleException("caído", new InvalidOperationException())
                : Task.FromResult<IReadOnlyList<CanalDeNotificacion>>(canales ?? []);

        public Task<EmailSendResult> EnviarPruebaAsync(NotificationChannel canal, EmailMessage mensaje, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
