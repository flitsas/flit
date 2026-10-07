using System.Text;
using Flit.Infrastructure.Persistence;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13283 (Feature #13280 A1, Épica #13202) — DDL 130 contra PostgreSQL real con TODAS las migraciones: la tabla de
/// validaciones biométricas acepta el proveedor y los estados manuales, rechaza lo que está fuera de la lista cerrada,
/// trae las columnas de consentimiento y revisión, y el backfill marca como automática lo ya aprobado.
/// </summary>
public sealed class IdentidadManualSchemaMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Office = new("0199c200-0000-7000-8000-000000000001");
    private static readonly Guid OtTenant = new("b1000000-0000-7000-8000-0000000000d1");
    private static readonly Guid Company = new("b2000000-0000-7000-8000-0000000000d2");
    private static readonly Guid Signer = new("0199c200-0000-7000-8000-000000000101");

    private static readonly string[] ColumnasNuevas =
    [
        "approval_origin", "manual_activated_by", "manual_activated_at", "consent_at", "consent_ip",
        "consent_text_version", "reviewed_by", "reviewed_at", "rejection_reason_code",
    ];

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

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await cmd.ExecuteScalarAsync(Ct);
        return result is null or DBNull ? default : (T)result;
    }

    private async Task<NpgsqlConnection> SeedAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(OtTenant, "IT-OT-M2", false, null));
            ctx.Tenants.Add(TenantSeed.New(Company, "IT-CIA-M2", false, null));
            await ctx.SaveChangesAsync(Ct);
        }

        var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code)
            VALUES (@o, 'IT-OT-M2', 'OT integración M2', '05', '05001');
            INSERT INTO admin.transit_office_profiles (tenant_id, transit_office_id) VALUES (@ott, @o);
            INSERT INTO admin.mandate_signers
              (id, transit_office_id, full_name, document_type, document_number, integrity_hash, registered_at,
               created_at, signer_model, signature_method, is_active, email)
            VALUES (@s, @o, 'Mandatario de prueba', 'CC', '7700999001', 'h', now(), now(), 'natural', 'biometria', true, 'm@flit.test');
            """,
            ("o", Office), ("ott", OtTenant), ("s", Signer));
        return cn;
    }

    // El ancla (CHECK ck_biometric_validation_anchor) se cumple con una ficha de mandatario: es la fila más barata de sembrar.
    private static Task InsertValidationAsync(NpgsqlConnection cn, string status, string provider) =>
        ExecAsync(cn,
            """
            INSERT INTO tramites.procedure_instance_biometric_validations
              (id, tenant_id, party_role, mandate_signer_id, name, document_type, document_number, email, status, provider,
               token_hash, expires_at, created_at)
            VALUES (uuidv7(), @t, 'mandatario', @s, 'Persona', 'CC', '7700999001', 'p@flit.test', @st, @p,
                    @h, now() + interval '1 hour', now())
            """,
            ("t", Company), ("s", Signer), ("st", status), ("p", provider), ("h", Guid.NewGuid().ToString("N")));

    private static async Task<string?> ConstraintOfViolationAsync(Func<Task> act)
    {
        try
        {
            await act();
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.ConstraintName ?? ex.SqlState;
        }
    }

    [PostgresFact]
    public async Task AC1_LaBdAceptaLosEstadosManualesConElProveedorManual()
    {
        await using var cn = await SeedAsync();

        foreach (var estado in new[] { BiometricEstados.ManualActivo, BiometricEstados.PendienteRevisionManual })
        {
            (await ConstraintOfViolationAsync(() => InsertValidationAsync(cn, estado, BiometricProviders.Manual)))
                .Should().BeNull($"{estado} con proveedor manual es válido");
        }

        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM tramites.procedure_instance_biometric_validations WHERE provider = 'manual'"))
            .Should().Be(2);
    }

    [PostgresFact]
    public async Task AC1_TodosLosEstadosYProveedoresDelCodigoSonValidosEnBd()
    {
        await using var cn = await SeedAsync();

        // Se limpia entre inserciones: la unicidad en vuelo por mandatario (DDL 129) permite una sola fila enviada a la vez.
        const string limpiar = "DELETE FROM tramites.procedure_instance_biometric_validations";
        foreach (var estado in BiometricEstados.Todos)
        {
            await ExecAsync(cn, limpiar);
            (await ConstraintOfViolationAsync(() => InsertValidationAsync(cn, estado, BiometricProviders.Mock)))
                .Should().BeNull($"el estado {estado} está en BiometricEstados");
        }

        foreach (var proveedor in BiometricProviders.Todos)
        {
            await ExecAsync(cn, limpiar);
            (await ConstraintOfViolationAsync(() => InsertValidationAsync(cn, BiometricEstados.Enviado, proveedor)))
                .Should().BeNull($"el proveedor {proveedor} está en BiometricProviders");
        }
    }

    [PostgresFact]
    public async Task AC2_UnEstadoOUnProveedorFueraDeLaListaLosRechazaElCheck()
    {
        await using var cn = await SeedAsync();

        (await ConstraintOfViolationAsync(() => InsertValidationAsync(cn, "estado_inventado", BiometricProviders.Mock)))
            .Should().Be("ck_biometric_validations_status");
        (await ConstraintOfViolationAsync(() => InsertValidationAsync(cn, BiometricEstados.Enviado, "otro_proveedor")))
            .Should().Be("ck_biometric_validations_provider");

        await InsertValidationAsync(cn, BiometricEstados.Aprobado, BiometricProviders.Mock);
        (await ConstraintOfViolationAsync(() => ExecAsync(cn,
                "UPDATE tramites.procedure_instance_biometric_validations SET approval_origin = 'robot'")))
            .Should().Be("ck_biometric_validations_approval_origin");
    }

    [PostgresFact]
    public async Task AC3_ExistenLasColumnasDeActivacionConsentimientoYRevision()
    {
        await using var cn = await SeedAsync();

        var columnas = new List<string>();
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT column_name || ':' || data_type || ':' || is_nullable FROM information_schema.columns
             WHERE table_schema = 'tramites' AND table_name = 'procedure_instance_biometric_validations'
               AND column_name = ANY(@c)
            """, cn))
        {
            cmd.Parameters.AddWithValue("c", ColumnasNuevas);
            await using var reader = await cmd.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                columnas.Add(reader.GetString(0));
            }
        }

        columnas.Should().BeEquivalentTo(
            "approval_origin:text:YES", "manual_activated_by:uuid:YES", "manual_activated_at:timestamp with time zone:YES",
            "consent_at:timestamp with time zone:YES", "consent_ip:text:YES", "consent_text_version:text:YES",
            "reviewed_by:uuid:YES", "reviewed_at:timestamp with time zone:YES", "rejection_reason_code:text:YES");
        (await ScalarAsync<int>(cn,
            """
            SELECT character_maximum_length FROM information_schema.columns
             WHERE table_schema = 'tramites' AND table_name = 'procedure_instance_biometric_validations' AND column_name = 'status'
            """)).Should().BeGreaterThanOrEqualTo(BiometricEstados.Todos.Max(e => e.Length), "pendiente_revision_manual cabe en status");
    }

    [PostgresFact]
    public async Task AC3_ElBackfillMarcaAutomaticaLoYaAprobadoYDejaNullElResto_Idempotente()
    {
        await using var cn = await SeedAsync();
        await InsertValidationAsync(cn, BiometricEstados.Aprobado, BiometricProviders.Kyverum);
        await InsertValidationAsync(cn, BiometricEstados.Rechazado, BiometricProviders.Kyverum);
        await InsertValidationAsync(cn, BiometricEstados.ManualActivo, BiometricProviders.Manual);

        // Reaplica el DDL 130 (como lo haría una BD con datos previos): es idempotente y hace el backfill.
        await ExecAsync(cn, LoadDdl());
        await ExecAsync(cn, LoadDdl());

        (await ScalarAsync<string>(cn,
            "SELECT approval_origin FROM tramites.procedure_instance_biometric_validations WHERE status = 'aprobado'"))
            .Should().Be("automatica");
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM tramites.procedure_instance_biometric_validations "
            + "WHERE status <> 'aprobado' AND approval_origin IS NOT NULL")).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC3_ElIndiceDeLaPestanaDeManualesEsParcialSobreElProveedorManual()
    {
        await using var cn = await SeedAsync();

        var definicion = await ScalarAsync<string>(cn,
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'tramites' AND indexname = 'ix_biometric_validations_manual_tab'");

        definicion.Should().NotBeNull();
        definicion.Should().Contain("(tenant_id, status, manual_activated_at)").And.Contain("provider");
    }

    [PostgresFact]
    public async Task AC4_LaMigracionQuedaRegistradaEnElHistorialYSeAplicoAlArrancar()
    {
        await using var cn = await SeedAsync();

        // El fixture migra una base vacía con Database.MigrateAsync(): si la migración no fuera descubrible
        // (sin Designer ni atributos) no estaría en el historial y las columnas no existirían.
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM public.\"__EFMigrationsHistory\" WHERE migration_id = '20261005205449_HU13283_IdentidadManual'"))
            .Should().Be(1);
    }

    private static string LoadDdl()
    {
        const string resource = "Flit.Infrastructure.Persistence.Sql.Ddl.130-HU13283-identidad-manual.sql";
        using var stream = typeof(FlitDbContext).Assembly.GetManifestResourceStream(resource);
        stream.Should().NotBeNull($"el DDL embebido {resource} debe existir");
        using var reader = new StreamReader(stream!, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
