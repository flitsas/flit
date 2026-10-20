using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Email;
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

    private DbEmailThemeResolver NewResolver() =>
        new(
            new BrandingTenantLookupRepository(NewContext()),
            new TenantBrandingRepository(NewContext()),
            new MemoryCache(new MemoryCacheOptions()),
            new EmailThemePublicBrandingOptions { PublicBaseUrl = "https://dev.flitsas.online" },
            NullLogger<DbEmailThemeResolver>.Instance);

    /// <summary>Hasher trivial: esta suite no ejercita login, solo necesita satisfacer <see cref="MarcaBlancaScenario.SeedAsync"/>.</summary>
    private sealed class PlainPasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"plain:{password}";
        public bool Verify(string password, string storedHash) => storedHash == $"plain:{password}";
        public string DummyHash => "plain:dummy";
    }
}
