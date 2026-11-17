using Flit.Tramites.Domain.Tramites.Estados;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.OtClientProcedures;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.TransitOffices;

/// <summary>
/// Alcance de compañías cliente por usuario del OT contra PostgreSQL real (DDL 197 y 198): la migración se descubre y
/// aplica, los DDL son idempotentes, el filtro del alcance se traduce a SQL en la bandeja, y la activación de una cuenta
/// invitada con alcance crea el usuario y su alcance en la misma transacción (con la FK a <c>identity.users</c>).
/// </summary>
public sealed class OtUsuarioAlcanceClientesIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string MigrationScopes = "20261117120000_OtUsuarioAlcanceClientes";
    private const string MigrationInvitation = "20261117130000_OtInvitacionAlcanceClientes";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeScope(IReadOnlyList<Guid>? restriction) : IOtUserClientScopeResolver
    {
        public Task<IReadOnlyList<Guid>?> GetRestrictionForUserAsync(Guid userId, Guid transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(restriction);

        public Task<IReadOnlyList<Guid>?> GetRestrictionAsync(Guid transitOfficeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(restriction);
    }

    private static async Task<long> CountAsync(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(Ct));
    }

    [PostgresFact]
    public async Task Migraciones_SeDescubrenYEstanAplicadas_ConSusTablas()
    {
        await using var ctx = NewContext();
        var applied = await ctx.Database.GetAppliedMigrationsAsync(Ct);
        ctx.Database.GetMigrations().Should().Contain([MigrationScopes, MigrationInvitation]);
        applied.Should().Contain([MigrationScopes, MigrationInvitation]);

        await using var cn = await Fixture.OpenConnectionAsync();
        (await CountAsync(cn, """
            SELECT count(*) FROM information_schema.tables
             WHERE table_schema = 'admin'
               AND table_name IN ('ot_user_client_scopes', 'ot_user_client_scope_clients', 'ot_invitation_client_scope_clients')
            """)).Should().Be(3);
    }

    [PostgresFact]
    public async Task Ddl_SonIdempotentes()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        foreach (var file in new[] { "197-ot-usuario-alcance-clientes.sql", "198-ot-invitacion-alcance-clientes.sql" })
        {
            await using var cmd = new NpgsqlCommand(EmbeddedDdl.LoadUp(file), cn);
            await cmd.ExecuteNonQueryAsync(Ct);
        }
    }

    [PostgresFact]
    public async Task Bandeja_ConAlcanceEspecificos_FiltraEnSqlListaContadoresYDetalle()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var c1Proc = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C1);
        var c2Proc = HierarchyScenario.DeliveredProcedureOf(HierarchyScenario.C2);
        var filter = new OtClientProcedureFilter { Page = 1, PageSize = 100 };

        await using (var ctx = NewContext())
        {
            var todos = new OtClientProcedureRepository(ctx, new NullTramiteTransitionPublisher(), clientScope: new FakeScope(null));
            (await todos.ListAsync(HierarchyScenario.O, filter, cancellationToken: Ct)).Items
                .Select(i => i.ClientTenantId).Should().BeEquivalentTo(HierarchyScenario.Clients);
        }

        await using var restringido = NewContext();
        var repo = new OtClientProcedureRepository(
            restringido, new NullTramiteTransitionPublisher(), clientScope: new FakeScope([HierarchyScenario.C1]));

        (await repo.ListAsync(HierarchyScenario.O, filter, cancellationToken: Ct)).Items
            .Select(i => i.ClientTenantId).Should().BeEquivalentTo([HierarchyScenario.C1]);
        (await repo.GetBandejaCountersAsync(HierarchyScenario.O, null, cancellationToken: Ct))!.PorDecidir.Should().Be(1);
        (await repo.GetByIdAsync(HierarchyScenario.O, c1Proc, Ct)).Should().NotBeNull();
        (await repo.GetByIdAsync(HierarchyScenario.O, c2Proc, Ct)).Should().BeNull("compañía no asignada = 404");
        (await repo.ListAccessibleRefsAsync(HierarchyScenario.O, null, [c1Proc, c2Proc], cancellationToken: Ct))
            .Select(r => r.Id).Should().BeEquivalentTo([c1Proc]);
    }

    [PostgresFact]
    public async Task Activacion_ConAlcanceEnLaInvitacion_CreaUsuarioYAlcanceEnLaMismaTransaccion()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var invitationId = Guid.NewGuid();

        await using (var seed = NewContext())
        {
            seed.UserInvitations.Add(new UserInvitation
            {
                Id = invitationId,
                TenantId = HierarchyScenario.O,
                Email = "invitado.alcance@it.local",
                FullName = "Invitado",
                TokenHash = "hash",
                InvitedBy = HierarchyScenario.UserOf(HierarchyScenario.C1),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(Ct);
            seed.OtInvitationClientScopeClients.Add(new OtInvitationClientScopeClientEntity
            {
                InvitationId = invitationId, ClientTenantId = HierarchyScenario.C1, CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(Ct);
        }

        await using (var ctx = NewContext())
        {
            var repo = new Flit.Infrastructure.Persistence.Repositories.UserActivationRepository(
                ctx, [new OtInvitationClientScopeActivationHook(ctx)]);
            await repo.ActivateAsync(
                new ActivationData(
                    invitationId, "invitado.alcance@it.local", "Invitado", "hash", HierarchyScenario.O, [],
                    HierarchyScenario.UserOf(HierarchyScenario.C1), DateTimeOffset.UtcNow),
                Ct);
        }

        await using var verify = NewContext();
        var user = await verify.Users.AsNoTracking().SingleAsync(u => u.Email == "invitado.alcance@it.local", Ct);
        var scope = await verify.OtUserClientScopes.AsNoTracking().Include(s => s.Clients).SingleAsync(s => s.UserId == user.Id, Ct);
        scope.Mode.Should().Be(OtUserClientScopeModes.Especificos);
        scope.TenantId.Should().Be(HierarchyScenario.O);
        scope.Clients.Select(c => c.ClientTenantId).Should().BeEquivalentTo([HierarchyScenario.C1]);

        // Borrar la invitación arrastra sus compañías pendientes (ON DELETE CASCADE).
        await using var cn = await Fixture.OpenConnectionAsync();
        await using (var del = new NpgsqlCommand($"DELETE FROM security.user_invitations WHERE id = '{invitationId}'", cn))
        {
            await del.ExecuteNonQueryAsync(Ct);
        }

        (await CountAsync(cn, $"SELECT count(*) FROM admin.ot_invitation_client_scope_clients WHERE invitation_id = '{invitationId}'"))
            .Should().Be(0);
    }

    [PostgresFact]
    public async Task Mantenimiento_GuardaReemplazaYBorraElAlcance_ConAuditoriaDePermisos()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var target = HierarchyScenario.UserOf(HierarchyScenario.C1);
        var actor = HierarchyScenario.UserOf(HierarchyScenario.C2);

        await using (var ctx = NewContext())
        {
            await new OtUserClientScopeRepository(ctx).SetAsync(
                HierarchyScenario.O, HierarchyScenario.Ot1, target, OtClientScopeModes.Especificos,
                [HierarchyScenario.C1, HierarchyScenario.C2], actor, Ct);
        }

        await using (var ctx = NewContext())
        {
            await new OtUserClientScopeRepository(ctx).SetAsync(
                HierarchyScenario.O, HierarchyScenario.Ot1, target, OtClientScopeModes.Especificos,
                [HierarchyScenario.C2], actor, Ct);
        }

        await using (var ctx = NewContext())
        {
            var clients = await ctx.OtUserClientScopeClients.Select(c => c.ClientTenantId).ToListAsync(Ct);
            clients.Should().BeEquivalentTo([HierarchyScenario.C2]);
            (await new OtUserClientScopeRepository(ctx).GetSummariesAsync(HierarchyScenario.O, Ct))[target].ClientCount.Should().Be(1);

            await new OtUserClientScopeRepository(ctx).SetAsync(
                HierarchyScenario.O, HierarchyScenario.Ot1, target, OtClientScopeModes.Todos, [], actor, Ct);
        }

        await using var verify = NewContext();
        (await verify.OtUserClientScopes.CountAsync(Ct)).Should().Be(0);
        (await verify.OtUserClientScopeClients.CountAsync(Ct)).Should().Be(0);
        var reasons = await verify.UserPermissionAudits.Where(a => a.Action == OtUserClientScopeRepository.AuditAction)
            .OrderBy(a => a.CreatedAt).Select(a => a.ReasonCode).ToListAsync(Ct);
        reasons.Should().Equal("todos>especificos:2", "especificos>especificos:1", "especificos>todos:0");
    }

    [PostgresFact]
    public async Task Invitacion_SiGuardarElAlcanceFalla_LaTransaccionRevierteLaInvitacion_FallaCerrada()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var invitationId = Guid.NewGuid();

        await using (var ctx = NewContext())
        {
            var tx = await ctx.Database.BeginTransactionAsync(Ct);
            await using (tx)
            {
                ctx.UserInvitations.Add(new UserInvitation
                {
                    Id = invitationId, TenantId = HierarchyScenario.O, Email = "falla.alcance@it.local", FullName = "F",
                    TokenHash = "h", InvitedBy = HierarchyScenario.UserOf(HierarchyScenario.C1), CreatedAt = DateTimeOffset.UtcNow,
                });
                await ctx.SaveChangesAsync(Ct);

                // Compañía inexistente: viola la FK a identity.tenants y el SaveChanges lanza.
                await FluentActions
                    .Awaiting(() => new OtUserClientScopeRepository(ctx).SetInvitationScopeAsync(
                        invitationId, [Guid.NewGuid()], HierarchyScenario.UserOf(HierarchyScenario.C1), Ct))
                    .Should().ThrowAsync<DbUpdateException>();
                // Sin Commit: al salir se revierte todo.
            }
        }

        await using var verify = NewContext();
        (await verify.UserInvitations.AnyAsync(i => i.Id == invitationId, Ct)).Should().BeFalse("la invitación no sobrevive sin su alcance");
    }

    [PostgresFact]
    public async Task Alcance_SoloAdmiteModosValidos_YUnaFilaPorUsuarioYOrganismo()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var user = HierarchyScenario.UserOf(HierarchyScenario.C1);

        await using (var ctx = NewContext())
        {
            ctx.OtUserClientScopes.Add(new OtUserClientScopeEntity
            {
                Id = Guid.NewGuid(), TenantId = HierarchyScenario.O, UserId = user, TransitOfficeId = HierarchyScenario.Ot1,
                Mode = OtUserClientScopeModes.Especificos, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync(Ct);
        }

        await using var duplicado = NewContext();
        duplicado.OtUserClientScopes.Add(new OtUserClientScopeEntity
        {
            Id = Guid.NewGuid(), TenantId = HierarchyScenario.O, UserId = user, TransitOfficeId = HierarchyScenario.Ot1,
            Mode = OtUserClientScopeModes.Todos, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await FluentActions.Awaiting(() => duplicado.SaveChangesAsync(Ct)).Should().ThrowAsync<DbUpdateException>();

        await using var invalido = NewContext();
        invalido.OtUserClientScopes.Add(new OtUserClientScopeEntity
        {
            Id = Guid.NewGuid(), TenantId = HierarchyScenario.O, UserId = HierarchyScenario.UserOf(HierarchyScenario.C2),
            TransitOfficeId = HierarchyScenario.Ot1, Mode = "algunos", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await FluentActions.Awaiting(() => invalido.SaveChangesAsync(Ct)).Should().ThrowAsync<DbUpdateException>();
    }
}
