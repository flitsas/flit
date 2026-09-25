using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Application.Companies.Domains.RegisterDomain;
using Flit.Admin.Domain.Companies.Domains;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Domains;

/// <summary>
/// Uso de ejemplo:
/// var handler = new RegisterDomainHandler(repo, new DomainOptions());
/// var result = await handler.HandleAsync(new RegisterDomainCommand { TenantId = id, Host = "red.example.com" });
/// HU #12416 AC1 (formato/unicidad), AC2 (solo MARCA_BLANCA), AC3 (reservados), AC5 (cambio de host).
/// </summary>
public sealed class RegisterDomainHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Operator = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private static TenantDomain NewDomain(string host, long rowVersion = 0) => new()
    {
        TenantId = TenantId,
        Host = host,
        Status = TenantDomainStatus.Pending,
        VerificationToken = "tok-0000000000000001",
        StatusChangedAt = DateTimeOffset.UtcNow,
        CheckAttempts = 0,
        RowVersion = rowVersion,
    };

    [Fact]
    public async Task AC1_PrimerPut_RegistraElDominioNormalizado()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantDomain?)null);
        repo.RegisterOrReplaceAsync(TenantId, "red.example.com", Arg.Any<string>(), Operator, Arg.Any<CancellationToken>())
            .Returns(ci => NewDomain(ci.ArgAt<string>(1)));

        var handler = new RegisterDomainHandler(repo, new DomainOptions());
        var result = await handler.HandleAsync(new RegisterDomainCommand
        {
            TenantId = TenantId,
            Host = "Red.Example.com",
            ChangedBy = Operator,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RegisterDomainOutcome.Registered);
        result.Domain!.Host.Should().Be("red.example.com");
        await repo.Received(1).RegisterOrReplaceAsync(TenantId, "red.example.com", Arg.Any<string>(), Operator, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("https://red.example.com")]
    [InlineData("red.example.com:443")]
    [InlineData("españa..com")]
    [InlineData("localhost")]
    public async Task AC1_FormatoInvalido_SeRechazaSinLlamarAlRepositorioDeEscritura(string host)
    {
        var repo = Substitute.For<ITenantDomainRepository>();

        var handler = new RegisterDomainHandler(repo, new DomainOptions());
        var result = await handler.HandleAsync(new RegisterDomainCommand
        {
            TenantId = TenantId,
            Host = host,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RegisterDomainOutcome.Invalid);
        result.ErrorCode.Should().Be(DomainErrors.HostInvalid);
        await repo.DidNotReceiveWithAnyArgs().RegisterOrReplaceAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task AC3_HostReservado_SeRechazaConCodigoIdentificable()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        var options = new DomainOptions { Reserved = ["*.flitsas.online"] };

        var handler = new RegisterDomainHandler(repo, options);
        var result = await handler.HandleAsync(new RegisterDomainCommand
        {
            TenantId = TenantId,
            Host = "app.flitsas.online",
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RegisterDomainOutcome.Invalid);
        result.ErrorCode.Should().Be(DomainErrors.HostReserved);
        await repo.DidNotReceiveWithAnyArgs().RegisterOrReplaceAsync(default, default!, default!, default, default);
    }

    /// <summary>HU #12761 — el host de prueba exceptuado atraviesa la validación de reservados y se registra.</summary>
    [Fact]
    public async Task AC1_HostDePruebaPermitido_SeRegistraPeseAEstarEnLaZonaReservada()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantDomain?)null);
        repo.RegisterOrReplaceAsync(TenantId, "marcablancadev.flitsas.online", Arg.Any<string>(), Operator, Arg.Any<CancellationToken>())
            .Returns(ci => NewDomain(ci.ArgAt<string>(1)));

        var options = new DomainOptions
        {
            Reserved = ["*.flitsas.online"],
            Allowed = ["marcablancadev.flitsas.online", "marcablancaqa.flitsas.online", "marcablancapdn.flitsas.online"],
        };

        var handler = new RegisterDomainHandler(repo, options);
        var result = await handler.HandleAsync(new RegisterDomainCommand
        {
            TenantId = TenantId,
            Host = "MarcaBlancaDev.FLITSAS.online",
            ChangedBy = Operator,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RegisterDomainOutcome.Registered);
        result.Domain!.Host.Should().Be("marcablancadev.flitsas.online");
        await repo.Received(1).RegisterOrReplaceAsync(TenantId, "marcablancadev.flitsas.online", Arg.Any<string>(), Operator, Arg.Any<CancellationToken>());
    }

    /// <summary>HU #12761 — la excepción es exacta: un subdominio del host de prueba sigue reservado.</summary>
    [Fact]
    public async Task AC3_SubdominioDelHostPermitido_SeSigueRechazando()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        var options = new DomainOptions
        {
            Reserved = ["*.flitsas.online"],
            Allowed = ["marcablancadev.flitsas.online"],
        };

        var handler = new RegisterDomainHandler(repo, options);
        var result = await handler.HandleAsync(new RegisterDomainCommand
        {
            TenantId = TenantId,
            Host = "sub.marcablancadev.flitsas.online",
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RegisterDomainOutcome.Invalid);
        result.ErrorCode.Should().Be(DomainErrors.HostReserved);
        await repo.DidNotReceiveWithAnyArgs().RegisterOrReplaceAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task AC2_TenantNoMarcaBlanca_RepositorioRechaza_SeTraduce()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantDomain?)null);
        repo.RegisterOrReplaceAsync(TenantId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns<TenantDomain>(_ => throw new DomainTenantNotMarcaBlancaException(TenantId));

        var handler = new RegisterDomainHandler(repo, new DomainOptions());
        var result = await handler.HandleAsync(new RegisterDomainCommand
        {
            TenantId = TenantId,
            Host = "red.example.com",
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RegisterDomainOutcome.TenantNotMarcaBlanca);
    }

    [Fact]
    public async Task AC1_HostYaRegistradoPorOtraRed_RepositorioRechaza_SeTraduce()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns((TenantDomain?)null);
        repo.RegisterOrReplaceAsync(TenantId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns<TenantDomain>(_ => throw new DomainHostAlreadyRegisteredException("red.example.com"));

        var handler = new RegisterDomainHandler(repo, new DomainOptions());
        var result = await handler.HandleAsync(new RegisterDomainCommand
        {
            TenantId = TenantId,
            Host = "red.example.com",
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RegisterDomainOutcome.HostAlreadyRegistered);
    }

    [Fact]
    public async Task AC5_RowVersionDesactualizada_Conflicto_NoLlamaAlRepositorioDeEscritura()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(NewDomain("red.example.com", rowVersion: 5));

        var handler = new RegisterDomainHandler(repo, new DomainOptions());
        var result = await handler.HandleAsync(new RegisterDomainCommand
        {
            TenantId = TenantId,
            Host = "otra.example.com",
            RowVersion = 4,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RegisterDomainOutcome.Conflict);
        result.ErrorCode.Should().Be(DomainErrors.ConcurrencyConflict);
        await repo.DidNotReceiveWithAnyArgs().RegisterOrReplaceAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task AC5_CambioDeHost_GeneraUnTokenDeVerificacionNuevoValido()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByTenantIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(NewDomain("vieja.example.com", rowVersion: 1));
        string? capturedToken = null;
        repo.RegisterOrReplaceAsync(TenantId, "nueva.example.com", Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedToken = ci.ArgAt<string>(2);
                return NewDomain("nueva.example.com", rowVersion: 2);
            });

        var handler = new RegisterDomainHandler(repo, new DomainOptions());
        var result = await handler.HandleAsync(new RegisterDomainCommand
        {
            TenantId = TenantId,
            Host = "nueva.example.com",
            RowVersion = 1,
        }, TestContext.Current.CancellationToken);

        result.Outcome.Should().Be(RegisterDomainOutcome.Registered);
        capturedToken.Should().NotBeNullOrEmpty();
        capturedToken.Should().MatchRegex("^[A-Za-z0-9_-]{16,64}$", "debe cumplir ck_tenant_domains_verification_token (DDL 116)");
    }
}
