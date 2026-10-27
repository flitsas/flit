using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Sql;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13809 (Feature #13808, Épica #13516) — el correo de usuarios e invitaciones queda normalizado y único a
/// nivel de datos, contra PostgreSQL real con la migración <c>HU13809_UnicidadCorreoNormalizado</c> aplicada.
/// <para>
/// AC3/AC4/AC5/AC6 prueban la base ya migrada. AC1/AC2 vuelven a correr el DDL 149 sobre datos sembrados sin
/// los CHECK (como estaría una base anterior a la migración), dentro de una transacción que se revierte: el
/// reset entre pruebas trunca filas pero no deshace cambios de esquema.
/// </para>
/// </summary>
public sealed class HU13809UnicidadCorreoNormalizadoTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string Ddl = "149-HU13809-unicidad-correo-normalizado.sql";
    private static readonly Guid TenantA = TenantSeed.ParentId;
    private static readonly Guid TenantB = TenantSeed.LoneId;
    private static readonly Guid Invitador = Guid.Parse("0199a000-0000-7000-8000-000000013809");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        if (!PostgresAvailability.IsAvailable)
            return;

        await using var ctx = NewContext();
        ctx.Tenants.Add(TenantSeed.GroupParent());
        ctx.Tenants.Add(TenantSeed.Lone());
        ctx.Users.Add(NuevoUsuario("invitador-13809@example.test", Invitador));
        await ctx.SaveChangesAsync(Ct);
    }

    private static User NuevoUsuario(string email, Guid? id = null, DateTimeOffset? deletedAt = null) => new()
    {
        Id = id ?? Guid.CreateVersion7(),
        Email = email,
        DisplayName = email,
        Status = "active",
        HomeTenantId = TenantA,
        CreatedAt = DateTimeOffset.UtcNow,
        DeletedAt = deletedAt,
    };

    private static UserInvitation NuevaInvitacion(Guid tenantId, string email, DateTimeOffset? creada = null) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Email = email,
        TokenHash = Guid.NewGuid().ToString("N"),
        Status = "pending",
        InvitedBy = Invitador,
        CreatedAt = creada ?? DateTimeOffset.UtcNow,
        RowVersion = 0,
    };

    private async Task<PostgresException> GuardarDebeFallarAsync(Action<Flit.Infrastructure.Persistence.FlitDbContext> agregar)
    {
        await using var ctx = NewContext();
        agregar(ctx);
        var ex = await FluentActions.Awaiting(() => ctx.SaveChangesAsync(Ct)).Should().ThrowAsync<DbUpdateException>();
        return ex.Which.InnerException.Should().BeOfType<PostgresException>().Subject;
    }

    [PostgresFact]
    public async Task AC3_La_base_rechaza_un_correo_de_usuario_sin_normalizar()
    {
        var pg = await GuardarDebeFallarAsync(ctx => ctx.Users.Add(NuevoUsuario("Juan@X.com")));

        pg.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        pg.ConstraintName.Should().Be("ck_users_email_normalizado");
    }

    [PostgresFact]
    public async Task AC3_La_base_rechaza_un_correo_de_invitacion_con_espacios()
    {
        var pg = await GuardarDebeFallarAsync(ctx => ctx.UserInvitations.Add(NuevaInvitacion(TenantA, " juan@x.com")));

        pg.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        pg.ConstraintName.Should().Be("ck_user_invitations_email_normalizado");
    }

    [PostgresFact]
    public async Task AC4_La_base_rechaza_un_segundo_usuario_vivo_con_el_mismo_correo()
    {
        await using (var seed = NewContext())
        {
            seed.Users.Add(NuevoUsuario("juan@x.com"));
            await seed.SaveChangesAsync(Ct);
        }

        var pg = await GuardarDebeFallarAsync(ctx => ctx.Users.Add(NuevoUsuario("juan@x.com")));

        pg.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        pg.ConstraintName.Should().Be("uq_users_email");
    }

    [PostgresFact]
    public async Task AC5_Un_usuario_eliminado_no_bloquea_recrear_su_correo()
    {
        await using (var seed = NewContext())
        {
            seed.Users.Add(NuevoUsuario("juan@x.com", deletedAt: DateTimeOffset.UtcNow.AddDays(-1)));
            await seed.SaveChangesAsync(Ct);
        }

        await using var ctx = NewContext();
        ctx.Users.Add(NuevoUsuario("juan@x.com"));
        await ctx.SaveChangesAsync(Ct);

        await using var check = NewContext();
        (await check.Users.CountAsync(u => u.Email == "juan@x.com", Ct)).Should().Be(2);
    }

    [PostgresFact]
    public async Task AC6_Solo_una_invitacion_pendiente_por_correo_entre_companias()
    {
        await using (var seed = NewContext())
        {
            seed.UserInvitations.Add(NuevaInvitacion(TenantA, "juan@x.com"));
            await seed.SaveChangesAsync(Ct);
        }

        var pg = await GuardarDebeFallarAsync(ctx => ctx.UserInvitations.Add(NuevaInvitacion(TenantB, "juan@x.com")));

        pg.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        pg.ConstraintName.Should().Be("uq_user_invitations_email_pending");
    }

    // ── Reaplicar el DDL sobre una base "anterior a la migración" ──────────────────────────────────

    /// <summary>Quita lo que crea el DDL 149 para sembrar datos como estaban antes de la migración.</summary>
    private const string EstadoPrevio =
        """
        ALTER TABLE identity.users DROP CONSTRAINT ck_users_email_normalizado;
        ALTER TABLE security.user_invitations DROP CONSTRAINT ck_user_invitations_email_normalizado;
        DROP INDEX security.uq_user_invitations_email_pending;
        CREATE UNIQUE INDEX uq_user_invitations_tenant_email_pending
            ON security.user_invitations (tenant_id, email) WHERE status = 'pending';
        """;

    [PostgresFact]
    public async Task AC1_El_DDL_normaliza_correos_y_cancela_pendientes_que_ya_no_pueden_aceptarse()
    {
        await using var ctx = NewContext();
        await using var tx = await ctx.Database.BeginTransactionAsync(Ct);
        await ctx.Database.ExecuteSqlRawAsync(EstadoPrevio, Ct);

        var usuario = NuevoUsuario(" Ana@X.com ");
        var conUsuarioVivo = NuevaInvitacion(TenantA, "ANA@x.com");
        var vieja = NuevaInvitacion(TenantA, "Pedro@x.com", DateTimeOffset.UtcNow.AddDays(-3));
        var reciente = NuevaInvitacion(TenantB, "pedro@X.com ", DateTimeOffset.UtcNow.AddDays(-1));
        ctx.Users.Add(usuario);
        ctx.UserInvitations.AddRange(conUsuarioVivo, vieja, reciente);
        await ctx.SaveChangesAsync(Ct);

        await ctx.Database.ExecuteSqlRawAsync(EmbeddedDdl.LoadUp(Ddl), Ct);

        ctx.ChangeTracker.Clear();
        (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == usuario.Id, Ct)).Email.Should().Be("ana@x.com");
        var invitaciones = await ctx.UserInvitations.AsNoTracking()
            .Where(i => i.Id == conUsuarioVivo.Id || i.Id == vieja.Id || i.Id == reciente.Id)
            .ToDictionaryAsync(i => i.Id, Ct);
        invitaciones[conUsuarioVivo.Id].Email.Should().Be("ana@x.com");
        invitaciones[vieja.Id].Email.Should().Be("pedro@x.com");
        invitaciones[reciente.Id].Email.Should().Be("pedro@x.com");
        invitaciones[conUsuarioVivo.Id].Status.Should().Be("cancelled", "su correo ya tiene un usuario vivo");
        invitaciones[vieja.Id].Status.Should().Be("cancelled", "entre pendientes del mismo correo queda la más reciente");
        invitaciones[reciente.Id].Status.Should().Be("pending");

        await tx.RollbackAsync(Ct);
    }

    [PostgresFact]
    public async Task AC2_El_DDL_aborta_si_hay_usuarios_vivos_duplicados_ignorando_mayusculas()
    {
        await using var ctx = NewContext();
        await using var tx = await ctx.Database.BeginTransactionAsync(Ct);
        await ctx.Database.ExecuteSqlRawAsync(EstadoPrevio, Ct);
        var original = NuevoUsuario("Juan@x.com");
        ctx.Users.AddRange(original, NuevoUsuario("juan@x.com"));
        await ctx.SaveChangesAsync(Ct);

        var ex = await FluentActions.Awaiting(() => ctx.Database.ExecuteSqlRawAsync(EmbeddedDdl.LoadUp(Ddl), Ct))
            .Should().ThrowAsync<PostgresException>();

        ex.Which.MessageText.Should().Contain("HU13809").And.Contain("juan@x.com");
        await tx.RollbackAsync(Ct);
    }
}
