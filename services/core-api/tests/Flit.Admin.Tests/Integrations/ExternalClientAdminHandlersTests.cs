using Flit.Admin.Application.Integrations.Clients;
using Flit.Admin.Domain.Integrations;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Integrations;

/// <summary>
/// HU #13088 — reglas de la administración de clientes externos (AC1, AC2), sin base ni HTTP.
/// <code>
/// var (result, error) = await create.HandleAsync(new("flito-dev", "Flito DEV", finalidad, [ExternalScopes.TramitesRead], actor));
/// // result.ClientSecret se muestra una vez; result.Client no lleva hashes
/// </code>
/// </summary>
public sealed class ExternalClientAdminHandlersTests
{
    private const string Finalidad = "Sincronización de trámites para procesos Flito";
    private static readonly Guid Actor = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset Ahora = new(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);

    private readonly IExternalClientRepository _clients = Substitute.For<IExternalClientRepository>();
    private readonly FakeHasher _hasher = new();

    [Fact]
    public async Task AC1_ElAltaGeneraUnSecretoFuerteYSoloGuardaSuHash()
    {
        NewExternalClient? guardado = null;
        _clients.CreateAsync(Arg.Do<NewExternalClient>(c => guardado = c), Arg.Any<CancellationToken>())
            .Returns(ci => Vista(ci.Arg<NewExternalClient>().ClientId));

        var (result, error) = await new CreateExternalClientHandler(_clients, _hasher).HandleAsync(
            new("flito-dev", " Flito DEV ", Finalidad, [ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead, ExternalScopes.TramitesRead], Actor),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.ClientSecret.Should().MatchRegex("^[A-Za-z0-9_-]{43}$", "32 bytes en base64url");
        guardado!.SecretHash.Should().Be(FakeHasher.HashOf(result.ClientSecret)).And.NotContain(result.ClientSecret[..10]);
        guardado.DisplayName.Should().Be("Flito DEV");
        guardado.Scopes.Should().Equal(ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead);
        guardado.CreatedBy.Should().Be(Actor);
    }

    [Fact]
    public async Task AC1_DosAltasNoRepitenSecreto()
    {
        _clients.CreateAsync(Arg.Any<NewExternalClient>(), Arg.Any<CancellationToken>())
            .Returns(ci => Vista(ci.Arg<NewExternalClient>().ClientId));
        var handler = new CreateExternalClientHandler(_clients, _hasher);

        var a = await handler.HandleAsync(new("flito-dev", "A", Finalidad, [ExternalScopes.TramitesRead], Actor), TestContext.Current.CancellationToken);
        var b = await handler.HandleAsync(new("flito-qa", "B", Finalidad, [ExternalScopes.TramitesRead], Actor), TestContext.Current.CancellationToken);

        a.Result!.ClientSecret.Should().NotBe(b.Result!.ClientSecret);
    }

    [Theory]
    [InlineData("Flito-Dev", "Flito", Finalidad, ExternalClientAdminErrors.InvalidClientId)]
    [InlineData("fd", "Flito", Finalidad, ExternalClientAdminErrors.InvalidClientId)]
    [InlineData(null, "Flito", Finalidad, ExternalClientAdminErrors.InvalidClientId)]
    [InlineData("flito-dev", " ", Finalidad, ExternalClientAdminErrors.InvalidDisplayName)]
    [InlineData("flito-dev", "Flito", "", ExternalClientAdminErrors.InvalidPurpose)]
    public async Task AC1_DatosInvalidosSeRechazanSinTocarLaBase(string? clientId, string displayName, string purpose, string esperado)
    {
        var (_, error) = await new CreateExternalClientHandler(_clients, _hasher).HandleAsync(
            new(clientId, displayName, purpose, [ExternalScopes.TramitesRead], Actor), TestContext.Current.CancellationToken);

        error.Should().Be(esperado);
        await _clients.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ict.transactions.write")]
    [InlineData(ExternalScopes.TramitesRead + ",admin")]
    public async Task AC1_SoloPermisosDelContratoYAlMenosUno(string lista)
    {
        var scopes = lista.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var (_, error) = await new CreateExternalClientHandler(_clients, _hasher).HandleAsync(
            new("flito-dev", "Flito", Finalidad, scopes, Actor), TestContext.Current.CancellationToken);

        error.Should().Be(ExternalClientAdminErrors.InvalidScopes);
    }

    [Fact]
    public async Task AC1_IdentificadorRepetido_EsClientIdTaken()
    {
        _clients.CreateAsync(Arg.Any<NewExternalClient>(), Arg.Any<CancellationToken>())
            .Returns<ExternalClientView>(_ => throw new ExternalClientAlreadyExistsException("flito-dev"));

        var (_, error) = await new CreateExternalClientHandler(_clients, _hasher).HandleAsync(
            new("flito-dev", "Flito", Finalidad, [ExternalScopes.TramitesRead], Actor), TestContext.Current.CancellationToken);

        error.Should().Be(ExternalClientAdminErrors.ClientIdTaken);
    }

    [Fact]
    public async Task AC2_EditarPasaSoloLosCambiosYElAutor()
    {
        _clients.UpdateAsync(default, default!, default, default, default).ReturnsForAnyArgs(Vista("flito-dev"));

        var (result, error) = await new UpdateExternalClientHandler(_clients, new FixedTime(Ahora)).HandleAsync(
            new(Guid.Empty, null, null, [ExternalScopes.TramitesRead], IsActive: false, MustRotate: true, Actor),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();
        await _clients.Received(1).UpdateAsync(
            Guid.Empty,
            Arg.Is<ExternalClientChanges>(c => c.DisplayName == null && c.Purpose == null && c.IsActive == false
                && c.MustRotate == true && c.Scopes!.SequenceEqual(new[] { ExternalScopes.TramitesRead })),
            Actor, Ahora, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_EditarConPermisosVaciosSeRechaza()
    {
        var (_, error) = await new UpdateExternalClientHandler(_clients, new FixedTime(Ahora)).HandleAsync(
            new(Guid.Empty, null, null, [], null, null, Actor), TestContext.Current.CancellationToken);

        error.Should().Be(ExternalClientAdminErrors.InvalidScopes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AC2_RegenerarDevuelveElSecretoUnaVezYRespetaRevocarAnterior(bool revocar)
    {
        string? hashGuardado = null;
        _clients.ReplaceSecretAsync(default, default!, default, default, default, default)
            .ReturnsForAnyArgs(ci => { hashGuardado = ci.ArgAt<string>(1); return Vista("flito-dev"); });

        var (result, error) = await new RegenerateExternalClientSecretHandler(_clients, _hasher, new FixedTime(Ahora))
            .HandleAsync(Guid.Empty, revocar, Actor, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        hashGuardado.Should().Be(FakeHasher.HashOf(result!.ClientSecret));
        await _clients.Received(1).ReplaceSecretAsync(Guid.Empty, Arg.Any<string>(), revocar, Actor, Ahora, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC2_ClienteInexistente_EsNotFoundEnEditarRegenerarYDesbloquear()
    {
        var id = Guid.NewGuid();

        (await new UpdateExternalClientHandler(_clients, new FixedTime(Ahora))
            .HandleAsync(new(id, "X", null, null, null, null, Actor), TestContext.Current.CancellationToken)).Error
            .Should().Be(ExternalClientAdminErrors.NotFound);
        (await new RegenerateExternalClientSecretHandler(_clients, _hasher, new FixedTime(Ahora))
            .HandleAsync(id, false, Actor, TestContext.Current.CancellationToken)).Error
            .Should().Be(ExternalClientAdminErrors.NotFound);
        (await new UnlockExternalClientHandler(_clients, new FixedTime(Ahora))
            .HandleAsync(id, Actor, TestContext.Current.CancellationToken)).Error
            .Should().Be(ExternalClientAdminErrors.NotFound);
    }

    private static ExternalClientView Vista(string clientId) =>
        new(Guid.CreateVersion7(), clientId, "Flito", Finalidad, [ExternalScopes.TramitesRead], true, false, null, null, Ahora);

    private sealed class FakeHasher : IExternalClientSecretHasher
    {
        public static string HashOf(string secret) => "h:" + new string(secret.Reverse().ToArray());

        public string Hash(string secret) => HashOf(secret);

        public bool Verify(string secret, string storedHash) => storedHash == HashOf(secret);

        public string DummyHash => "dummy";
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
