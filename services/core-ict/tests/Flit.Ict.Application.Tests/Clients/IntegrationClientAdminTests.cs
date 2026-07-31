using Flit.Ict.Application.Clients;
using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Ict.Application.Tests.Clients;

public sealed class IntegrationClientAdminTests
{
    private readonly IIntegrationClientRepository _repository = Substitute.For<IIntegrationClientRepository>();
    private readonly ITenantDirectory _tenants = Substitute.For<ITenantDirectory>();
    private readonly IIctPasswordHasher _hasher = Substitute.For<IIctPasswordHasher>();

    private static readonly Guid TenantId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public IntegrationClientAdminTests()
    {
        _hasher.Hash(Arg.Any<string>()).Returns("hashed");
        _tenants.GetAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantInfo(TenantId, "T1", "Renting Andino", "901698038", IsActive: true));
    }

    private CreateIntegrationClientHandler CreateHandler() => new(_repository, _tenants, _hasher);

    [Fact]
    public async Task Create_generates_secret_hashes_it_and_persists_with_given_tenant()
    {
        _repository.FindByUsernameAsync("gestor1", Arg.Any<CancellationToken>()).Returns((IntegrationClient?)null);

        var (result, error) = await CreateHandler().HandleAsync(
            new CreateClientCommand("gestor1", TenantId, null), Ct);

        error.Should().BeNull();
        result.Should().NotBeNull();
        result!.GeneratedSecret.Should().NotBeNullOrWhiteSpace();
        result.Client.TenantId.Should().Be(TenantId);
        result.Client.Username.Should().Be("gestor1");
        result.Client.IsActive.Should().BeTrue();
        _hasher.Received(1).Hash(result.GeneratedSecret);
        await _repository.Received(1).AddAsync(
            Arg.Is<IntegrationClient>(c => c.Username == "gestor1" && c.TenantId == TenantId && c.PasswordHash == "hashed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_rejects_short_username()
    {
        var (result, error) = await CreateHandler().HandleAsync(new CreateClientCommand("ab", TenantId, null), Ct);

        result.Should().BeNull();
        error.Should().Be("invalid_username");
        await _repository.DidNotReceive().AddAsync(Arg.Any<IntegrationClient>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_rejects_unknown_tenant()
    {
        var unknown = Guid.NewGuid();
        _tenants.GetAsync(unknown, Arg.Any<CancellationToken>()).Returns((TenantInfo?)null);

        var (result, error) = await CreateHandler().HandleAsync(new CreateClientCommand("gestor1", unknown, null), Ct);

        result.Should().BeNull();
        error.Should().Be("tenant_not_found");
    }

    [Fact]
    public async Task Create_rejects_inactive_tenant()
    {
        var inactive = Guid.NewGuid();
        _tenants.GetAsync(inactive, Arg.Any<CancellationToken>())
            .Returns(new TenantInfo(inactive, "T2", "Inactiva", "900000000", IsActive: false));

        var (result, error) = await CreateHandler().HandleAsync(new CreateClientCommand("gestor1", inactive, null), Ct);

        result.Should().BeNull();
        error.Should().Be("tenant_inactive");
    }

    [Fact]
    public async Task Create_rejects_taken_username()
    {
        _repository.FindByUsernameAsync("gestor1", Arg.Any<CancellationToken>())
            .Returns(new IntegrationClient { Username = "gestor1" });

        var (result, error) = await CreateHandler().HandleAsync(new CreateClientCommand("gestor1", TenantId, null), Ct);

        result.Should().BeNull();
        error.Should().Be("username_taken");
    }

    [Fact]
    public async Task Reset_secret_returns_not_found_when_missing()
    {
        _repository.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((IntegrationClient?)null);

        var handler = new ResetIntegrationClientSecretHandler(_repository, _tenants, _hasher);
        var (result, error) = await handler.HandleAsync(Guid.NewGuid(), null, Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
    }

    [Fact]
    public async Task Reset_secret_regenerates_and_clears_lock()
    {
        var client = new IntegrationClient
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Username = "gestor1",
            PasswordHash = "old",
            FailedLoginAttempts = 3,
            LockedUntil = DateTime.UtcNow.AddMinutes(10),
        };
        _repository.FindByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);

        var handler = new ResetIntegrationClientSecretHandler(_repository, _tenants, _hasher);
        var (result, error) = await handler.HandleAsync(client.Id, null, Ct);

        error.Should().BeNull();
        result!.GeneratedSecret.Should().NotBeNullOrWhiteSpace();
        client.PasswordHash.Should().Be("hashed");
        client.PreviousPasswordHash.Should().Be("old");
        client.FailedLoginAttempts.Should().Be(0);
        client.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task Update_enforces_tenant_scope_for_non_super_admin()
    {
        // Un cliente de OTRO tenant se trata como inexistente cuando el llamador está restringido a su compañía.
        var client = new IntegrationClient { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Username = "ajeno" };
        _repository.FindByIdAsync(client.Id, Arg.Any<CancellationToken>()).Returns(client);

        var handler = new UpdateIntegrationClientHandler(_repository, _tenants);
        var (result, error) = await handler.HandleAsync(
            new UpdateClientCommand(client.Id, IsActive: false, MustRotate: null, Scopes: null),
            restrictToTenant: TenantId, Ct);

        result.Should().BeNull();
        error.Should().Be("not_found");
        await _repository.DidNotReceive().SaveAsync(Arg.Any<IntegrationClient>(), Arg.Any<CancellationToken>());
    }
}
