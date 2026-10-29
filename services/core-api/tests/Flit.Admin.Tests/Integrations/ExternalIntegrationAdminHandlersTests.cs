using Flit.Admin.Application.Integrations.Auth;
using Flit.Admin.Application.Integrations.Clients;
using Flit.Admin.Domain.Integrations;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Integrations;

/// <summary>
/// HU #14067 (Feature H, Épica #14021) — reglas de la integración externa «Persona Natural» ligada a la compañía
/// (AC1 a AC4 y AC6), sin base ni HTTP.
/// <code>
/// var (result, error) = await create.HandleAsync(new(companyId, "persona_natural", null, null, actor));
/// // result.ClientSecret se muestra una vez; result.Client.Scopes == ["platform.tramites.persona_natural"]
/// </code>
/// </summary>
public sealed class ExternalIntegrationAdminHandlersTests
{
    private static readonly Guid Compania = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Actor = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset Ahora = new(2026, 10, 28, 15, 0, 0, TimeSpan.Zero);

    private readonly IExternalClientRepository _clients = Substitute.For<IExternalClientRepository>();
    private readonly FakeHasher _hasher = new();

    public ExternalIntegrationAdminHandlersTests() =>
        _clients.CompanyExistsAsync(Compania, Arg.Any<CancellationToken>()).Returns(true);

