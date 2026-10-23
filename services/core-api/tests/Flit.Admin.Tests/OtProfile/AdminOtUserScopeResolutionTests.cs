using System.Security.Claims;
using Flit.Api.Authorization;
using Flit.Api.Endpoints;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Flit.Admin.Tests.OtProfile;

/// <summary>
/// HU #12854 (Feature #12847, Épica #12751) — prueba directa de
/// <c>AdminOtEndpoints.ResolveOtUserScopeAsync</c>, el helper de scoping que ahora comparten
/// Reglas (POST/GET/PATCH), Perfil (<c>PATCH /profile</c>) y Feature Flags
/// (<c>PATCH /feature-flags/{id}</c>) con Requisitos (HU #10546).
///
/// El helper es <c>internal</c> (no <c>private</c>) precisamente para que este test lo invoque
/// DIRECTO vía <c>InternalsVisibleTo Flit.Admin.Tests</c> (Flit.Api.csproj), sin reflexión —
/// mismo patrón documentado en <c>FlitPdfStamper.ComputeStampGeometry</c>.
///
/// Se prueba el helper de forma aislada —no vía HTTP— porque HU #12855 (mismo Feature #12847)
/// restringe ESTOS MISMOS endpoints a SuperAdmin en el mismo cambio: un intento de probar el AC3
/// (ot_admin sigue usando su propio tenant) por HTTP recibiría 403 por autorización antes de
/// llegar al scoping, sin ejercitar lo que esta HU realmente cambia. Los AC1/AC2 sobre SuperAdmin
/// además quedan cubiertos por HTTP end-to-end en
/// <see cref="Flit.Admin.Tests.OtRules.AdminOtRulesScopeTests"/> y
/// <see cref="AdminOtProfileFeatureFlagScopeTests"/> (ahí SuperAdmin pasa ambas HUs: B1 y B2).
/// </summary>
public sealed class AdminOtUserScopeResolutionTests
{
    private readonly Guid _officeId = Guid.NewGuid();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _superAdminTenantId = Guid.NewGuid();

    // Segundo organismo/tenant, ajeno al de _tenantId — usado por el caso IDOR.
    private readonly Guid _foreignOfficeId = Guid.NewGuid();
    private readonly Guid _foreignTenantId = Guid.NewGuid();

    [Fact] // HU12854_AC1 — SuperAdmin con transitOfficeId resuelve el tenant DUEÑO del organismo.
    public async Task HU12854_AC1_ResolveOtUserScopeAsync_AsSuperAdmin_WithTransitOfficeId_ResolvesOwnerTenant()
    {
        await using var db = NewSeededContext();
        var user = BuildPrincipal("SuperAdmin", _superAdminTenantId);

        var (tenantId, error) = await AdminOtEndpoints.ResolveOtUserScopeAsync(
            user, _officeId, db, CancellationToken.None);

        error.Should().BeNull();
        tenantId.Should().Be(_tenantId, "debe resolver el tenant dueño del organismo, no el del SuperAdmin");
    }

    [Fact] // HU12854_AC2 — SuperAdmin sin transitOfficeId → 400, sin resolver ningún tenant.
    public async Task HU12854_AC2_ResolveOtUserScopeAsync_AsSuperAdmin_WithoutTransitOfficeId_Returns400()
    {
        await using var db = NewSeededContext();
        var user = BuildPrincipal("SuperAdmin", _superAdminTenantId);

        var (tenantId, error) = await AdminOtEndpoints.ResolveOtUserScopeAsync(
            user, null, db, CancellationToken.None);

        error.Should().NotBeNull();
        tenantId.Should().Be(Guid.Empty, "sin transitOfficeId no debe resolver el tenant del SuperAdmin ni ningún otro");
        await AssertStatusCodeAsync(error!, StatusCodes.Status400BadRequest);
    }

