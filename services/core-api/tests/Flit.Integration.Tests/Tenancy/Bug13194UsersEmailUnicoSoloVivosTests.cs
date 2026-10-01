using Flit.Api.Grpc;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Domain.Auth;
using Flit.Modules.Security.Domain.UserManagement;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Tenancy;

/// <summary>
/// Bug #13194 (review 2, O6 + B1) — <c>uq_users_email</c> es parcial (<c>deleted_at IS NULL</c>), contra
/// PostgreSQL real con la migración <c>BUG13194_UsersEmailUnicoSoloVivos</c> aplicada:
/// <list type="bullet">
/// <item>ACTIVAR una invitación sobre un correo cuyo único usuario está eliminado crea una fila NUEVA sin
/// violar el índice (la eliminada se conserva con su historial).</item>
/// <item>El usuario de servicio ICT (<c>IctOrchestrationService.ResolveIctCreatorAsync</c>) se obtiene-o-crea
/// con <c>ON CONFLICT (email) WHERE deleted_at IS NULL</c>: sin el predicado PostgreSQL no infiere el
/// árbitro sobre un índice parcial (42P10) y la materialización ICT se rompía en cada ejecución.</item>
/// <item>RESTAURAR la eliminada cuando el correo ya lo usa la cuenta viva: el repositorio traduce el
/// 23505 de <c>uq_users_email</c> a <see cref="UserEmailInUseByLiveAccountException"/> (409), no un 500.</item>
/// </list>
/// <para>
/// Uso de ejemplo:
/// <code>
/// await new UserActivationRepository(ctx).ActivateAsync(new ActivationData(..., "reuso@example.test", ...), ct);
/// // dos filas con el mismo correo: una con deleted_at, otra viva
/// </code>
/// </para>
/// </summary>
public sealed class Bug13194UsersEmailUnicoSoloVivosTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Correo = "reuso-13194@example.test";
    private static readonly Guid Tenant = TenantSeed.LoneId;
    private static readonly Guid UsuarioEliminado = Guid.Parse("0199a000-0000-7000-8000-000000013194");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (!PostgresAvailability.IsAvailable)
            return;

        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.Lone());
        ctx.Users.Add(new User
        {
            Id = UsuarioEliminado,
            Email = Correo,
            DisplayName = "Cuenta eliminada",
            Status = "active",
            HomeTenantId = Tenant,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            DeletedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await ctx.SaveChangesAsync(Ct);
    }

    private async Task ActivarInvitacionAsync()
    {
        await using var ctx = NewContext();
        await new UserActivationRepository(ctx).ActivateAsync(
            new ActivationData(
                InvitationId: Guid.NewGuid(),
                Email: Correo,
                FullName: "Cuenta nueva",
                PasswordHash: "hash-no-real",
                TenantId: Tenant,
                RoleIds: [],
                InvitedBy: Guid.NewGuid(),
                ActivatedAt: DateTimeOffset.UtcNow),
            Ct);
    }

    [PostgresFact]
    public async Task O6_Activar_invitacion_sobre_correo_de_cuenta_eliminada_crea_fila_nueva()
    {
        await ActivarInvitacionAsync();

        await using var check = NewContext();
        var filas = await check.Users.AsNoTracking()
            .Where(u => u.Email == Correo)
            .Select(u => new { u.Id, u.DeletedAt })
            .ToListAsync(Ct);

        filas.Should().HaveCount(2, "la cuenta eliminada se conserva y la activación crea otra fila");
        filas.Should().ContainSingle(u => u.DeletedAt == null && u.Id != UsuarioEliminado);
        filas.Should().ContainSingle(u => u.Id == UsuarioEliminado && u.DeletedAt != null);
    }

    [PostgresFact]
    public async Task B1_Restaurar_la_eliminada_con_correo_ya_vivo_traduce_23505_a_excepcion_de_dominio()
    {
        await ActivarInvitacionAsync();

        await using var ctx = NewContext();
        var act = () => new UserManagementRepository(ctx).RestoreUserAsync(UsuarioEliminado, restoredBy: null, Ct);

        await act.Should().ThrowAsync<UserEmailInUseByLiveAccountException>();

        await using var check = NewContext();
        (await check.Users.AsNoTracking().SingleAsync(u => u.Id == UsuarioEliminado, Ct))
            .DeletedAt.Should().NotBeNull("la restauración se rechazó y la cuenta sigue eliminada");
    }

    // ── Usuario de servicio ICT (code review 3ª vuelta) ─────────────────────────────────────────────

    private static string CorreoServicioIct => $"ict-integration+{Tenant}@flit.local";

    [PostgresFact]
    public async Task ICT_Usuario_de_servicio_se_crea_y_luego_se_obtiene_el_mismo()
    {
        Guid primero;
        await using (var ctx = NewContext())
            primero = await IctOrchestrationService.ResolveIctCreatorAsync(ctx, string.Empty, Tenant, Ct);
        Guid segundo;
        await using (var ctx = NewContext())
            segundo = await IctOrchestrationService.ResolveIctCreatorAsync(ctx, string.Empty, Tenant, Ct);

        segundo.Should().Be(primero, "la segunda ejecución obtiene el usuario que creó la primera");
        await using var check = NewContext();
        (await check.Users.AsNoTracking().CountAsync(u => u.Email == CorreoServicioIct, Ct)).Should().Be(1);
    }

    [PostgresFact]
    public async Task ICT_Con_usuario_de_servicio_eliminado_crea_uno_nuevo_vivo()
    {
        var eliminado = Guid.Parse("0199a000-0000-7000-8000-0000000131c7");
        await using (var seed = NewContext())
        {
            seed.Users.Add(new User
            {
                Id = eliminado,
                Email = CorreoServicioIct,
                DisplayName = "Integración ICT",
                Status = "active",
                HomeTenantId = Tenant,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
                DeletedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await seed.SaveChangesAsync(Ct);
        }

        Guid creado;
        await using (var ctx = NewContext())
            creado = await IctOrchestrationService.ResolveIctCreatorAsync(ctx, string.Empty, Tenant, Ct);

        creado.Should().NotBe(eliminado, "nunca se devuelve el usuario de servicio eliminado");
        await using var check = NewContext();
        var vivo = await check.Users.AsNoTracking().SingleAsync(u => u.Id == creado, Ct);
        vivo.DeletedAt.Should().BeNull();
        vivo.Email.Should().Be(CorreoServicioIct);
    }
}