    [Fact]
    public async Task AC1_ElAltaLigaLaIntegracionALaCompaniaYDevuelveElSecretoUnaVez()
    {
        NewExternalClient? guardado = null;
        _clients.CreateAsync(Arg.Do<NewExternalClient>(c => guardado = c), Arg.Any<CancellationToken>())
            .Returns(ci => Vista(ci.Arg<NewExternalClient>()));

        var (result, error) = await new CreateExternalIntegrationHandler(_clients, _hasher).HandleAsync(
            new(Compania, "persona_natural", null, null, Actor), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.ClientSecret.Should().MatchRegex("^[A-Za-z0-9_-]{43}$");
        result.Client.ClientId.Should().MatchRegex("^pn-[0-9a-f]{16}$");
        guardado!.CompanyId.Should().Be(Compania);
        guardado.Tipo.Should().Be("persona_natural");
        guardado.CreatedBy.Should().Be(Actor);
        guardado.SecretHash.Should().Be(FakeHasher.HashOf(result.ClientSecret)).And.NotContain(result.ClientSecret[..10]);
    }

    [Fact]
    public async Task AC6_ElAlcanceEsFijoYSoloCubreLaFachadaDePersonaNatural()
    {
        NewExternalClient? guardado = null;
        _clients.CreateAsync(Arg.Do<NewExternalClient>(c => guardado = c), Arg.Any<CancellationToken>())
            .Returns(ci => Vista(ci.Arg<NewExternalClient>()));

        await new CreateExternalIntegrationHandler(_clients, _hasher).HandleAsync(
            new(Compania, "persona_natural", null, null, Actor), TestContext.Current.CancellationToken);

        guardado!.Scopes.Should().Equal("platform.tramites.persona_natural");
        guardado.Scopes.Should().NotIntersectWith(ExternalScopes.Todos, "no cubre los endpoints externos de lectura de trámites");
    }

    [Theory]
    [InlineData("externa")]
    [InlineData("otro")]
    [InlineData(null)]
    public async Task AC1_SoloSePuedeCrearElTipoPersonaNatural(string? tipo)
    {
        var (_, error) = await new CreateExternalIntegrationHandler(_clients, _hasher).HandleAsync(
            new(Compania, tipo, null, null, Actor), TestContext.Current.CancellationToken);

        error.Should().Be(ExternalIntegrationAdminErrors.InvalidType);
        await _clients.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task AC1_CompaniaInexistente_EsCompanyNotFoundSinTocarLaBase()
    {
        var (_, error) = await new CreateExternalIntegrationHandler(_clients, _hasher).HandleAsync(
            new(Guid.NewGuid(), "persona_natural", null, null, Actor), TestContext.Current.CancellationToken);

        error.Should().Be(ExternalIntegrationAdminErrors.CompanyNotFound);
        await _clients.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Fact]
    public async Task AC2_YaHayUnaActivaDelMismoTipo_Es409IntegracionYaActiva()
    {
        _clients.CreateAsync(Arg.Any<NewExternalClient>(), Arg.Any<CancellationToken>())
            .Returns<ExternalClientView>(_ => throw new ExternalIntegrationAlreadyActiveException(Compania, "persona_natural"));

        var (result, error) = await new CreateExternalIntegrationHandler(_clients, _hasher).HandleAsync(
            new(Compania, "persona_natural", null, null, Actor), TestContext.Current.CancellationToken);

        result.Should().BeNull();
        error.Should().Be("integracion_ya_activa");
    }

    [Fact]
    public async Task AC1_UnaColisionDeIdentificadorSeReintentaConOtro()
    {
        var intentos = 0;
        _clients.CreateAsync(Arg.Any<NewExternalClient>(), Arg.Any<CancellationToken>())
            .Returns(ci => ++intentos == 1
                ? throw new ExternalClientAlreadyExistsException(ci.Arg<NewExternalClient>().ClientId)
                : Task.FromResult(Vista(ci.Arg<NewExternalClient>())));

        var (result, error) = await new CreateExternalIntegrationHandler(_clients, _hasher).HandleAsync(
            new(Compania, "persona_natural", null, null, Actor), TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result.Should().NotBeNull();
        intentos.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AC3_RotarPasaElModoDeRevocacionYDevuelveElSecretoNuevo(bool revocar)
    {
        var vista = Vista();
        _clients.GetByCompanyAsync(Compania, vista.Id, Arg.Any<CancellationToken>()).Returns(vista);
        string? hash = null;
        _clients.ReplaceSecretAsync(default, default!, default, default, default, default)
            .ReturnsForAnyArgs(ci => { hash = ci.ArgAt<string>(1); return vista; });

        var (result, error) = await new RotateExternalIntegrationSecretHandler(_clients, _hasher, new FixedTime(Ahora))
            .HandleAsync(Compania, vista.Id, revocar, Actor, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        hash.Should().Be(FakeHasher.HashOf(result!.ClientSecret));
        await _clients.Received(1).ReplaceSecretAsync(vista.Id, Arg.Any<string>(), revocar, Actor, Ahora, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC3_NoSeRotaUnaIntegracionAjenaInactivaOBloqueada()
    {
        var ajena = Vista();
        var inactiva = Vista() with { IsActive = false };
        var bloqueada = Vista() with { LockedUntil = ExternalIntegrationRules.IndefiniteBlock };
        _clients.GetByCompanyAsync(Compania, inactiva.Id, Arg.Any<CancellationToken>()).Returns(inactiva);
        _clients.GetByCompanyAsync(Compania, bloqueada.Id, Arg.Any<CancellationToken>()).Returns(bloqueada);
        var handler = new RotateExternalIntegrationSecretHandler(_clients, _hasher, new FixedTime(Ahora));

        (await handler.HandleAsync(Compania, ajena.Id, false, Actor, TestContext.Current.CancellationToken)).Error
            .Should().Be(ExternalIntegrationAdminErrors.NotFound, "de otra compañía o inexistente");
        (await handler.HandleAsync(Compania, inactiva.Id, false, Actor, TestContext.Current.CancellationToken)).Error
            .Should().Be("integracion_inactiva");
        (await handler.HandleAsync(Compania, bloqueada.Id, false, Actor, TestContext.Current.CancellationToken)).Error
            .Should().Be("integracion_bloqueada");
        await _clients.DidNotReceiveWithAnyArgs().ReplaceSecretAsync(default, default!, default, default, default, default);
    }

    [Fact]
    public async Task AC4_BloquearSellaElBloqueoIndefinidoYElAutor()
    {
        var vista = Vista();
        _clients.GetByCompanyAsync(Compania, vista.Id, Arg.Any<CancellationToken>()).Returns(vista);
        _clients.BlockAsync(vista.Id, Actor, Ahora, Arg.Any<CancellationToken>())
            .Returns(vista with { LockedUntil = ExternalIntegrationRules.IndefiniteBlock });

        var (result, error) = await new ChangeExternalIntegrationStateHandler(_clients, new FixedTime(Ahora))
            .HandleAsync(Compania, vista.Id, ExternalIntegrationAction.Block, Actor, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        ExternalIntegrationRules.EstaBloqueadaIndefinidamente(result!).Should().BeTrue();
    }

    [Fact]
    public async Task AC4_DesactivarPoneIsActiveEnFalsoConElAutorYEsIdempotente()
    {
        var vista = Vista();
        _clients.GetByCompanyAsync(Compania, vista.Id, Arg.Any<CancellationToken>()).Returns(vista);
        _clients.UpdateAsync(vista.Id, Arg.Any<ExternalClientChanges>(), Actor, Ahora, Arg.Any<CancellationToken>())
            .Returns(vista with { IsActive = false });
        var handler = new ChangeExternalIntegrationStateHandler(_clients, new FixedTime(Ahora));

        var (result, error) = await handler.HandleAsync(
            Compania, vista.Id, ExternalIntegrationAction.Deactivate, Actor, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        result!.IsActive.Should().BeFalse();
        await _clients.Received(1).UpdateAsync(
            vista.Id, Arg.Is<ExternalClientChanges>(c => c.IsActive == false && c.Scopes == null), Actor, Ahora, Arg.Any<CancellationToken>());

        // Ya desactivada: no vuelve a escribir.
        _clients.GetByCompanyAsync(Compania, vista.Id, Arg.Any<CancellationToken>()).Returns(vista with { IsActive = false });
        (await handler.HandleAsync(Compania, vista.Id, ExternalIntegrationAction.Deactivate, Actor, TestContext.Current.CancellationToken))
            .Error.Should().BeNull();
        await _clients.Received(1).UpdateAsync(
            Arg.Any<Guid>(), Arg.Any<ExternalClientChanges>(), Arg.Any<Guid?>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AC4_SinIntegracionEnLaCompania_Es404YNoToca()
    {
        var (_, error) = await new ChangeExternalIntegrationStateHandler(_clients, new FixedTime(Ahora))
            .HandleAsync(Compania, Guid.NewGuid(), ExternalIntegrationAction.Block, Actor, TestContext.Current.CancellationToken);

        error.Should().Be(ExternalIntegrationAdminErrors.NotFound);
        await _clients.DidNotReceiveWithAnyArgs().BlockAsync(default, default, default, default);
    }

    [Fact]
    public async Task AC6_ElPaseDeUnaIntegracionPersonaNaturalSoloLlevaElPermisoDeLaFachada()
    {
        var emisor = Substitute.For<IExternalClientTokenIssuer>();
        emisor.IsAvailable.Returns(true);
        emisor.Issue(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>()).Returns(new ExternalAccessToken("jwt", 1800));
        // Aunque la fila trajera permisos de lectura externa, el tipo los filtra.
        var cliente = new ExternalClientCredentials(
            Guid.CreateVersion7(), "pn-0123456789abcdef", FakeHasher.HashOf("s"), null, null, false, true,
            ["platform.tramites.persona_natural", ExternalScopes.TramitesRead, ExternalScopes.TramitesPiiRead], 0, null, "persona_natural");
        _clients.GetCredentialsByClientIdAsync("pn-0123456789abcdef", Arg.Any<CancellationToken>()).Returns(cliente);

        var result = await new IssueExternalClientTokenHandler(_clients, _hasher, emisor, new ExternalClientAuthSettings(), new FixedTime(Ahora))
            .HandleAsync(new("pn-0123456789abcdef", "s"), TestContext.Current.CancellationToken);

        result.Status.Should().Be(ExternalTokenStatus.Issued);
        result.Scopes.Should().Equal("platform.tramites.persona_natural");
    }

    [Fact]
    public async Task AC4_UnaIntegracionBloqueadaODesactivadaNoObtienePase()
    {
        var emisor = Substitute.For<IExternalClientTokenIssuer>();
        emisor.IsAvailable.Returns(true);
        var bloqueada = new ExternalClientCredentials(
            Guid.CreateVersion7(), "pn-0123456789abcdef", FakeHasher.HashOf("s"), null, null, false, true,
            ["platform.tramites.persona_natural"], 0, ExternalIntegrationRules.IndefiniteBlock, "persona_natural");
        var inactiva = bloqueada with { LockedUntil = null, IsActive = false };
        var handler = new IssueExternalClientTokenHandler(_clients, _hasher, emisor, new ExternalClientAuthSettings(), new FixedTime(Ahora));

        _clients.GetCredentialsByClientIdAsync("pn-0123456789abcdef", Arg.Any<CancellationToken>()).Returns(bloqueada);
        (await handler.HandleAsync(new("pn-0123456789abcdef", "s"), TestContext.Current.CancellationToken)).Status
            .Should().Be(ExternalTokenStatus.Locked);

        _clients.GetCredentialsByClientIdAsync("pn-0123456789abcdef", Arg.Any<CancellationToken>()).Returns(inactiva);
        (await handler.HandleAsync(new("pn-0123456789abcdef", "s"), TestContext.Current.CancellationToken)).Status
            .Should().Be(ExternalTokenStatus.InvalidClient);
        emisor.DidNotReceiveWithAnyArgs().Issue(default!, default!);
    }

    [Fact]
    public async Task LaRutaGenericaNoAdministraLasIntegracionesDeCompania()
    {
        var pn = Vista();
        _clients.GetByIdAsync(pn.Id, Arg.Any<CancellationToken>()).Returns(pn);

        (await new GetExternalClientHandler(_clients).HandleAsync(pn.Id, TestContext.Current.CancellationToken)).Should().BeNull();
        (await new UpdateExternalClientHandler(_clients, new FixedTime(Ahora))
            .HandleAsync(new(pn.Id, null, null, [ExternalScopes.TramitesRead], null, null, Actor), TestContext.Current.CancellationToken)).Error
            .Should().Be(ExternalClientAdminErrors.NotFound);
        (await new RegenerateExternalClientSecretHandler(_clients, _hasher, new FixedTime(Ahora))
            .HandleAsync(pn.Id, false, Actor, TestContext.Current.CancellationToken)).Error.Should().Be(ExternalClientAdminErrors.NotFound);
        (await new UnlockExternalClientHandler(_clients, new FixedTime(Ahora))
            .HandleAsync(pn.Id, Actor, TestContext.Current.CancellationToken)).Error.Should().Be(ExternalClientAdminErrors.NotFound);
        await _clients.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default, default, default);
        await _clients.DidNotReceiveWithAnyArgs().ReplaceSecretAsync(default, default!, default, default, default, default);
        await _clients.DidNotReceiveWithAnyArgs().UnlockAsync(default, default, default, default);
    }

    private static ExternalClientView Vista(NewExternalClient? nuevo = null) =>
        new(Guid.CreateVersion7(), nuevo?.ClientId ?? "pn-0123456789abcdef", "Portal Persona Natural", "Finalidad",
            nuevo?.Scopes ?? ["platform.tramites.persona_natural"], true, false, null, null, Ahora, "persona_natural", Compania);

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
