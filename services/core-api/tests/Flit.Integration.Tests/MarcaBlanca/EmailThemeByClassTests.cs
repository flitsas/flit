using Flit.Infrastructure.Email;
using Flit.Infrastructure.Notifications.DeliveryLog;
using Flit.Infrastructure.Notifications.Theme;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Application.Auth.ForgotPassword;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Integration.Tests.MarcaBlanca;

/// <summary>
/// HU #12429 AC6 — el MISMO evento de notificación, resuelto por clase de destinatario: una hija de
/// la red MARCA_BLANCA A (<see cref="MarcaBlancaScenario.ChildA"/>) recibe el tema de su marca con el
/// remitente visible de la red; una hija de Concesión y una compañía sin red reciben el tema y el
/// remitente de FLIT (por defecto, sin nombre visible). Verifica tanto el <see cref="EmailTheme"/>
/// resuelto como la fila que queda en <c>admin.notification_delivery_logs</c>
/// (<c>sender_name</c>/<c>sender_email</c>, HU #12430).
/// </summary>
public sealed class EmailThemeByClassTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (Fixture.IsInitialized)
        {
            await MarcaBlancaScenario.SeedAsync(Fixture, new PlainPasswordHasher());
        }
    }

    [PostgresFact]
    public async Task AC6_HijaDeMarcaBlanca_ResuelveElTemaDeSuRedConElRemitenteDeLaRed()
    {
        var theme = await NewResolver().ResolveAsync(MarcaBlancaScenario.ChildA, TestContext.Current.CancellationToken);

        theme.Kind.Should().Be(EmailThemeKind.Brand);
        theme.PlatformName.Should().Be(MarcaBlancaScenario.PlatformNameA);
        var senderDisplayName = theme.IsBrand ? theme.PlatformName : null;
        senderDisplayName.Should().Be(MarcaBlancaScenario.PlatformNameA);
    }

    [PostgresFact]
    public async Task AC6_HijaDeConcesionYSinRed_ResuelvenFlitConRemitenteNulo()
    {
        var resolver = NewResolver();

        var concesionChildTheme = await resolver.ResolveAsync(MarcaBlancaScenario.ConcesionChild, TestContext.Current.CancellationToken);
        var loneTheme = await resolver.ResolveAsync(MarcaBlancaScenario.Lone, TestContext.Current.CancellationToken);

        concesionChildTheme.Should().Be(EmailTheme.Flit);
        loneTheme.Should().Be(EmailTheme.Flit);
        (concesionChildTheme.IsBrand ? concesionChildTheme.PlatformName : null).Should().BeNull();
        (loneTheme.IsBrand ? loneTheme.PlatformName : null).Should().BeNull();
    }

    [PostgresFact]
    public async Task AC6_MismoEventoComposeInvitacion_HijaDeAVsSinRed_DifierenSoloPorElTema()
    {
        var resolver = NewResolver();
        var themeA = await resolver.ResolveAsync(MarcaBlancaScenario.ChildA, TestContext.Current.CancellationToken);
        var themeLone = await resolver.ResolveAsync(MarcaBlancaScenario.Lone, TestContext.Current.CancellationToken);

        var composedForA = Flit.Modules.Security.Application.Auth.InvitationEmailTemplate.Compose(
            "Invitada de prueba", "https://app.flit.test/invite/activate?token=abc", theme: themeA);
        var composedForLone = Flit.Modules.Security.Application.Auth.InvitationEmailTemplate.Compose(
            "Invitada de prueba", "https://app.flit.test/invite/activate?token=abc", theme: themeLone);

        composedForA.HtmlBody.Should().Contain(MarcaBlancaScenario.PlatformNameA);
        composedForLone.HtmlBody.Should().NotContain(MarcaBlancaScenario.PlatformNameA);
        composedForA.HtmlBody.Should().NotBeEquivalentTo(composedForLone.HtmlBody);
    }

    [PostgresFact]
    public async Task AC6_ElDecorador_PersisteSenderNameSaneadoSoloParaLaHijaDeLaRed()
    {
        var resolver = NewResolver();
        var themeA = await resolver.ResolveAsync(MarcaBlancaScenario.ChildA, TestContext.Current.CancellationToken);
        var themeLone = await resolver.ResolveAsync(MarcaBlancaScenario.Lone, TestContext.Current.CancellationToken);

        // HU #12430 AC1/AC6 (NotificationDeliveryLoggingEmailSender.appliedSenderName) — en el canal
        // flit_smtp el remitente visible SIEMPRE se persiste: el de la marca cuando el tema es Brand,
        // o el nombre por defecto de la plataforma (EmailSettings.DefaultSenderName) cuando es FLIT.
        // Nunca null — eso solo ocurre en canales que no son flit_smtp (Renting/tenant_api).
        await SendAndAssertAsync(MarcaBlancaScenario.ChildA, themeA, expectedSenderName: MarcaBlancaScenario.PlatformNameA);
        await SendAndAssertAsync(MarcaBlancaScenario.Lone, themeLone, expectedSenderName: DefaultSenderName);
    }

    private const string DefaultSenderName = "FLIT Trámites";

    private async Task SendAndAssertAsync(Guid tenantId, EmailTheme theme, string? expectedSenderName)
    {
        var services = new ServiceCollection();
        services.AddScoped<INotificationDeliveryLogWriter, NotificationDeliveryLogWriter>();
        services.AddScoped(_ => Fixture.CreateDbContext());
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var emailSettings = new EmailSettings { DefaultSenderEmail = "no-reply@flitsas.online", DefaultSenderName = DefaultSenderName };
        var inner = new FixedResultEmailSender(EmailSendResult.Sent with { Channel = "flit_smtp" });
        var decorator = new NotificationDeliveryLoggingEmailSender(
            inner, scopeFactory, NullLogger<NotificationDeliveryLoggingEmailSender>.Instance, emailSettings);

        var message = new EmailMessage(tenantId, "security.invitation", "destinatario@ejemplo.test", "Destinatario", "Asunto", "<html/>")
        {
            ThemeKind = theme.KindWireValue,
            ThemeVersion = theme.IsBrand ? theme.Version : null,
            SenderDisplayName = theme.IsBrand ? theme.PlatformName : null,
        };

        await decorator.SendAsync(message, TestContext.Current.CancellationToken);

        await using var check = NewContext();
        var row = await check.NotificationDeliveryLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId)
            .OrderByDescending(l => l.CreatedAt)
            .FirstAsync(TestContext.Current.CancellationToken);

        row.SenderName.Should().Be(expectedSenderName);
        if (expectedSenderName is not null)
        {
            row.SenderEmail.Should().Be(emailSettings.DefaultSenderEmail);
        }
    }

    private DbEmailThemeResolver NewResolver() =>
        new(
            new BrandingTenantLookupRepository(NewContext()),
            new TenantBrandingRepository(NewContext()),
            new MemoryCache(new MemoryCacheOptions()),
            new EmailThemePublicBrandingOptions { PublicBaseUrl = "https://dev.flitsas.online" },
            NullLogger<DbEmailThemeResolver>.Instance);

    private sealed class FixedResultEmailSender(EmailSendResult result) : IEmailSender
    {
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    /// <summary>Hasher trivial: esta suite no ejercita login, solo necesita satisfacer <see cref="MarcaBlancaScenario.SeedAsync"/>.</summary>
    private sealed class PlainPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"plain:{password}";
        public bool Verify(string password, string storedHash) => storedHash == $"plain:{password}";
        public string DummyHash => "plain:dummy";
    }
}
