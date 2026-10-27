using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13810 (Feature #13808, Épica #13516) — las búsquedas de usuario por correo (login, recuperación de
/// contraseña y unicidad en la administración de usuarios) normalizan la entrada y comparan de forma exacta,
/// contra PostgreSQL real. Antes usaban <c>ILIKE</c>, donde <c>_</c> y <c>%</c> son comodines.
/// <para>
/// Uso de ejemplo: con el usuario vivo <c>demo-13810@example.test</c>,
/// <c>FindByEmailAsync(" Demo-13810@Example.TEST ")</c> lo encuentra y <c>FindByEmailAsync("d_mo-13810@example.test")</c> no.
/// </para>
/// </summary>
public sealed class HU13810BusquedaCorreoExactaTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Correo = "demo-13810@example.test";
    private const string ConMayusculasYEspacios = " Demo-13810@Example.TEST ";
    private const string ConComodin = "d_mo-13810@example.test";
    private static readonly Guid UsuarioId = Guid.Parse("0199a000-0000-7000-8000-000000013810");

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
            Id = UsuarioId,
            Email = Correo,
            DisplayName = "Demo 13810",
            Status = "active",
            HomeTenantId = TenantSeed.LoneId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        // El login (AuthUserRepository) solo devuelve usuarios con credencial.
        ctx.UserCredentials.Add(new UserCredential
        {
            Id = Guid.CreateVersion7(),
            UserId = UsuarioId,
            PasswordHash = "hash-no-real",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync(Ct);
    }

    [PostgresFact]
    public async Task AC3_Login_encuentra_al_usuario_sin_importar_mayusculas_ni_espacios()
    {
        await using var db = NewContext();

        var usuario = await new AuthUserRepository(db, new UserRoleAssignmentRepository(db))
            .FindByEmailAsync(ConMayusculasYEspacios, Ct);

        usuario.Should().NotBeNull();
        usuario!.UserId.Should().Be(UsuarioId);
    }

    [PostgresFact]
    public async Task AC4_Login_no_trata_el_guion_bajo_como_comodin()
    {
        await using var db = NewContext();

        var usuario = await new AuthUserRepository(db, new UserRoleAssignmentRepository(db))
            .FindByEmailAsync(ConComodin, Ct);

        usuario.Should().BeNull();
    }

    [PostgresFact]
    public async Task AC3_AC4_Recuperacion_de_contrasena_compara_exacto_sobre_el_correo_normalizado()
    {
        await using var db = NewContext();
        var repo = new UserAccountRepository(db);

        (await repo.FindActiveByEmailAsync(ConMayusculasYEspacios, Ct)).Should().NotBeNull();
        (await repo.FindActiveByEmailAsync(ConComodin, Ct)).Should().BeNull();
        (await repo.FindActiveTargetByEmailAsync(ConMayusculasYEspacios, Ct)).Should().NotBeNull();
        (await repo.FindActiveTargetByEmailAsync(ConComodin, Ct)).Should().BeNull();
    }

    [PostgresFact]
    public async Task AC2_AC4_Unicidad_en_administracion_de_usuarios_compara_exacto_sobre_el_correo_normalizado()
    {
        await using var db = NewContext();
        var repo = new UserManagementRepository(db);

        (await repo.FindLiveByEmailAsync(ConMayusculasYEspacios, Ct))!.UserId.Should().Be(UsuarioId);
        (await repo.FindLiveByEmailAsync(ConComodin, Ct)).Should().BeNull();
    }

    [PostgresFact]
    public async Task AC2_Unicidad_al_invitar_compara_sobre_el_correo_normalizado()
    {
        await using var db = NewContext();

        (await new InvitationRepository(db).UserExistsWithEmailAsync("DEMO-13810@example.test", Ct)).Should().BeTrue();
    }
}
