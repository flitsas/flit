using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #13885 — contra PostgreSQL real con TODAS las migraciones. Una validación en vuelo de un trámite anulado o borrado
/// quedaba en vuelo para siempre (Bug #13055 la congelaba y el worker excluía esos trámites al expirar), y el índice único
/// <c>uq_biometric_validations_inflight_doc_norm</c> impedía guardar cualquier validación nueva de la persona en el tenant.
/// El worker ahora la reclama para cerrarla como <c>expirado</c>; las vivas, las terminales y las del mandatario no se tocan.
/// </summary>
public sealed class Bug13885ValidacionZombieWorkerTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Compania = new("b3000000-0000-7000-8000-000000013885");
    private static readonly Guid Gestor = new("b3000000-0000-7000-8000-000000138851");
    private static readonly Guid Anulado = new("b3000000-0000-7000-8000-000000138852");
    private static readonly Guid Borrado = new("b3000000-0000-7000-8000-000000138853");
    private static readonly Guid Vivo = new("b3000000-0000-7000-8000-000000138854");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private async Task<NpgsqlConnection> SeedAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Compania, "IT-13885", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync(Ct);
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13885@flit.test",
                DisplayName = "Gestor sintético 13885",
                Status = "active",
                HomeTenantId = Compania,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync(Ct);
        }

        var cn = await Fixture.OpenConnectionAsync();
        foreach (var (id, referencia, estado) in new[] { (Anulado, "IT-13885-A", "anulado"), (Borrado, "IT-13885-B", "borrador"), (Vivo, "IT-13885-V", "borrador") })
        {
            await ExecAsync(cn,
                """
                INSERT INTO tramites.procedure_instances
                    (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
                VALUES (@id, @t, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1), @ref, 'borrador', @vin, @g, now())
                """,
                ("id", id), ("t", Compania), ("ref", referencia), ("vin", "SINTVIN" + referencia[^1]), ("g", Gestor));
            await ExecAsync(cn,
                "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@t, @id, @st)",
                ("t", Compania), ("id", id), ("st", estado));
            await ExecAsync(cn, "UPDATE tramites.procedure_instances SET status = @st WHERE id = @id", ("st", estado), ("id", id));
        }

        await ExecAsync(cn, "UPDATE tramites.procedure_instances SET deleted_at = now() WHERE id = @id", ("id", Borrado));
        return cn;
    }

    private static async Task<Guid> SembrarValidacionAsync(
        NpgsqlConnection cn, Guid? tramite, string documento, string status, TimeSpan expiraEn, int diasAtras)
    {
        var id = Guid.NewGuid();
        await ExecAsync(cn,
            """
            INSERT INTO tramites.procedure_instance_biometric_validations
              (id, tenant_id, procedure_instance_id, party_role, name, document_type, document_number, email, status, provider,
               kyverum_verification_id, token_hash, expires_at, created_at, updated_at, attempts, max_attempts)
            VALUES (@id, @t, @pi, 'comprador', 'Persona', 'CC', @doc, 'p@flit.test', @st, 'kyverum',
                    @k, @h, now() + @exp, now() - @atras, now() - @atras, 0, 3)
            """,
            ("id", id), ("t", Compania), ("pi", tramite), ("doc", documento), ("st", status), ("k", "kyv-" + id.ToString("N")[..8]),
            ("h", Guid.NewGuid().ToString("N")), ("exp", expiraEn), ("atras", TimeSpan.FromDays(diasAtras)));
        return id;
    }

    private async Task<Guid?> ReclamarAsync()
    {
        await using var ctx = NewContext();
        await using var tx = await ctx.Database.BeginTransactionAsync(Ct);
        var id = await IdentityValidationReconcileProcessor.ClaimNextInFlightDeTramiteInactivoIdAsync(ctx, Ct);
        await tx.RollbackAsync(Ct);
        return id;
    }

    [PostgresFact]
    public async Task Worker_ReclamaSoloLasEnVueloDeTramitesAnuladosOBorrados()
    {
        await using var cn = await SeedAsync();
        var zombieAnulado = await SembrarValidacionAsync(cn, Anulado, "1001", "en_proceso", TimeSpan.FromDays(-20), diasAtras: 22);
        var zombieBorrado = await SembrarValidacionAsync(cn, Borrado, "1002", "enviado", TimeSpan.FromHours(5), diasAtras: 21);
        await SembrarValidacionAsync(cn, Vivo, "1003", "en_proceso", TimeSpan.FromHours(5), diasAtras: 30);
        await SembrarValidacionAsync(cn, Anulado, "1004", "aprobado", TimeSpan.FromDays(-20), diasAtras: 40);

        (await ReclamarAsync()).Should().Be(zombieAnulado, "la más antigua en vuelo de un trámite anulado");
        await ExecAsync(cn, "UPDATE tramites.procedure_instance_biometric_validations SET status = 'expirado' WHERE id = @id", ("id", zombieAnulado));

        (await ReclamarAsync()).Should().Be(zombieBorrado, "un trámite borrado tampoco puede usarla, aunque el enlace siga vigente");
        await ExecAsync(cn, "UPDATE tramites.procedure_instance_biometric_validations SET status = 'expirado' WHERE id = @id", ("id", zombieBorrado));

        (await ReclamarAsync()).Should().BeNull("las vivas y las terminales no se reclaman");
    }

    [PostgresFact]
    public async Task IndiceUnico_TrasCerrarLaZombie_AdmiteLaValidacionNuevaDeLaMismaPersona()
    {
        await using var cn = await SeedAsync();
        var zombie = await SembrarValidacionAsync(cn, Anulado, "10741866", "en_proceso", TimeSpan.FromDays(-20), diasAtras: 22);

        var bloqueada = async () => await SembrarValidacionAsync(cn, Vivo, "10741866", "en_proceso", TimeSpan.FromHours(24), diasAtras: 0);
        (await bloqueada.Should().ThrowAsync<PostgresException>()).Which.ConstraintName
            .Should().Be("uq_biometric_validations_inflight_doc_norm", "es el bloqueo que vio FT2-0000181");

        (await ReclamarAsync()).Should().Be(zombie);
        await ExecAsync(cn, "UPDATE tramites.procedure_instance_biometric_validations SET status = 'expirado' WHERE id = @id", ("id", zombie));

        var nueva = await SembrarValidacionAsync(cn, Vivo, "10741866", "en_proceso", TimeSpan.FromHours(24), diasAtras: 0);
        nueva.Should().NotBeEmpty();
    }
}
