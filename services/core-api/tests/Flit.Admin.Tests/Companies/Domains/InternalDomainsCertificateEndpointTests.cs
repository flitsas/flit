using Flit.Admin.Application.Companies.Domains.Verification;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Api.Endpoints.Internal;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace Flit.Admin.Tests.Companies.Domains;

/// <summary>
/// HU #12425 AC3 — <see cref="InternalDomainsEndpoints.PutCertificateAsync"/> y
/// <see cref="InternalDomainsEndpoints.GetPendingCertificateDomainsAsync"/> sin levantar el host
/// completo (mismo patrón que <see cref="InternalDomainsEndpointTests"/>). Uso de ejemplo:
/// <code>
/// var result = await InternalDomainsEndpoints.PutCertificateAsync(host, body, configuration, handler, ct);
/// </code>
/// </summary>
public sealed class InternalDomainsCertificateEndpointTests
{
    private const string ConfiguredKey = "clave-interna-de-prueba";
    private static readonly Guid TenantId = Guid.Parse("33333333-3333-4333-8333-333333333333");

    private static IConfiguration NewConfiguration(string? apiKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(apiKey is null
                ? []
                : new Dictionary<string, string?> { ["Internal:ApiKey"] = apiKey })
            .Build();

    private static HttpRequest NewRequest(string? headerValue)
    {
        var context = new DefaultHttpContext();
        if (headerValue is not null)
        {
            context.Request.Headers["X-Internal-Key"] = headerValue;
        }

        return context.Request;
    }

    private static int GetStatus(IResult result) =>
        result switch
        {
            IStatusCodeHttpResult statusCodeResult when statusCodeResult.StatusCode is { } statusCode => statusCode,
            _ => StatusCodes.Status200OK,
        };

    private static TenantDomain NewDomain(string status, DateTimeOffset? verifiedAt = null) => new()
    {
        TenantId = TenantId,
        Host = "app.movilidadandina.com",
        Status = status,
        VerificationToken = "tok-0000000000000001",
        VerifiedAt = verifiedAt,
        StatusChangedAt = DateTimeOffset.UtcNow,
        CheckAttempts = 0,
        RowVersion = 1,
    };

    [Fact]
    public async Task PutCertificate_SinClaveConfigurada_Responde401()
    {
        var handler = new ApplyDomainCertificateHandler(Substitute.For<ITenantDomainRepository>());

        var result = await InternalDomainsEndpoints.PutCertificateAsync(
            "app.movilidadandina.com",
            new DomainCertificateRequestBody(DateTimeOffset.UtcNow, null),
            NewRequest(ConfiguredKey),
            handler,
            NewConfiguration(apiKey: null),
            TestContext.Current.CancellationToken);

        GetStatus(result).Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task PutCertificate_HostDesconocido_Responde404()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByHostAsync("desconocido.example.com", Arg.Any<CancellationToken>()).Returns((TenantDomain?)null);
        var handler = new ApplyDomainCertificateHandler(repo);

        var result = await InternalDomainsEndpoints.PutCertificateAsync(
            "desconocido.example.com",
            new DomainCertificateRequestBody(DateTimeOffset.UtcNow, null),
            NewRequest(ConfiguredKey),
            handler,
            NewConfiguration(ConfiguredKey),
            TestContext.Current.CancellationToken);

        GetStatus(result).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task PutCertificate_DominioPendiente_Responde409DomainNotVerified()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByHostAsync("app.movilidadandina.com", Arg.Any<CancellationToken>()).Returns(NewDomain(TenantDomainStatus.Pending));
        var handler = new ApplyDomainCertificateHandler(repo);

        var result = await InternalDomainsEndpoints.PutCertificateAsync(
            "app.movilidadandina.com",
            new DomainCertificateRequestBody(DateTimeOffset.UtcNow, null),
            NewRequest(ConfiguredKey),
            handler,
            NewConfiguration(ConfiguredKey),
            TestContext.Current.CancellationToken);

        GetStatus(result).Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task PutCertificate_DominioVerificado_PasaAActivoYResponde200()
    {
        var now = DateTimeOffset.UtcNow;
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.GetByHostAsync("app.movilidadandina.com", Arg.Any<CancellationToken>())
            .Returns(NewDomain(TenantDomainStatus.Verified, verifiedAt: now.AddMinutes(-5)));
        repo.ApplyCertificateAsync(
                "app.movilidadandina.com",
                TenantDomainStatus.Active,
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset?>(),
                ApplyDomainCertificateHandler.JobActor,
                Arg.Any<CancellationToken>())
            .Returns(ci => NewDomain(TenantDomainStatus.Active));
        var handler = new ApplyDomainCertificateHandler(repo);

        var result = await InternalDomainsEndpoints.PutCertificateAsync(
            "app.movilidadandina.com",
            new DomainCertificateRequestBody(now, now.AddDays(90)),
            NewRequest(ConfiguredKey),
            handler,
            NewConfiguration(ConfiguredKey),
            TestContext.Current.CancellationToken);

        GetStatus(result).Should().Be(StatusCodes.Status200OK);
        await repo.Received(1).ApplyCertificateAsync(
            "app.movilidadandina.com", TenantDomainStatus.Active, Arg.Any<DateTimeOffset?>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), ApplyDomainCertificateHandler.JobActor, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PendingCertificate_SinClaveConfigurada_Responde401()
    {
        var result = await InternalDomainsEndpoints.GetPendingCertificateDomainsAsync(
            NewRequest(ConfiguredKey), Substitute.For<ITenantDomainRepository>(), NewConfiguration(apiKey: null), TestContext.Current.CancellationToken);

        GetStatus(result).Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task PendingCertificate_SinDominiosPendientes_Responde200ListaVacia()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.ListPendingCertificateHostsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)[]);

        var result = await InternalDomainsEndpoints.GetPendingCertificateDomainsAsync(
            NewRequest(ConfiguredKey), repo, NewConfiguration(ConfiguredKey), TestContext.Current.CancellationToken);

        GetStatus(result).Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task PendingCertificate_ClaveCorrecta_ListaLosHostsDelRepositorio()
    {
        var repo = Substitute.For<ITenantDomainRepository>();
        repo.ListPendingCertificateHostsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)["app.movilidadandina.com"]);

        var result = await InternalDomainsEndpoints.GetPendingCertificateDomainsAsync(
            NewRequest(ConfiguredKey), repo, NewConfiguration(ConfiguredKey), TestContext.Current.CancellationToken);

        GetStatus(result).Should().Be(StatusCodes.Status200OK);
        await repo.Received(1).ListPendingCertificateHostsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }
}
