using Flit.Admin.Domain.Companies.Domains;
using Flit.Infrastructure.Domains;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Domains;

/// <summary>
/// HU #12968 (B-08, contrato v1 §5) contra PostgreSQL real: una red puede tener su dominio HUB y un dominio por
/// producto; cada host resuelve su producto, y la unicidad es por (red, propósito).
/// </summary>
public sealed class TenantDomainPurposeTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid HeadId = TenantSeed.ParentId;

    private async Task SeedAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.New(HeadId, "IT-MB-PURPOSE", isGroupParent: true, parentId: null, GroupKindCodes.MarcaBlanca));
        await ctx.SaveChangesAsync();
    }

    private async Task AddActiveDomainAsync(string host, string purpose, string token)
    {
        var now = DateTimeOffset.UtcNow;
        await using var ctx = NewContext();
        ctx.TenantDomains.Add(new TenantDomainEntity
        {
            TenantId = HeadId, Host = host, Purpose = purpose, VerificationToken = token,
            Status = TenantDomainStatuses.Active, VerifiedAt = now, ActivatedAt = now, CertificateIssuedAt = now,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<Flit.Admin.Application.Companies.Domains.NetworkResolution> ResolveAsync(string host)
    {
        await using var ctx = NewContext();
        var resolver = new CachedTenantDomainResolver(
            new TenantDomainRepository(ctx), new MemoryCache(new MemoryCacheOptions()), NullLogger<CachedTenantDomainResolver>.Instance);
        return await resolver.ResolveAsync(host, TestContext.Current.CancellationToken);
    }

    [PostgresFact]
    public async Task DominioHubYDominioDeProducto_ResuelvenCadaUnoSuProducto()
    {
        await SeedAsync();
        await AddActiveDomainAsync("red.example.com", TenantDomainPurposes.Hub, "tok-0000000000000011");
        await AddActiveDomainAsync("comparendos.red.example.com", "comparendos", "tok-0000000000000012");

        var hub = await ResolveAsync("red.example.com");
        var comparendos = await ResolveAsync("comparendos.red.example.com");

        hub.IsNetwork.Should().BeTrue();
        hub.HeadTenantId.Should().Be(HeadId);
        hub.ProductCode.Should().Be("plataforma");
        comparendos.HeadTenantId.Should().Be(HeadId);
        comparendos.ProductCode.Should().Be("comparendos");
    }

    [PostgresFact]
    public async Task DosDominiosVigentesDelMismoProposito_ElMotorLoRechaza()
    {
        await SeedAsync();
        await AddActiveDomainAsync("tramites.red.example.com", "tramites", "tok-0000000000000013");

        var act = () => AddActiveDomainAsync("tramites2.red.example.com", "tramites", "tok-0000000000000014");

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be("uq_tenant_domains_tenant_purpose");
    }

    [PostgresTheory]
    [InlineData("plataforma", "ck_tenant_domains_purpose")]
    [InlineData("flotas", "ck_tenant_domains_purpose_product")]
    public async Task PropositoInvalido_ElMotorLoRechaza(string purpose, string constraint)
    {
        await SeedAsync();

        var act = () => AddActiveDomainAsync("x.red.example.com", purpose, "tok-0000000000000015");

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.ConstraintName.Should().Be(constraint);
    }

    [PostgresFact]
    public async Task ElRepositorioDeLaRedSigueOperandoSobreElDominioHub()
    {
        await SeedAsync();
        await AddActiveDomainAsync("red.example.com", TenantDomainPurposes.Hub, "tok-0000000000000016");
        await AddActiveDomainAsync("tramites.red.example.com", "tramites", "tok-0000000000000017");

        await using var ctx = NewContext();
        var domain = await new TenantDomainRepository(ctx).GetByTenantIdAsync(HeadId, TestContext.Current.CancellationToken);

        domain.Should().NotBeNull();
        domain!.Host.Should().Be("red.example.com");
    }
}
