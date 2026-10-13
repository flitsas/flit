using Flit.Infrastructure.Messaging;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain.Tenancy;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13286 (Feature #13280 A4, Épica #13202) — contra PostgreSQL real con TODAS las migraciones: ningún proceso automático
/// de Kyverum reclama una fila del flujo manual. Cada prueba siembra la MISMA fila con proveedor <c>manual</c> (no se reclama)
/// y luego con proveedor <c>kyverum</c> (sí se reclama): así el filtro no pasa por vacío. Cubre las consultas de reclamo del
/// worker de reconciliación (pendientes y vencidas), la cola de envío, y la cola de atascadas (listado y reencolado).
/// </summary>
public sealed class IdentidadManualAislamientoDeWorkersTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Office = new("0199c200-0000-7000-8000-000000000a41");
    private static readonly Guid OtTenant = new("b1000000-0000-7000-8000-0000000000a4");
    private static readonly Guid Company = new("b2000000-0000-7000-8000-0000000000a5");
    private static readonly Guid Signer = new("0199c200-0000-7000-8000-000000000a42");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private async Task<NpgsqlConnection> SeedAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(OtTenant, "IT-OT-A4", false, null));
            ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-A4", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code)
            VALUES (@o, 'IT-OT-A4', 'OT integración A4', '05', '05001');
            INSERT INTO admin.transit_office_profiles (tenant_id, transit_office_id) VALUES (@ott, @o);
            INSERT INTO admin.mandate_signers
              (id, transit_office_id, full_name, document_type, document_number, integrity_hash, registered_at,
               created_at, signer_model, signature_method, is_active, email)
            VALUES (@s, @o, 'Mandatario de prueba', 'CC', '7700999004', 'h', now(), now(), 'natural', 'biometria', true, 'm@flit.test');
            """,
            ("o", Office), ("ott", OtTenant), ("s", Signer));
        return cn;
    }

    /// <summary>Siembra UNA fila (la unicidad en vuelo por mandatario permite una a la vez: se limpia antes).</summary>
    private static async Task<Guid> SembrarFilaAsync(
        NpgsqlConnection cn, string provider, string status, TimeSpan expiraEn, string? kyverumId = "kyv-a4")
    {
        await ExecAsync(cn, "DELETE FROM tramites.procedure_instance_biometric_validations");
        var id = Guid.NewGuid();
        await ExecAsync(cn,
            """
            INSERT INTO tramites.procedure_instance_biometric_validations
              (id, tenant_id, party_role, mandate_signer_id, name, document_type, document_number, email, status, provider,
               kyverum_verification_id, token_hash, expires_at, created_at, updated_at, attempts, max_attempts)
            VALUES (@id, @t, 'mandatario', @s, 'Persona', 'CC', '7700999004', 'p@flit.test', @st, @p,
                    @k, @h, now() + @exp, now() - interval '1 day', now() - interval '1 day', 0, 3)
            """,
            ("id", id), ("t", Company), ("s", Signer), ("st", status), ("p", provider), ("k", kyverumId),
            ("h", Guid.NewGuid().ToString("N")), ("exp", expiraEn));
        return id;
    }

    private async Task<Guid?> ReclamarAsync(Func<FlitDbContext, Task<Guid?>> claim)
    {
        await using var ctx = NewContext();
        await using var tx = await ctx.Database.BeginTransactionAsync(Ct);
        var claimed = await claim(ctx);
        await tx.RollbackAsync(Ct);
        return claimed;
    }

    [PostgresFact]
    public async Task Reconcile_ClaimNextId_NoReclamaFilasManuales_PeroSiLaKyverumEquivalente()
    {
        await using var cn = await SeedAsync();
        var corte = DateTimeOffset.UtcNow;

        foreach (var estado in new[] { BiometricEstados.EnProceso, BiometricEstados.ManualActivo, BiometricEstados.PendienteRevisionManual })
        {
            // Una fila manual lo más tentadora posible para el worker: id de Kyverum residual, sin vencer.
            await SembrarFilaAsync(cn, BiometricProviders.Manual, estado, TimeSpan.FromHours(23));
            (await ReclamarAsync(ctx => IdentityValidationReconcileProcessor.ClaimNextIdAsync(ctx, corte, Ct)))
                .Should().BeNull($"una fila manual en {estado} no se reconcilia contra Kyverum");
        }

        var kyverum = await SembrarFilaAsync(cn, BiometricProviders.Kyverum, BiometricEstados.EnProceso, TimeSpan.FromHours(23));
        (await ReclamarAsync(ctx => IdentityValidationReconcileProcessor.ClaimNextIdAsync(ctx, corte, Ct)))
            .Should().Be(kyverum, "la misma fila con proveedor kyverum sí se reclama (AC4: Kyverum sigue igual)");
    }

    [PostgresFact]
    public async Task Reconcile_ClaimNextId_ReclamaElRechazoAntesDeAgotarIntentos_PeroNoElDefinitivo()
    {
        // Un rechazo aplicado con intentos disponibles se sigue consultando (la persona pudo aprobar después en el mismo
        // enlace); con los intentos agotados o rechazado por la revisión humana, no.
        await using var cn = await SeedAsync();
        var corte = DateTimeOffset.UtcNow;

        var prematuro = await SembrarFilaAsync(cn, BiometricProviders.Kyverum, BiometricEstados.Rechazado, TimeSpan.FromHours(23));
        await ExecAsync(cn, "UPDATE tramites.procedure_instance_biometric_validations SET attempts = 1, reconcile_poll_count = 3");
        (await ReclamarAsync(ctx => IdentityValidationReconcileProcessor.ClaimNextIdAsync(ctx, corte, Ct)))
            .Should().Be(prematuro, "con 1 de 3 intentos el rechazo no es definitivo; el sondeo lento la sigue consultando");

        await ExecAsync(cn, "UPDATE tramites.procedure_instance_biometric_validations SET attempts = 3");
        (await ReclamarAsync(ctx => IdentityValidationReconcileProcessor.ClaimNextIdAsync(ctx, corte, Ct)))
            .Should().BeNull("con los intentos agotados el rechazo es definitivo");

        await ExecAsync(cn,
            "UPDATE tramites.procedure_instance_biometric_validations SET attempts = 1, rejection_reason_code = 'documento_ilegible'");
        (await ReclamarAsync(ctx => IdentityValidationReconcileProcessor.ClaimNextIdAsync(ctx, corte, Ct)))
            .Should().BeNull("el rechazo de la revisión humana no lo reabre Kyverum");
    }

    [PostgresFact]
    public async Task Reconcile_ClaimNextExpiredId_NoTerminalizaFilasManualesVencidas_PeroSiLaKyverum()
    {
        await using var cn = await SeedAsync();
        var ahora = DateTimeOffset.UtcNow;

        foreach (var estado in new[] { BiometricEstados.EnProceso, BiometricEstados.ManualActivo, BiometricEstados.PendienteRevisionManual })
        {
            await SembrarFilaAsync(cn, BiometricProviders.Manual, estado, TimeSpan.FromHours(-1));
            (await ReclamarAsync(ctx => IdentityValidationReconcileProcessor.ClaimNextExpiredIdAsync(ctx, ahora, Ct)))
                .Should().BeNull($"el enlace manual vencido en {estado} no lo marca expirado el worker de Kyverum");
        }

        var kyverum = await SembrarFilaAsync(cn, BiometricProviders.Kyverum, BiometricEstados.EnProceso, TimeSpan.FromHours(-1));
        (await ReclamarAsync(ctx => IdentityValidationReconcileProcessor.ClaimNextExpiredIdAsync(ctx, ahora, Ct)))
            .Should().Be(kyverum);
    }

    [PostgresFact]
    public async Task ColaDeEnvio_ClaimNextId_NoReclamaFilasManuales_PeroSiLaKyverum()
    {
        await using var cn = await SeedAsync();

        await SembrarFilaAsync(cn, BiometricProviders.Manual, BiometricEstados.PendienteEnvio, TimeSpan.FromHours(23), kyverumId: null);
        (await ReclamarAsync(ctx => IdentityValidationSendRetryProcessor.ClaimNextIdAsync(ctx, Ct)))
            .Should().BeNull("una fila manual nunca se envía a Kyverum, ni siquiera si quedara en pendiente_envio");

        var kyverum = await SembrarFilaAsync(cn, BiometricProviders.Kyverum, BiometricEstados.PendienteEnvio, TimeSpan.FromHours(23), kyverumId: null);
        (await ReclamarAsync(ctx => IdentityValidationSendRetryProcessor.ClaimNextIdAsync(ctx, Ct)))
            .Should().Be(kyverum);
    }

    [PostgresFact]
    public async Task Atascadas_ListadoYReencolado_IgnoranFilasManuales_PeroNoLasKyverum()
    {
        await using var cn = await SeedAsync();
        var manual = await SembrarFilaAsync(cn, BiometricProviders.Manual, BiometricEstados.ErrorEnvio, TimeSpan.FromHours(23), kyverumId: null);

        await using (var ctx = NewContext())
        {
            var repo = new IdentityValidationOutboxRepository(ctx);
            (await repo.ListStuckAsync(TenantScope.Single(Company), 50, Ct)).Should().BeEmpty();
            (await repo.RequeueStuckAsync(Company, manual, Ct)).Should().BeFalse();
            (await repo.RequeueAllStuckAsync(Company, Ct)).Should().Be(0);
        }

        await using (var ctx = NewContext())
        {
            ctx.ProcedureInstanceBiometricValidations.Single(v => v.Id == manual).Status
                .Should().Be(BiometricEstados.ErrorEnvio, "el reencolado no la devolvió a pendiente_envio");
        }

        var kyverum = await SembrarFilaAsync(cn, BiometricProviders.Kyverum, BiometricEstados.ErrorEnvio, TimeSpan.FromHours(23), kyverumId: null);
        await using (var ctx = NewContext())
        {
            var repo = new IdentityValidationOutboxRepository(ctx);
            (await repo.ListStuckAsync(TenantScope.Single(Company), 50, Ct)).Should().ContainSingle(r => r.ValidationId == kyverum);
            (await repo.RequeueAllStuckAsync(Company, Ct)).Should().Be(1);
        }
    }
}
