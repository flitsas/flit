using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Modules.Security.Domain.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13811 (Feature #13808, Épica #13516) — contra PostgreSQL real: la invitación pendiente es única por correo
/// entre compañías y las dos carreras (invitar a la vez, activar a la vez) se traducen a excepciones de dominio
/// (409) en lugar de un 23505 sin manejar (500).
/// <para>
/// Uso de ejemplo: con una invitación pendiente de <c>juan-13811@example.test</c> en la compañía A,
/// <c>new InvitationRepository(db).ExistsPendingAsync("Juan-13811@example.test", ct)</c> devuelve <c>true</c>.
/// </para>
/// </summary>
public sealed class HU13811PendienteGlobalYActivacionTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Correo = "juan-13811@example.test";
    private static readonly Guid TenantA = TenantSeed.ParentId;
    private static readonly Guid TenantB = TenantSeed.LoneId;
    private static readonly Guid Invitador = Guid.Parse("0199a000-0000-7000-8000-000000013811");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (!PostgresAvailability.IsAvailable)
            return;

        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.GroupParent());
        ctx.Tenants.Add(TenantSeed.Lone());
        ctx.Users.Add(new User
        {
            Id = Invitador,
            Email = "invitador-13811@example.test",
            DisplayName = "Invitador",
            Status = "active",
            HomeTenantId = TenantA,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);
    }

    private async Task<Guid> InvitarAsync(Guid tenantId)
    {
        await using var db = NewContext();
        return await new InvitationRepository(db).CreateAsync(
            new UserInvitationData(tenantId, Correo, "Juan", [], Guid.NewGuid().ToString("N"), Invitador), Ct);
    }

    [PostgresFact]
    public async Task AC1_La_pendiente_de_otra_compania_cuenta_como_pendiente()
    {
        await InvitarAsync(TenantA);

        await using var db = NewContext();
        (await new InvitationRepository(db).ExistsPendingAsync("Juan-13811@example.test", Ct)).Should().BeTrue();
    }

    [PostgresFact]
    public async Task AC1_Invitar_a_la_vez_desde_otra_compania_se_traduce_a_pendiente_existente()
    {
        await InvitarAsync(TenantA);

        var act = () => InvitarAsync(TenantB);

        await act.Should().ThrowAsync<InvitationAlreadyPendingException>();
        await using var check = NewContext();
        (await check.UserInvitations.CountAsync(i => i.Email == Correo && i.Status == "pending", Ct)).Should().Be(1);
    }

    [PostgresFact]
    public async Task AC3_Activar_con_el_correo_ya_vivo_se_traduce_a_usuario_existente_y_no_guarda_nada()
    {
        var invitacion = await InvitarAsync(TenantA);
        await using (var seed = NewContext())
        {
            seed.Users.Add(new User
            {
                Id = Guid.CreateVersion7(),
                Email = Correo,
                DisplayName = "Ganó la carrera",
                Status = "active",
                HomeTenantId = TenantB,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await seed.SaveChangesAsync(Ct);
        }

        await using var db = NewContext();
        var act = () => new UserActivationRepository(db).ActivateAsync(
            new ActivationData(invitacion, Correo, "Juan", "hash-no-real", TenantA, [], Invitador, DateTimeOffset.UtcNow), Ct);

        await act.Should().ThrowAsync<UserAlreadyExistsException>();
        await using var check = NewContext();
        (await check.Users.CountAsync(u => u.Email == Correo, Ct)).Should().Be(1, "solo queda el usuario que ganó la carrera");
        (await check.UserInvitations.SingleAsync(i => i.Id == invitacion, Ct)).Status
            .Should().Be("pending", "la activación fallida no marca la invitación como aceptada");
    }
}
