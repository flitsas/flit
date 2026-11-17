using System.Security.Claims;
using Flit.Admin.Application.Companies.MandateSigners.AssociableCompanies;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;
using Flit.Admin.Application.OtClientScopes;
using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>
/// El actor con alcance <c>especificos</c> no puede ampliar (PostgreSQL real): ni al fijar el alcance de otro usuario, ni en
/// la invitación (el invitado hereda), ni al vincular compañías a un mandatario; las asociaciones que no ve se conservan.
/// <para>Uso de ejemplo: el usuario del OT restringido a C1 pide <c>todos</c> para otro usuario y recibe
/// <see cref="SetOtUserClientScopeStatus.ExceedsActor"/>; un Admin OT sin alcance puede darle <c>especificos</c> [C1, C2].</para>
/// </summary>
public sealed class OtAlcanceClientesActorIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Actor = Guid.Parse("11111111-2222-4333-8444-000000000001");
    private static readonly Guid Target = Guid.Parse("11111111-2222-4333-8444-000000000002");
    private static readonly Guid UnrestrictedActor = Guid.Parse("11111111-2222-4333-8444-000000000003");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeScope(IReadOnlyList<Guid>? restriction) : IOtUserClientScopeResolver
    {
        public Task<IReadOnlyList<Guid>?> GetRestrictionForUserAsync(Guid userId, Guid transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(restriction);

        public Task<IReadOnlyList<Guid>?> GetRestrictionAsync(Guid transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(restriction);
    }

    /// <summary>Tres usuarios del OT (tenant O): el actor (restringido a C1), el destinatario y un Admin sin alcance.</summary>
    private async Task SeedOtUsersAsync()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using (var ctx = NewContext())
        {
            foreach (var id in new[] { Actor, Target, UnrestrictedActor })
            {
                ctx.Users.Add(new User
                {
                    Id = id, Email = $"{id}@ot.it.test", DisplayName = "Usuario OT", Status = "active",
                    HomeTenantId = HierarchyScenario.O, CreatedAt = DateTimeOffset.UtcNow,
                });
            }

            // El escenario solo habilita a C1 en Ot1; C2 también debe ser cliente del OT para poder repartirse.
            ctx.TenantTransitOfficeGrants.Add(new TenantTransitOfficeGrant
            {
                Id = Guid.NewGuid(), TenantId = HierarchyScenario.C2, TransitOfficeId = HierarchyScenario.Ot1, IsEnabled = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync(Ct);
        }

        await using var scopeCtx = NewContext();
        await new OtUserClientScopeRepository(scopeCtx).SetAsync(
            HierarchyScenario.O, HierarchyScenario.Ot1, Actor, OtClientScopeModes.Especificos, [HierarchyScenario.C1],
            UnrestrictedActor, Ct);
    }

    private static HttpOtUserClientScopeResolver ResolverOf(FlitDbContext ctx, Guid userId) =>
        new(ctx, new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", userId.ToString()), new Claim(ClaimTypes.Role, "ot_admin")], "test")),
            },
        });

    [PostgresFact]
    public async Task Set_ActorRestringido_NoAmplia_ElSinRestriccionSi()
    {
        await SeedOtUsersAsync();

        await using (var ctx = NewContext())
        {
            var handler = new SetOtUserClientScopeHandler(new OtUserClientScopeRepository(ctx), ResolverOf(ctx, Actor));

            (await handler.HandleAsync(
                new SetOtUserClientScopeCommand(HierarchyScenario.O, null, Target, Actor, OtClientScopeModes.Todos, []), Ct))
                .Should().Be(SetOtUserClientScopeStatus.ExceedsActor);
            (await handler.HandleAsync(
                new SetOtUserClientScopeCommand(
                    HierarchyScenario.O, null, Target, Actor, OtClientScopeModes.Especificos, [HierarchyScenario.C1, HierarchyScenario.C2]), Ct))
                .Should().Be(SetOtUserClientScopeStatus.ExceedsActor);
            (await ctx.OtUserClientScopes.AsNoTracking().CountAsync(s => s.UserId == Target, Ct)).Should().Be(0);

            (await handler.HandleAsync(
                new SetOtUserClientScopeCommand(
                    HierarchyScenario.O, null, Target, Actor, OtClientScopeModes.Especificos, [HierarchyScenario.C1]), Ct))
                .Should().Be(SetOtUserClientScopeStatus.Ok);
        }

        await using (var ctx = NewContext())
        {
            var libre = new SetOtUserClientScopeHandler(new OtUserClientScopeRepository(ctx), ResolverOf(ctx, UnrestrictedActor));
            (await libre.HandleAsync(
                new SetOtUserClientScopeCommand(
                    HierarchyScenario.O, null, Target, UnrestrictedActor, OtClientScopeModes.Especificos,
                    [HierarchyScenario.C1, HierarchyScenario.C2]), Ct))
                .Should().Be(SetOtUserClientScopeStatus.Ok);
        }
    }

    [PostgresFact]
    public async Task CompaniasCliente_ActorRestringidoVeSoloLasSuyas_ElSinRestriccionTodas()
    {
        await SeedOtUsersAsync();
        await using var ctx = NewContext();
        var handler = new ListOtClientCompaniesHandler(new OtUserClientScopeRepository(ctx), ResolverOf(ctx, Actor));

        var restringido = await handler.HandleAsync(HierarchyScenario.O, null, Actor, Ct);
        var libre = await handler.HandleAsync(HierarchyScenario.O, null, UnrestrictedActor, Ct);

        restringido.ActorRestricted.Should().BeTrue();
        restringido.Items.Select(i => i.TenantId).Should().BeEquivalentTo([HierarchyScenario.C1]);
        libre.Items.Select(i => i.TenantId).Should().Contain([HierarchyScenario.C1, HierarchyScenario.C2]);
    }

    [PostgresFact]
    public async Task Activacion_InvitacionSinCompanias_DelActorRestringido_HeredaSuAlcance()
    {
        await SeedOtUsersAsync();
        var invitationId = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.UserInvitations.Add(new Flit.Infrastructure.Persistence.Entities.Security.UserInvitation
            {
                Id = invitationId, TenantId = HierarchyScenario.O, Email = "hereda.alcance@it.local", FullName = "Invitado",
                TokenHash = "hash", InvitedBy = Actor, CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new UserActivationRepository(ctx, [new OtInvitationClientScopeActivationHook(ctx)]);
            await repo.ActivateAsync(
                new ActivationData(invitationId, "hereda.alcance@it.local", "Invitado", "hash", HierarchyScenario.O, [], Actor, DateTimeOffset.UtcNow),
                Ct);
        }

        await using var verify = NewContext();
        var user = await verify.Users.AsNoTracking().SingleAsync(u => u.Email == "hereda.alcance@it.local", Ct);
        var scope = await verify.OtUserClientScopes.AsNoTracking().Include(s => s.Clients).SingleAsync(s => s.UserId == user.Id, Ct);
        scope.Mode.Should().Be(OtUserClientScopeModes.Especificos);
        scope.Clients.Select(c => c.ClientTenantId).Should().BeEquivalentTo([HierarchyScenario.C1]);
    }

    // ── Mandatarios ─────────────────────────────────────────────────────────────────────────────────

    private static MandatarioAssociableCompanies Associable(FlitDbContext ctx) =>
        new(new ManagingCompanyDirectory(ctx), new CompanyHierarchyRepository(ctx));

    private static DbTransitOfficeOperationalStatusReader OtStatus(FlitDbContext ctx) =>
        new DbTransitOfficeOperationalStatusReader(ctx);

    private async Task<Guid> SeedMandatarioAsync()
    {
        await TransitNetworkSeedAsync();
        await using var ctx = NewContext();
        var reader = new DbMandateSignerReader(ctx);
        var result = await new CreateMandateSignerHandler(
                OtStatus(ctx), reader, new MandateSignerRepository(ctx), null, null, Associable(ctx), new FakeScope(null))
            .HandleAsync(
                new CreateMandateSignerCommand
                {
                    TransitOfficeId = HierarchyScenario.Ot1,
                    FullName = "Ana Restrepo",
                    DocumentNumber = "3000000001",
                    CompanyTenantIds = [],
                    Email = "ana@flit.test",
                    SignatureMethod = "biometria",
                    OfficeCompanies = [new MandateSignerOfficeCompanies(HierarchyScenario.Ot1, [HierarchyScenario.C1, HierarchyScenario.C2])],
                    ConfiguredByScope = "organismo",
                    CompanyVisibility = OtCompanyVisibility.WholeNetwork,
                },
                Ct);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors.Select(e => e.Message)));
        return result.MandateSignerId!.Value;
    }

    private async Task TransitNetworkSeedAsync()
    {
        await SeedOtUsersAsync();
        await using var ctx = NewContext();
        await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P, HierarchyScenario.Ot1);
    }

    private static UpdateMandateSignerCommand Edicion(Guid id, params Guid[] asociadas) =>
        new()
        {
            TransitOfficeId = HierarchyScenario.Ot1,
            MandateSignerId = id,
            FullName = "Ana Restrepo",
            DocumentNumber = "3000000001",
            CompanyTenantIds = [],
            Email = "ana@flit.test",
            SignatureMethod = "biometria",
            OfficeCompanies = [new MandateSignerOfficeCompanies(HierarchyScenario.Ot1, asociadas)],
            ConfiguredByScope = "organismo",
            CompanyVisibility = OtCompanyVisibility.WholeNetwork,
        };

    private async Task<List<Guid>> AsociadasActivasAsync()
    {
        await using var ctx = NewContext();
        return await ctx.MandateSignerAssociatedCompanies.AsNoTracking()
            .Where(a => a.IsActive).Select(a => a.AssociatedCompanyTenantId).ToListAsync(Ct);
    }

    [PostgresFact]
    public async Task Mandatario_ActorRestringido_ConservaLasAsociadasQueNoVe_YRechazaAgregarFueraDeSuAlcance()
    {
        var id = await SeedMandatarioAsync();
        (await AsociadasActivasAsync()).Should().BeEquivalentTo([HierarchyScenario.C1, HierarchyScenario.C2]);

        // El reader devuelve las asociadas vigentes por organismo (lo que el update necesita para conservar).
        await using (var ctx = NewContext())
        {
            var item = await new DbMandateSignerReader(ctx).GetByIdAsync(id, Ct);
            item!.OfficeCompanies.SelectMany(o => o.AssociatedCompanyTenantIds)
                .Should().BeEquivalentTo([HierarchyScenario.C1, HierarchyScenario.C2]);
        }

        // Actor restringido a C1: edita enviando solo lo que ve; C2 no se borra por omisión.
        await using (var ctx = NewContext())
        {
            var update = new UpdateMandateSignerHandler(
                OtStatus(ctx), new DbMandateSignerReader(ctx), new MandateSignerRepository(ctx), Associable(ctx), null, null,
                new FakeScope([HierarchyScenario.C1]));

            var result = await update.HandleAsync(Edicion(id, HierarchyScenario.C1), Ct);
            result.Outcome.Should().Be(UpdateMandateSignerOutcome.Updated, string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        (await AsociadasActivasAsync()).Should().BeEquivalentTo([HierarchyScenario.C1, HierarchyScenario.C2]);

        // Quitar lo que sí ve no arrastra lo que no ve.
        await using (var ctx = NewContext())
        {
            var update = new UpdateMandateSignerHandler(
                OtStatus(ctx), new DbMandateSignerReader(ctx), new MandateSignerRepository(ctx), Associable(ctx), null, null,
                new FakeScope([HierarchyScenario.C1]));
            (await update.HandleAsync(Edicion(id), Ct)).Outcome.Should().Be(UpdateMandateSignerOutcome.Updated);
        }

        (await AsociadasActivasAsync()).Should().BeEquivalentTo([HierarchyScenario.C2]);

        // Agregar una fuera de su alcance: 403 y nada cambia.
        await using (var ctx = NewContext())
        {
            var update = new UpdateMandateSignerHandler(
                OtStatus(ctx), new DbMandateSignerReader(ctx), new MandateSignerRepository(ctx), Associable(ctx), null, null,
                new FakeScope([HierarchyScenario.C1]));
            var act = () => update.HandleAsync(Edicion(id, HierarchyScenario.C1, HierarchyScenario.P), Ct);
            await act.Should().ThrowAsync<OtClientScopeExceedsActorException>();
        }

        (await AsociadasActivasAsync()).Should().BeEquivalentTo([HierarchyScenario.C2]);
    }

    [PostgresFact]
    public async Task Mandatario_Alta_ActorRestringido_FueraDeAlcance_Es403_YNoPersiste()
    {
        await TransitNetworkSeedAsync();
        await using var ctx = NewContext();

        var act = () => new CreateMandateSignerHandler(
                OtStatus(ctx), new DbMandateSignerReader(ctx), new MandateSignerRepository(ctx), null, null, Associable(ctx),
                new FakeScope([HierarchyScenario.C1]))
            .HandleAsync(
                new CreateMandateSignerCommand
                {
                    TransitOfficeId = HierarchyScenario.Ot1,
                    FullName = "Beto Gil",
                    DocumentNumber = "3000000002",
                    CompanyTenantIds = [HierarchyScenario.C2],
                    Email = "beto@flit.test",
                    SignatureMethod = "biometria",
                    ConfiguredByScope = "organismo",
                    CompanyVisibility = OtCompanyVisibility.WholeNetwork,
                },
                Ct);

        await act.Should().ThrowAsync<OtClientScopeExceedsActorException>();
        await using var verify = NewContext();
        (await verify.MandateSigners.AsNoTracking().AnyAsync(s => s.DocumentNumber == "3000000002", Ct)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task DirectorioAsociables_ConAlcance_SoloOfreceLasDelActor()
    {
        await SeedOtUsersAsync();
        await using var ctx = NewContext();

        var restringido = await Associable(ctx).ListForOtAsync(null, 1, 100, [HierarchyScenario.C1], Ct);
        var libre = await Associable(ctx).ListForOtAsync(null, 1, 100, null, Ct);

        restringido.Page!.Items.Select(i => i.Id).Should().BeEquivalentTo([HierarchyScenario.C1]);
        libre.Page!.Items.Select(i => i.Id).Should().Contain([HierarchyScenario.C1, HierarchyScenario.C2]);
    }
}
