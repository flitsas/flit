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
}
