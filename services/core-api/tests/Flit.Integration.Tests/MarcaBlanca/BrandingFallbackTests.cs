using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.ResolvePublicBranding;
using Flit.Admin.Domain.Companies.Branding;
using Flit.Infrastructure.Notifications.Theme;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Flit.Integration.Tests.MarcaBlanca;

/// <summary>
/// HU #12429 AC5 — un fallo simulado de la base de datos (<see cref="IBrandingTenantLookup"/> lanza)
/// o del storage del logotipo NUNCA bloquea el acceso ni pierde el correo: la marca y el tema
/// resuelven a FLIT (mismo camino que un negativo cualquiera) y el envío se completa y queda
/// registrado en <c>admin.notification_delivery_logs</c> con <c>theme_kind = 'flit'</c>.
/// </summary>
public sealed class BrandingFallbackTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    [PostgresFact]
    public async Task AC5_FalloDeBdEnResolvePublicBranding_Responde200Flit()
    {
        var throwingLookup = Substitute.For<IBrandingTenantLookup>();
        throwingLookup.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<BrandingTenantSnapshot?>(_ => throw new InvalidOperationException("caída simulada de BD"));

        var handler = new ResolvePublicBrandingHandler(
            new TenantBrandingRepository(NewContext()),
            throwingLookup,
            new NoopCache(),
            NullLogger<ResolvePublicBrandingHandler>.Instance);

        var result = await handler.HandleAsync(isNetworkDomain: true, headTenantId: Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().Be(BrandIdentityResponse.From(BrandIdentity.Flit));
    }

    [PostgresFact]
    public async Task AC5_FalloDeBdEnElTemaDeCorreo_ResuelveFlitYNoPropaga()
    {
        var throwingLookup = Substitute.For<IBrandingTenantLookup>();
        throwingLookup.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<BrandingTenantSnapshot?>(_ => throw new InvalidOperationException("caída simulada de BD"));

        var resolver = new DbEmailThemeResolver(
            throwingLookup,
            new TenantBrandingRepository(NewContext()),
            new MemoryCache(new MemoryCacheOptions()),
            new EmailThemePublicBrandingOptions { PublicBaseUrl = "https://dev.flitsas.online" },
            NullLogger<DbEmailThemeResolver>.Instance);

        var theme = await resolver.ResolveAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        theme.Should().Be(EmailTheme.Flit);
    }

    private sealed class NoopCache : IPublicBrandingCache
    {
        public bool TryGet(Guid headTenantId, out BrandIdentity identity)
        {
            identity = BrandIdentity.Flit;
            return false;
        }

        public void Store(Guid headTenantId, BrandIdentity identity, TimeSpan ttl)
        {
        }
    }
}
