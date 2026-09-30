using Flit.Admin.Application.Integrations.Auth;
using Flit.Admin.Domain.Integrations;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Integrations;

/// <summary>
/// HU #13087 (Feature #13065, Épica #12737) contra Postgres real: el conteo de fallos y el bloqueo en una
/// sola sentencia (AC3, también con fallos simultáneos), la limpieza al emitir (AC1) y la ventana de gracia
/// del secreto anterior tras una rotación (AC4), con el handler, el repositorio y el Argon2id reales.
/// </summary>
public sealed class ExternalClientTokenRepositoryTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    // Secretos de prueba generados para esta prueba; no son credenciales reales.
    private const string Secreto = "it13087-secreto-de-prueba-4b1e0d";
    private const string SecretoNuevo = "it13087-secreto-nuevo-7c3a9f";
    private static readonly Argon2PasswordHasher Hasher = new();
    private static readonly TimeSpan Bloqueo = TimeSpan.FromMinutes(15);

    [PostgresFact]
    public async Task AC3_ElQuintoFalloBloquea15MinutosYReiniciaElContador()
    {
        var id = await AltaAsync("it-fallos");
        var ahora = DateTimeOffset.UtcNow;

        for (var i = 1; i <= 4; i++)
        {
            (await Repo().RegisterFailedAttemptAsync(id, 5, Bloqueo, ahora)).Should().BeNull();
        }

        (await EscalarAsync<int>($"SELECT failed_attempts FROM integrations.external_clients WHERE id = '{id}'")).Should().Be(4);

        var bloqueadoHasta = await Repo().RegisterFailedAttemptAsync(id, 5, Bloqueo, ahora);

        bloqueadoHasta.Should().BeCloseTo(ahora.Add(Bloqueo), TimeSpan.FromMilliseconds(1));
        (await EscalarAsync<int>($"SELECT failed_attempts FROM integrations.external_clients WHERE id = '{id}'")).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC3_CincoFallosSimultaneosBloqueanIgual()
    {
        var id = await AltaAsync("it-simultaneos");
        var ahora = DateTimeOffset.UtcNow;

        var resultados = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => Repo().RegisterFailedAttemptAsync(id, 5, Bloqueo, ahora))));

        resultados.Count(r => r is not null).Should().Be(1, "exactamente el quinto fallo, sea cual sea, bloquea");
        (await EscalarAsync<bool>($"SELECT locked_until > now() FROM integrations.external_clients WHERE id = '{id}'")).Should().BeTrue();
    }

    [PostgresFact]
    public async Task AC1_ElPaseEmitidoLimpiaIntentosYBloqueoYSellaElUltimoPase()
    {
        var id = await AltaAsync("it-emitido");
        await EjecutarAsync($"UPDATE integrations.external_clients SET failed_attempts = 3, locked_until = now() - interval '1 minute' WHERE id = '{id}'");
        var ahora = DateTimeOffset.UtcNow;

        await Repo().RegisterTokenIssuedAsync(id, ahora);

        (await EscalarAsync<int>($"SELECT failed_attempts FROM integrations.external_clients WHERE id = '{id}'")).Should().Be(0);
        (await EscalarAsync<bool>($"SELECT locked_until IS NULL FROM integrations.external_clients WHERE id = '{id}'")).Should().BeTrue();
        (await EscalarAsync<bool>($"SELECT last_token_at IS NOT NULL FROM integrations.external_clients WHERE id = '{id}'")).Should().BeTrue();
    }

    [PostgresFact]
    public async Task AC3_ConElHandler_TrasCincoFallosNiElSecretoCorrectoEntra()
    {
        await AltaAsync("it-handler-bloqueo");

        for (var i = 0; i < 5; i++)
        {
            (await Handler().HandleAsync(new("it-handler-bloqueo", "equivocado"))).Status.Should().Be(ExternalTokenStatus.InvalidClient);
        }

        var result = await Handler().HandleAsync(new("it-handler-bloqueo", Secreto));

        result.Status.Should().Be(ExternalTokenStatus.Locked);
        result.RetryAfter.Should().BeGreaterThan(TimeSpan.FromMinutes(14));
    }

    [PostgresFact]
    public async Task AC4_ConElHandler_ElSecretoAnteriorValeSoloDentroDeLaVentana()
    {
        var id = await AltaAsync("it-handler-rotado", SecretoNuevo);
        await EjecutarAsync(
            $"UPDATE integrations.external_clients SET previous_secret_hash = '{Hasher.Hash(Secreto)}', secret_rotated_at = now() - interval '23 hours' WHERE id = '{id}'");

        (await Handler().HandleAsync(new("it-handler-rotado", Secreto))).Status.Should().Be(ExternalTokenStatus.Issued);
        (await Handler().HandleAsync(new("it-handler-rotado", SecretoNuevo))).Status.Should().Be(ExternalTokenStatus.Issued);

        await EjecutarAsync($"UPDATE integrations.external_clients SET secret_rotated_at = now() - interval '25 hours' WHERE id = '{id}'");

        (await Handler().HandleAsync(new("it-handler-rotado", Secreto))).Status.Should().Be(ExternalTokenStatus.InvalidClient);
        (await Handler().HandleAsync(new("it-handler-rotado", SecretoNuevo))).Status.Should().Be(ExternalTokenStatus.Issued);
    }

    private ExternalClientRepository Repo() => new(NewContext());

    private IssueExternalClientTokenHandler Handler() => new(
        Repo(), new ExternalClientSecretHasher(Hasher), new StubIssuer(), new ExternalClientAuthSettings(), TimeProvider.System);

    private async Task<Guid> AltaAsync(string clientId, string secreto = Secreto)
    {
        var creado = await Repo().CreateAsync(new NewExternalClient(
            clientId, "Pruebas", "Pruebas de la HU #13087", Hasher.Hash(secreto), [ExternalScopes.TramitesRead], null));
        return creado.Id;
    }

    private async Task EjecutarAsync(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<T> EscalarAsync<T>(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private sealed class StubIssuer : IExternalClientTokenIssuer
    {
        public bool IsAvailable => true;

        public ExternalAccessToken Issue(string clientId, IReadOnlyList<string> scopes) => new("jwt", 1800);
    }
}
