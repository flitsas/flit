using Flit.Admin.Application.Integrations.Auth;
using Flit.Admin.Domain.Integrations;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Integrations;

/// <summary>
/// HU #13087 — reglas del canje de credenciales por pase (AC1–AC4), sin base ni HTTP.
/// <code>
/// var result = await handler.HandleAsync(new IssueExternalClientTokenCommand("flito-qa", secreto));
/// // result.Status == ExternalTokenStatus.Issued → result.AccessToken, result.Scopes
/// </code>
/// </summary>
public sealed class IssueExternalClientTokenHandlerTests
{
    private const string Secreto = "secreto-de-prueba";
    private static readonly DateTimeOffset Ahora = new(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);

    private readonly IExternalClientRepository _clients = Substitute.For<IExternalClientRepository>();
    private readonly IExternalClientTokenIssuer _issuer = Substitute.For<IExternalClientTokenIssuer>();
    private readonly ExternalClientAuthSettings _settings = new();

    public IssueExternalClientTokenHandlerTests()
    {
        _issuer.IsAvailable.Returns(true);
        _issuer.Issue(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>())
            .Returns(new ExternalAccessToken("jwt-de-prueba", 1800));
    }

    [Fact]
    public async Task AC1_ConCredencialesCorrectas_EmiteElPaseConSusPermisos()
    {
        var client = Cliente(scopes: [ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead]);
        Registrar(client);

        var result = await Handler().HandleAsync(new("flito-qa", Secreto), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExternalTokenStatus.Issued);
        result.AccessToken.Should().Be("jwt-de-prueba");
        result.ExpiresInSeconds.Should().Be(1800);
        result.Scopes.Should().Equal(ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead);
        await _clients.Received(1).RegisterTokenIssuedAsync(client.Id, Ahora, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC1_SoloLosPermisosDelContratoViajanEnElPase()
    {
        Registrar(Cliente(scopes: [ExternalScopes.TramitesRead, "ict.transactions.write", ExternalScopes.TramitesRead]));

        var result = await Handler().HandleAsync(new("flito-qa", Secreto), TestContext.Current.CancellationToken);

        result.Scopes.Should().Equal(ExternalScopes.TramitesRead);
        _issuer.Received(1).Issue("flito-qa", Arg.Is<IReadOnlyList<string>>(s => s.Count == 1));
    }

    [Fact]
    public async Task AC2_SecretoIncorrecto_EsInvalidClientYCuentaElFallo()
    {
        var client = Cliente();
        Registrar(client);

        var result = await Handler().HandleAsync(new("flito-qa", "otro"), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExternalTokenStatus.InvalidClient);
        await _clients.Received(1).RegisterFailedAttemptAsync(
            client.Id, 5, TimeSpan.FromMinutes(15), Ahora, Arg.Any<CancellationToken>());
        _issuer.DidNotReceiveWithAnyArgs().Issue(default!, default!);
    }

    [Fact]
    public async Task AC2_ClienteInactivo_EsElMismoInvalidClientSinContarFallo()
    {
        Registrar(Cliente(isActive: false));

        var result = await Handler().HandleAsync(new("flito-qa", Secreto), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExternalTokenStatus.InvalidClient);
        await _clients.DidNotReceiveWithAnyArgs().RegisterFailedAttemptAsync(default, default, default, default, default);
    }

    [Theory]
    [InlineData("no-existe")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AC2_ClienteInexistenteOVacio_EsInvalidClient(string? clientId)
    {
        var result = await Handler().HandleAsync(new(clientId, Secreto), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExternalTokenStatus.InvalidClient);
    }

    [Fact]
    public async Task AC3_Bloqueado_Responde423AunqueElSecretoSeaCorrecto()
    {
        Registrar(Cliente(lockedUntil: Ahora.AddMinutes(10)));

        var result = await Handler().HandleAsync(new("flito-qa", Secreto), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExternalTokenStatus.Locked);
        result.RetryAfter.Should().Be(TimeSpan.FromMinutes(10));
        await _clients.DidNotReceiveWithAnyArgs().RegisterFailedAttemptAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task AC3_BloqueoVencido_VuelveAEmitir()
    {
        Registrar(Cliente(lockedUntil: Ahora.AddSeconds(-1)));

        var result = await Handler().HandleAsync(new("flito-qa", Secreto), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExternalTokenStatus.Issued);
    }

    [Fact]
    public async Task AC4_RotacionObligatoria_Responde403ConElSecretoCorrecto()
    {
        Registrar(Cliente(mustRotate: true));

        var result = await Handler().HandleAsync(new("flito-qa", Secreto), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExternalTokenStatus.RotationRequired);
        _issuer.DidNotReceiveWithAnyArgs().Issue(default!, default!);
    }

    [Fact]
    public async Task AC4_RotacionObligatoria_NoSeRevelaSinElSecreto()
    {
        Registrar(Cliente(mustRotate: true));

        var result = await Handler().HandleAsync(new("flito-qa", "otro"), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExternalTokenStatus.InvalidClient);
    }

    [Fact]
    public async Task AC4_TrasRotar_ElSecretoAnteriorValeDentroDeLaVentana()
    {
        Registrar(Cliente(secret: "nuevo", previousSecret: Secreto, rotatedAt: Ahora.AddHours(-23)));

        var result = await Handler().HandleAsync(new("flito-qa", Secreto), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExternalTokenStatus.Issued);
    }

    [Fact]
    public async Task AC4_TrasRotar_ElSecretoAnteriorCaducaAlCerrarLaVentana()
    {
        Registrar(Cliente(secret: "nuevo", previousSecret: Secreto, rotatedAt: Ahora.AddHours(-24)));

        var result = await Handler().HandleAsync(new("flito-qa", Secreto), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExternalTokenStatus.InvalidClient);
    }

    [Fact]
    public async Task SinLlaveDelEmisor_NoTocaLaBaseYEsUnavailable()
    {
        _issuer.IsAvailable.Returns(false);

        var result = await Handler().HandleAsync(new("flito-qa", Secreto), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExternalTokenStatus.Unavailable);
        await _clients.DidNotReceiveWithAnyArgs().GetCredentialsByClientIdAsync(default!, default);
    }

    private IssueExternalClientTokenHandler Handler() =>
        new(_clients, new FakeHasher(), _issuer, _settings, new FixedTime(Ahora));

    private void Registrar(ExternalClientCredentials client) =>
        _clients.GetCredentialsByClientIdAsync(client.ClientId, Arg.Any<CancellationToken>()).Returns(client);

    private static ExternalClientCredentials Cliente(
        string secret = Secreto,
        string? previousSecret = null,
        DateTimeOffset? rotatedAt = null,
        bool mustRotate = false,
        bool isActive = true,
        DateTimeOffset? lockedUntil = null,
        IReadOnlyList<string>? scopes = null) =>
        new(Guid.CreateVersion7(), "flito-qa", FakeHasher.HashOf(secret),
            previousSecret is null ? null : FakeHasher.HashOf(previousSecret), rotatedAt, mustRotate, isActive,
            scopes ?? [ExternalScopes.TramitesRead], 0, lockedUntil);

    private sealed class FakeHasher : IExternalClientSecretHasher
    {
        public static string HashOf(string secret) => "h:" + secret;

        public string Hash(string secret) => HashOf(secret);

        public bool Verify(string secret, string storedHash) => storedHash == HashOf(secret);

        public string DummyHash => "dummy";
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