    [Fact] // HU12854_AC3 — ot_admin sigue usando su propio tenant, sin exigir transitOfficeId.
    public async Task HU12854_AC3_ResolveOtUserScopeAsync_AsOtAdmin_UsesOwnTenantWithoutTransitOfficeId()
    {
        await using var db = NewSeededContext();
        var user = BuildPrincipal("ot_admin", _tenantId);

        var (tenantId, error) = await AdminOtEndpoints.ResolveOtUserScopeAsync(
            user, null, db, CancellationToken.None);

        error.Should().BeNull();
        tenantId.Should().Be(_tenantId, "ot_admin no debe exigir transitOfficeId: usa el tenant de su propio JWT");
    }

    [Fact] // HU12854_AC3 (security) — IDOR: ot_admin manda ?transitOfficeId de OTRO organismo.
    public async Task HU12854_AC3_ResolveOtUserScopeAsync_AsOtAdmin_WithForeignTransitOfficeId_IgnoresItAndUsesOwnTenant()
    {
        await using var db = NewSeededContext();
        var user = BuildPrincipal("ot_admin", _tenantId);

        // ot_admin del tenant _tenantId intenta leer/escribir el organismo ajeno _foreignOfficeId
        // (tenant _foreignTenantId) inyectando el query param. El helper debe IGNORARLO por
        // completo: solo SuperAdmin puede fijar el organismo objetivo por query; ot_admin
        // SIEMPRE usa el tenant de su propio JWT.
        var (tenantId, error) = await AdminOtEndpoints.ResolveOtUserScopeAsync(
            user, _foreignOfficeId, db, CancellationToken.None);

        error.Should().BeNull();
        tenantId.Should().Be(_tenantId, "ot_admin no debe poder escapar de su tenant vía ?transitOfficeId=, ni siquiera al de otro organismo");
        tenantId.Should().NotBe(_foreignTenantId);
    }

    [Fact] // Organismo sin tenant OT vinculado → 404 (no un 500 ni un tenant fantasma).
    public async Task ResolveOtUserScopeAsync_AsSuperAdmin_OfficeWithoutOtTenant_Returns404()
    {
        await using var db = NewSeededContext();
        var orphanOfficeId = Guid.NewGuid();
        var user = BuildPrincipal("SuperAdmin", _superAdminTenantId);

        var (tenantId, error) = await AdminOtEndpoints.ResolveOtUserScopeAsync(
            user, orphanOfficeId, db, CancellationToken.None);

        error.Should().NotBeNull();
        tenantId.Should().Be(Guid.Empty);
        await AssertStatusCodeAsync(error!, StatusCodes.Status404NotFound);
    }

    private static ClaimsPrincipal BuildPrincipal(string role, Guid tenantId) =>
        new(new ClaimsIdentity(
            [
                new Claim("sub", Guid.NewGuid().ToString()),
                new Claim(AdminAuthorization.RoleClaimType, role),
                new Claim(AdminAuthorization.TenantIdClaimType, tenantId.ToString()),
            ],
            "TestAuth",
            nameType: "sub",
            // El JWT real configura RoleClaimType="role" (ApiSecurityExtensions); sin este mapeo,
            // IsInRole/IsSuperAdmin busca ClaimTypes.Role (URI largo) y nunca encuentra el claim.
            roleType: AdminAuthorization.RoleClaimType));

    private FlitDbContext NewSeededContext()
    {
        var ctx = new FlitDbContext(new DbContextOptionsBuilder<FlitDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            TransitOfficeId = _officeId,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.TransitOfficeProfiles.Add(new TransitOfficeProfile
        {
            Id = Guid.NewGuid(),
            TenantId = _foreignTenantId,
            TransitOfficeId = _foreignOfficeId,
            OperationMode = "dashboard",
            QuipuxReadOnly = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        ctx.SaveChanges();
        return ctx;
    }

    private static async Task AssertStatusCodeAsync(IResult result, int expectedStatusCode)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };

        await result.ExecuteAsync(httpContext);

        httpContext.Response.StatusCode.Should().Be(expectedStatusCode);
    }
}
