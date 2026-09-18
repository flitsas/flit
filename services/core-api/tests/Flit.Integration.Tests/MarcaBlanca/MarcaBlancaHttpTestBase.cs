using Flit.Infrastructure.Persistence;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Domain.Auth;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flit.Integration.Tests.MarcaBlanca;

/// <summary>
/// HU #12429 — base compartida de las suites HTTP end-to-end (AC2 <c>LoginAntiEnumerationTests</c>,
/// AC3 <c>RecoveryAndBrandingAntiEnumerationTests</c>, AC4 <c>CrossNetworkIsolationTests</c>): un
/// <see cref="MarcaBlancaApiFactory"/> por prueba, sobre la base efímera recién reseteada + el
/// escenario de <see cref="MarcaBlancaScenario"/> ya sembrado. Un <see cref="MarcaBlancaApiFactory"/>
/// por prueba (no por clase) es deliberado: el reset de <see cref="PostgresDatabaseFixture"/> es
/// anterior a cada prueba (patrón <c>PostgresTestBase</c>) y un host HTTP compartido reutilizando una
/// base ya truncada dejaría vivas cachés en memoria (branding pública, tema de correo) entre pruebas.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public abstract class MarcaBlancaHttpTestBase(PostgresDatabaseFixture fixture) : IAsyncLifetime
{
    protected PostgresDatabaseFixture Fixture { get; } = fixture;

    protected MarcaBlancaApiFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    public virtual async ValueTask InitializeAsync()
    {
        if (!PostgresAvailability.IsAvailable)
        {
            return; // fuera de CI el atributo [PostgresFact] ya omitió la prueba.
        }

        await Fixture.ResetAsync();

        Factory = new MarcaBlancaApiFactory(Fixture);
        Client = Factory.CreateClient();

        var hasher = Factory.Services.GetRequiredService<IPasswordHasher>();
        await MarcaBlancaScenario.SeedAsync(Fixture, hasher);
    }

    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        Client?.Dispose();
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
    }

    protected FlitDbContext NewContext() => Fixture.CreateDbContext();
}
