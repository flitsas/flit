using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Application.Auth.Network;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flit.Integration.Tests.Network;

/// <summary>
/// HU #12422 AC1/AC2/AC3/AC6/AC8 (Feature #12369, ADR-0060 D3) — <see cref="ITenantNetworkMembership"/>
/// (<c>DbTenantNetworkMembership</c>) contra PostgreSQL real, con DOS redes MARCA_BLANCA (A y B, cada
/// una con cabeza e hija), una cabeza CONCESION y una compañía sin jerarquía — mismo patrón de siembra
/// que <see cref="Flit.Integration.Tests.Domains.TenantDomainConstraintsTests"/>.
/// <para>
/// Alcance de esta suite: el componente de PERSISTENCIA nuevo de la HU (pertenencia a la red + dominio
/// activo de la cabeza, sobre <c>identity.tenants</c> y <c>admin.v_active_network_domains</c>). La
/// matriz de decisión de <c>LoginHandler</c> (dominio × sujeto × credencial, incluida la respuesta
/// 401/403 y el claim <c>dom</c>) está cubierta exhaustivamente con dobles en
/// <c>LoginHandlerNetworkScopeTests</c> (unitaria, sin Postgres) — aquí se verifica que el dato real
/// que ese handler consume es el correcto.
/// </para>
/// Uso de ejemplo: <c>await Sut(ctx).ResolveAsync(ChildA) → { HeadTenantId = HeadA, IsMarcaBlancaNetwork
/// = true, ActiveHost = "app.red-a-it.example" }</c>.
/// </summary>
public sealed class NetworkLoginScopeTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid HeadA = new("81111111-1111-4111-8111-111111111111");
    private static readonly Guid ChildA = new("81111111-2222-4222-8222-222222222222");
    private static readonly Guid HeadB = new("82222222-1111-4111-8111-111111111111");
    private static readonly Guid ChildB = new("82222222-2222-4222-8222-222222222222");
    private static readonly Guid ConcesionHead = new("83333333-1111-4111-8111-111111111111");
    private static readonly Guid NoNetworkTenant = new("84444444-1111-4111-8111-111111111111");
    private const string HostA = "app.red-a-it.example";
    private const string HostB = "app.red-b-it.example";

    private static DbTenantNetworkMembership Sut(FlitDbContext ctx) =>
        new(ctx, NullLogger<DbTenantNetworkMembership>.Instance);

    private async Task SeedAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.AddRange(
            TenantSeed.New(HeadA, "IT-NET-A-HEAD", isGroupParent: true, parentId: null, GroupKindCodes.MarcaBlanca),
            TenantSeed.New(HeadB, "IT-NET-B-HEAD", isGroupParent: true, parentId: null, GroupKindCodes.MarcaBlanca),
            TenantSeed.New(ConcesionHead, "IT-NET-CN-HEAD", isGroupParent: true, parentId: null, GroupKindCodes.Concesion),
            TenantSeed.New(NoNetworkTenant, "IT-NET-LONE", isGroupParent: false, parentId: null));
        await ctx.SaveChangesAsync();

        await using var ctx2 = NewContext();
        ctx2.Tenants.AddRange(
            TenantSeed.New(ChildA, "IT-NET-A-CHILD", isGroupParent: false, parentId: HeadA),
            TenantSeed.New(ChildB, "IT-NET-B-CHILD", isGroupParent: false, parentId: HeadB));
        await ctx2.SaveChangesAsync();

        await using var ctx3 = NewContext();
        ctx3.TenantDomains.AddRange(
            ActiveDomain(HeadA, HostA, "tok-red-a-0000000001"),
            ActiveDomain(HeadB, HostB, "tok-red-b-0000000002"));
        await ctx3.SaveChangesAsync();
    }

    private static TenantDomainEntity ActiveDomain(Guid tenantId, string host, string token)
    {
        var now = DateTimeOffset.UtcNow;
        return new TenantDomainEntity
        {
            TenantId = tenantId,
            Host = host,
            VerificationToken = token,
            Status = TenantDomainStatuses.Active,
            VerifiedAt = now,
            ActivatedAt = now,
            CertificateIssuedAt = now,
            CertificateExpiresAt = now.AddDays(90),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    [PostgresFact]
    public async Task AC1_CabezaDeA_ResuelveSuPropiaRedConDominioActivo()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        var result = await Sut(ctx).ResolveAsync(HeadA);

        result.IsMarcaBlancaNetwork.Should().BeTrue();
        result.HeadTenantId.Should().Be(HeadA);
        result.ActiveHost.Should().Be(HostA);
    }

    [PostgresFact]
    public async Task AC1_HijaDeA_ResuelveLaCabezaAYSuDominio()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        var result = await Sut(ctx).ResolveAsync(ChildA);

        result.IsMarcaBlancaNetwork.Should().BeTrue();
        result.HeadTenantId.Should().Be(HeadA);
        result.ActiveHost.Should().Be(HostA);
    }

    // AC8 — "incluido A contra B": la hija de A NUNCA resuelve a la cabeza de B.
    [PostgresFact]
    public async Task AC8_HijaDeA_NuncaResuelveALaCabezaDeB()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        var result = await Sut(ctx).ResolveAsync(ChildA);

        result.HeadTenantId.Should().NotBe(HeadB);
        result.ActiveHost.Should().NotBe(HostB);
    }

    [PostgresFact]
    public async Task AC1_HijaDeB_ResuelveLaCabezaBYSuDominio()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        var result = await Sut(ctx).ResolveAsync(ChildB);

        result.HeadTenantId.Should().Be(HeadB);
        result.ActiveHost.Should().Be(HostB);
    }

    // AC3/AC6 — Concesión NUNCA es una red MARCA_BLANCA, aunque sea cabeza de grupo.
    [PostgresFact]
    public async Task AC6_CabezaConcesion_NoEsRedMarcaBlanca()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        var result = await Sut(ctx).ResolveAsync(ConcesionHead);

        result.IsMarcaBlancaNetwork.Should().BeFalse();
        result.HeadTenantId.Should().BeNull();
        result.ActiveHost.Should().BeNull();
    }

    [PostgresFact]
    public async Task AC3_CompaniaSinRed_NoResuelveRed()
    {
        await SeedAsync();
        await using var ctx = NewContext();

        var result = await Sut(ctx).ResolveAsync(NoNetworkTenant);

        result.IsMarcaBlancaNetwork.Should().BeFalse();
        result.HeadTenantId.Should().BeNull();
    }

    // AC3 — cabeza MARCA_BLANCA SIN dominio activo (aún pending): no hay a dónde redirigir.
    [PostgresFact]
    public async Task AC3_CabezaMarcaBlancaSinDominioActivo_IsMarcaBlancaVerdaderoPeroSinHost()
    {
        await using var ctx0 = NewContext();
        ctx0.Tenants.Add(TenantSeed.New(HeadA, "IT-NET-A-HEAD", isGroupParent: true, parentId: null, GroupKindCodes.MarcaBlanca));
        await ctx0.SaveChangesAsync();
        // Sin sembrar TenantDomains: la cabeza existe pero no tiene dominio (pending o inexistente).

        await using var ctx = NewContext();
        var result = await Sut(ctx).ResolveAsync(HeadA);

        result.IsMarcaBlancaNetwork.Should().BeTrue();
        result.HeadTenantId.Should().Be(HeadA);
        result.ActiveHost.Should().BeNull();
    }

    [PostgresFact]
    public async Task TenantInexistente_ResuelveSinRed()
    {
        await using var ctx = NewContext();

        var result = await Sut(ctx).ResolveAsync(Guid.NewGuid());

        result.IsMarcaBlancaNetwork.Should().BeFalse();
    }
}
