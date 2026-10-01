using Flit.Infrastructure.Persistence;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13128 (Feature #13114, Épica #13090, ADR-0061) — migración 122: modelo, forma de firma, vigencia,
/// origen y baja lógica del mandatario, contra PostgreSQL real.
/// <para>
/// El backfill se prueba llevando la base efímera al estado ANTERIOR a la migración
/// (<c>IMigrator.MigrateAsync(previa)</c> ejecuta su Down), sembrando filas con el esquema viejo y volviendo a
/// aplicarla. Cada prueba deja la base en la última migración.
/// </para>
/// Uso de ejemplo:
/// <code>
/// await MigrateToPreviousAsync();          // esquema sin las columnas nuevas
/// await SeedLegacyAsync(cn);               // mandatarios, vínculos, reglas y config heredados
/// await MigrateToHu13128Async();          // aplica el DDL 122 y su backfill
/// </code>
/// </summary>
public sealed class MandatarioModeloMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string MigrationId = "20260930120000_HU13128_MandatarioModeloVigenciaOrigen";

    private static readonly Guid Ot1 = new("0199b000-0000-7000-8000-000000000001");
    private static readonly Guid Ot2 = new("0199b000-0000-7000-8000-000000000002");
    private static readonly Guid OtTenant = new("a1000000-0000-7000-8000-0000000000a1");
    private static readonly Guid CompanyA = new("a2000000-0000-7000-8000-0000000000a2");
    private static readonly Guid CompanyB = new("a3000000-0000-7000-8000-0000000000a3");
    private static readonly Guid UserSuper = new("0199b000-0000-7000-8000-0000000000b1");
    private static readonly Guid UserOt = new("0199b000-0000-7000-8000-0000000000b2");
    private static readonly Guid UserCompany = new("0199b000-0000-7000-8000-0000000000b3");
    private static readonly Guid UserUnknown = new("0199b000-0000-7000-8000-0000000000b4");
    private static readonly Guid RoleSuper = new("0199b000-0000-7000-8000-0000000000c1");
    private static readonly Guid RoleOther = new("0199b000-0000-7000-8000-0000000000c2");

    private static readonly Guid S1Baul = new("0199b000-0000-7000-8000-000000000101");
    private static readonly Guid S2IdRef = new("0199b000-0000-7000-8000-000000000102");
    private static readonly Guid S3Bio = new("0199b000-0000-7000-8000-000000000103");
    private static readonly Guid S4OtroTenant = new("0199b000-0000-7000-8000-000000000104");
    private static readonly Guid S5Nada = new("0199b000-0000-7000-8000-000000000105");
    private static readonly Guid S6FallbackOt = new("0199b000-0000-7000-8000-000000000106");
    private static readonly Guid S7Rechazada = new("0199b000-0000-7000-8000-000000000107");
    private static readonly Guid S8Vencida = new("0199b000-0000-7000-8000-000000000108");

    // ── infraestructura de la prueba ───────────────────────────────────────────────────────────────

    private async Task<string> PreviousMigrationAsync()
    {
        await using var ctx = NewContext();
        var all = ctx.Database.GetMigrations().ToList();
        var idx = all.IndexOf(MigrationId);
        idx.Should().BeGreaterThan(0, "la migración HU13128 debe estar descubierta por EF (atributos inline)");
        return all[idx - 1];
    }

    private bool _downgraded;

    /// <summary>Si una prueba falla con la base en el estado previo, la deja en la última migración.</summary>
    public override async ValueTask DisposeAsync()
    {
        if (_downgraded && PostgresAvailability.IsAvailable)
        {
            // HU #13160: la migración de limpieza (DDL 127) aborta si identity_validation_ref tiene datos. Las filas
            // heredadas de estas pruebas se limpian antes de volver a la última migración.
            await using (var cn = await Fixture.OpenConnectionAsync())
            {
                await ExecAsync(cn,
                    "UPDATE admin.mandate_signers SET identity_validation_ref = NULL WHERE identity_validation_ref IS NOT NULL");
            }

            await MigrateToLatestAsync();
        }

        await base.DisposeAsync();
    }

    private async Task MigrateToPreviousAsync()
    {
        _downgraded = true;
        var previous = await PreviousMigrationAsync();
        await using var ctx = NewContext();
        await ctx.GetService<IMigrator>().MigrateAsync(previous, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Aplica solo hasta la migración HU13128 (no hasta la última): las pruebas de esta clase siembran y leen
    /// <c>identity_validation_ref</c>, columna que la migración de limpieza HU13160 elimina después.
    /// </summary>
    private async Task MigrateToHu13128Async()
    {
        await using var ctx = NewContext();
        await ctx.GetService<IMigrator>().MigrateAsync(MigrationId, TestContext.Current.CancellationToken);
    }

    private async Task MigrateToLatestAsync()
    {
        await using var ctx = NewContext();
        await ctx.GetService<IMigrator>().MigrateAsync(null, TestContext.Current.CancellationToken);
        _downgraded = false;
    }

    private static string Ddl122Path()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "Flit.Infrastructure", "Persistence", "Sql", "Ddl",
                "122-HU13128-mandatario-modelo-vigencia-origen.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("No se encontró el DDL 122 subiendo desde el directorio de salida.");
    }

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return result is null or DBNull ? default : (T)result;
    }

    private async Task SeedBaseAsync(NpgsqlConnection cn)
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(OtTenant, "IT-OT", false, null));
            ctx.Tenants.Add(TenantSeed.New(CompanyA, "IT-CIA-A", false, null));
            ctx.Tenants.Add(TenantSeed.New(CompanyB, "IT-CIA-B", false, null));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await ExecAsync(cn,
            """
            INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code) VALUES
              (@o1, 'IT-OT-1', 'OT integración 1', '05', '05001'),
              (@o2, 'IT-OT-2', 'OT integración 2', '05', '05002');
            INSERT INTO admin.transit_office_profiles (tenant_id, transit_office_id) VALUES (@ott, @o1);
            INSERT INTO identity.users (id, email, display_name, status, created_at) VALUES
              (@us, 'super@it.test', 'Super', 'active', now()),
              (@uo, 'ot@it.test', 'OT', 'active', now()),
              (@uc, 'cia@it.test', 'Cia', 'active', now());
            INSERT INTO security.roles (id, code, name) VALUES (@rs, 'SuperAdmin', 'Super'), (@rr, 'ot_admin', 'OT');
            INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id) VALUES
              (@ott, @us, @rs), (@ott, @uo, @rr);
            """,
            ("o1", Ot1), ("o2", Ot2), ("ott", OtTenant),
            ("us", UserSuper), ("uo", UserOt), ("uc", UserCompany),
            ("rs", RoleSuper), ("rr", RoleOther));
        await ExecAsync(cn,
            "INSERT INTO security.user_role_assignments (tenant_id, user_id, role_id) VALUES (@a, @uc, @rr)",
            ("a", CompanyA), ("uc", UserCompany), ("rr", RoleOther));
    }

    /// <summary>
    /// Inserta un mandatario «heredado». <c>identity_validation_ref</c> solo se escribe si se pide: la columna existe
    /// hasta la migración HU13128 y la limpieza HU13160 la elimina, así que con el esquema final no se puede nombrar.
    /// </summary>
    private static Task InsertLegacySignerAsync(
        NpgsqlConnection cn, Guid id, string documentNumber, Guid? createdBy,
        Guid? vaultId = null, Guid? identityRef = null) =>
        identityRef is null
            ? ExecAsync(cn,
                """
                INSERT INTO admin.mandate_signers
                  (id, transit_office_id, full_name, document_type, document_number, integrity_hash, registered_at,
                   created_at, created_by, signature_vault_id)
                VALUES (@id, @ot, 'Mandatario de prueba', 'CC', @doc, 'h', now(), now(), @cb, @v)
                """,
                ("id", id), ("ot", Ot1), ("doc", documentNumber), ("cb", createdBy), ("v", vaultId))
            : ExecAsync(cn,
                """
                INSERT INTO admin.mandate_signers
                  (id, transit_office_id, full_name, document_type, document_number, integrity_hash, registered_at,
                   created_at, created_by, signature_vault_id, identity_validation_ref)
                VALUES (@id, @ot, 'Mandatario de prueba', 'CC', @doc, 'h', now(), now(), @cb, @v, @r)
                """,
                ("id", id), ("ot", Ot1), ("doc", documentNumber), ("cb", createdBy), ("v", vaultId), ("r", identityRef));

    private static Task LinkAsync(NpgsqlConnection cn, Guid signerId, Guid company) =>
        ExecAsync(cn,
            """
            INSERT INTO admin.mandate_signer_companies (mandate_signer_id, transit_office_id, company_tenant_id, created_at)
            VALUES (@s, @ot, @c, now())
            """,
            ("s", signerId), ("ot", Ot1), ("c", company));

    private static async Task BiometricAsync(
        NpgsqlConnection cn, Guid tenant, string docType, string docNumber, string status, DateTimeOffset? validUntil)
    {
        var personId = Guid.CreateVersion7();
        await ExecAsync(cn,
            """
            INSERT INTO tramites.persons (id, tenant_id, document_type, document_number, full_name, email)
            VALUES (@p, @t, @dt, @dn, 'Persona IT', 'p@it.test')
            """,
            ("p", personId), ("t", tenant), ("dt", docType), ("dn", docNumber + Guid.NewGuid().ToString("N")[..4]));
        await ExecAsync(cn,
            """
            INSERT INTO tramites.procedure_instance_biometric_validations
              (tenant_id, person_id, name, document_type, document_number, email, status, token_hash, expires_at,
               validated_at, valid_until)
            VALUES (@t, @p, 'Persona IT', @dt, @dn, 'p@it.test', @st, @th, now() + interval '1 day', now(), @vu)
            """,
            ("t", tenant), ("p", personId), ("dt", docType), ("dn", docNumber), ("st", status),
            ("th", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")), ("vu", validUntil));
    }

    private async Task SeedLegacyAsync(NpgsqlConnection cn)
    {
        await SeedBaseAsync(cn);
        var vault = Guid.CreateVersion7();
        await ExecAsync(cn,
            """
            INSERT INTO admin.signature_vault
              (id, tenant_id, document_type, document_number, full_name, signature_hash, storage_path, storage_sha256,
               vigencia_desde, vigencia_hasta)
            VALUES (@v, @t, 'CC', '1', 'Firmante', 'sh', 'p', 'sha', current_date - 1, current_date + 30)
            """,
            ("v", vault), ("t", CompanyA));

        await InsertLegacySignerAsync(cn, S1Baul, "1001", UserSuper, vaultId: vault);
        await InsertLegacySignerAsync(cn, S2IdRef, "1002", null, identityRef: Guid.CreateVersion7());
        await InsertLegacySignerAsync(cn, S3Bio, "1003", UserOt);
        await InsertLegacySignerAsync(cn, S4OtroTenant, "1004", null);
        await InsertLegacySignerAsync(cn, S5Nada, "1005", null);
        await InsertLegacySignerAsync(cn, S6FallbackOt, "1006", null);
        await InsertLegacySignerAsync(cn, S7Rechazada, "1007", UserCompany);
        await InsertLegacySignerAsync(cn, S8Vencida, "1008", UserUnknown);

        foreach (var s in new[] { S1Baul, S3Bio, S4OtroTenant, S7Rechazada, S8Vencida })
        {
            await LinkAsync(cn, s, CompanyA);
        }

        // S1: baúl + biometría aprobada => gana baúl. S3: biometría (tipo/número sin normalizar) en la compañía vinculada.
        await BiometricAsync(cn, CompanyA, "CC", "1001", "aprobado", DateTimeOffset.UtcNow.AddDays(10));
        await BiometricAsync(cn, CompanyA, "cc", " 1003 ", "aprobado", DateTimeOffset.UtcNow.AddDays(10));
        // S4: aprobada pero en una compañía NO vinculada => no cuenta.
        await BiometricAsync(cn, CompanyB, "CC", "1004", "aprobado", DateTimeOffset.UtcNow.AddDays(10));
        // S6: sin vínculos; respaldo = tenant propio del OT.
        await BiometricAsync(cn, OtTenant, "CC", "1006", "aprobado", DateTimeOffset.UtcNow.AddDays(10));
        // S7: rechazada => no cuenta. S8: aprobada aunque su vigencia de 30 días ya venció (la forma no es la vigencia).
        await BiometricAsync(cn, CompanyA, "CC", "1007", "rechazado", null);
        await BiometricAsync(cn, CompanyA, "CC", "1008", "aprobado", DateTimeOffset.UtcNow.AddDays(-5));

        // Reglas por compañía: autor Super Admin / usuario OT / último escritor gana (updated_by).
        await ExecAsync(cn,
            """
            INSERT INTO admin.company_ot_mandate_rules (company_tenant_id, transit_office_id, created_at, created_by, updated_by) VALUES
              (@a, @o1, now(), @us, NULL),
              (@b, @o1, now(), @uo, NULL),
              (@a, @o2, now(), @us, @uo);
            INSERT INTO admin.transit_office_mandate_config (transit_office_id, template_code, requires_for_natural_person, created_at, created_by) VALUES
              (@o1, 'generico', false, now(), @us),
              (@o2, 'generico', false, now(), @uc);
            """,
            ("a", CompanyA), ("b", CompanyB), ("o1", Ot1), ("o2", Ot2),
            ("us", UserSuper), ("uo", UserOt), ("uc", UserCompany));
    }

    private static async Task<Dictionary<Guid, string?>> SignatureMethodsAsync(NpgsqlConnection cn)
    {
        var map = new Dictionary<Guid, string?>();
        await using var cmd = new NpgsqlCommand("SELECT id, signature_method FROM admin.mandate_signers", cn);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await r.ReadAsync(TestContext.Current.CancellationToken))
        {
            map[r.GetGuid(0)] = r.IsDBNull(1) ? null : r.GetString(1);
        }

        return map;
    }

    private static async Task<List<string>> ColumnsAsync(NpgsqlConnection cn, string table)
    {
        var cols = new List<string>();
        await using var cmd = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema='admin' AND table_name=@t", cn);
        cmd.Parameters.AddWithValue("t", table);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await r.ReadAsync(TestContext.Current.CancellationToken))
        {
            cols.Add(r.GetString(0));
        }

        return cols;
    }

    // ── AC1, AC3: columnas, defaults y registro de la migración ────────────────────────────────────

    [PostgresFact]
    public async Task AC1_AC3_ColumnasNuevas_MigracionRegistradaEIndiceParcial()
    {
        await using var cn = await Fixture.OpenConnectionAsync();

        (await ColumnsAsync(cn, "mandate_signers")).Should().Contain(
            ["signer_model", "signature_method", "validity_kind", "valid_from", "valid_to", "deleted_at", "deleted_by"]);
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM public.\"__EFMigrationsHistory\" WHERE migration_id = @m", ("m", MigrationId)))
            .Should().Be(1, "EF descubre y registra la migración una sola vez");
        (await ScalarAsync<string>(cn,
            "SELECT indexdef FROM pg_indexes WHERE schemaname='admin' AND indexname='ix_mandate_signers_alive'"))
            .Should().Contain("WHERE (deleted_at IS NULL)");
    }

    // ── AC1, AC2, AC7, AC9: backfill ────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC1_AC9_Backfill_SignatureMethod_Baul_Biometria_Nulo()
    {
        await MigrateToPreviousAsync();
        await using var cn = await Fixture.OpenConnectionAsync();
        (await ColumnsAsync(cn, "mandate_signers")).Should().NotContain("signature_method", "esquema previo a la migración");
        await SeedLegacyAsync(cn);

        await MigrateToHu13128Async();

        var m = await SignatureMethodsAsync(cn);
        m[S1Baul].Should().Be("baul", "el baúl gana aunque tenga biometría aprobada");
        m[S2IdRef].Should().Be("biometria", "identity_validation_ref vinculado");
        m[S3Bio].Should().Be("biometria", "biometría aprobada en el tenant de su compañía (documento normalizado)");
        m[S4OtroTenant].Should().BeNull("la aprobada está en una compañía no vinculada");
        m[S5Nada].Should().BeNull();
        m[S6FallbackOt].Should().Be("biometria", "sin compañía vinculada se usa el tenant propio del OT (como el resolver)");
        m[S7Rechazada].Should().BeNull("una validación rechazada no cuenta");
        m[S8Vencida].Should().Be("biometria", "aprobada aunque su vigencia de 30 días haya vencido: la forma no es la vigencia");

        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM admin.mandate_signers WHERE signer_model='natural' AND validity_kind='fixed' " +
            "AND valid_from IS NULL AND valid_to IS NULL AND deleted_at IS NULL AND deleted_by IS NULL", cn);
        ((long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).Should().Be(8);
    }

    [PostgresFact]
    public async Task AC2_AC7_Backfill_ConfiguredByScope_EnLasTresTablas()
    {
        await MigrateToPreviousAsync();
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedLegacyAsync(cn);

        await MigrateToHu13128Async();

        var rules = await ScopesAsync(cn,
            "SELECT company_tenant_id::text || '|' || transit_office_id::text, configured_by_scope FROM admin.company_ot_mandate_rules");
        rules[$"{CompanyA}|{Ot1}"].Should().Be("super_admin");
        rules[$"{CompanyB}|{Ot1}"].Should().Be("organismo");
        rules[$"{CompanyA}|{Ot2}"].Should().Be("organismo", "gana el último escritor (updated_by), que no es Super Admin");

        var config = await ScopesAsync(cn,
            "SELECT transit_office_id::text, configured_by_scope FROM admin.transit_office_mandate_config");
        config[Ot1.ToString()].Should().Be("super_admin");
        config[Ot2.ToString()].Should().Be("organismo", "autor de compañía: en config el resto cae en organismo");

        var links = await ScopesAsync(cn,
            "SELECT mandate_signer_id::text, configured_by_scope FROM admin.mandate_signer_companies");
        links[S1Baul.ToString()].Should().Be("super_admin", "lo creó un Super Admin");
        links[S3Bio.ToString()].Should().Be("organismo", "lo creó un usuario del tenant del OT");
        links[S7Rechazada.ToString()].Should().Be("compania", "lo creó un usuario de una compañía");
        links[S4OtroTenant.ToString()].Should().Be("organismo", "sin autor: organismo");
        links[S8Vencida.ToString()].Should().Be("organismo", "autor no resoluble: organismo");
        links.Values.Should().OnlyContain(v => v == "organismo" || v == "compania" || v == "super_admin");
    }

    private static async Task<Dictionary<string, string>> ScopesAsync(NpgsqlConnection cn, string sql)
    {
        var map = new Dictionary<string, string>();
        await using var cmd = new NpgsqlCommand(sql, cn);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await r.ReadAsync(TestContext.Current.CancellationToken))
        {
            map[r.GetString(0)] = r.GetString(1);
        }

        return map;
    }

    // ── AC4, AC5, AC8: CHECK ────────────────────────────────────────────────────────────────────────

    private static Task InsertSignerAsync(
        NpgsqlConnection cn, string model = "natural", string? method = null, string kind = "fixed",
        DateOnly? from = null, DateOnly? to = null, string? document = "999") =>
        ExecAsync(cn,
            """
            INSERT INTO admin.mandate_signers
              (transit_office_id, full_name, document_number, integrity_hash, registered_at, created_at,
               signer_model, signature_method, validity_kind, valid_from, valid_to)
            VALUES (@ot, 'Mandatario', @doc, 'h', now(), now(), @m, @sm, @k, @f, @t)
            """,
            ("ot", Ot1), ("doc", document), ("m", model), ("sm", method), ("k", kind),
            ("f", from is null ? null : from.Value), ("t", to is null ? null : to.Value));

    private static async Task ShouldBeRejectedAsync(Func<Task> action, string constraint)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(action);
        ex.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        ex.ConstraintName.Should().Be(constraint);
    }

    [PostgresFact]
    public async Task AC4_ValoresFueraDeCatalogo_LosRechazaLaBase()
    {
        await using var cn = await Fixture.OpenConnectionAsync();

        await ShouldBeRejectedAsync(() => InsertSignerAsync(cn, model: "otro"), "ck_mandate_signers_signer_model");
        await ShouldBeRejectedAsync(() => InsertSignerAsync(cn, method: "firma_fisica"), "ck_mandate_signers_signature_method");
        await ShouldBeRejectedAsync(() => InsertSignerAsync(cn, kind: "forever"), "ck_mandate_signers_validity_kind");

        await InsertSignerAsync(cn, method: "baul");
        await InsertSignerAsync(cn, method: "biometria");
        await InsertSignerAsync(cn, method: null);
        await InsertSignerAsync(cn, model: "juridica");
        await InsertSignerAsync(cn, model: "formato_blanco", document: null);
    }

    [PostgresFact]
    public async Task AC4_ConfiguredByScopeInvalido_LoRechazaLaBaseEnLasTresTablas()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedBaseAsync(cn);
        var signer = Guid.CreateVersion7();
        await InsertLegacySignerAsync(cn, signer, "77", null);

        await ShouldBeRejectedAsync(() => ExecAsync(cn,
            "INSERT INTO admin.company_ot_mandate_rules (company_tenant_id, transit_office_id, created_at, configured_by_scope) VALUES (@c, @o, now(), 'plataforma')",
            ("c", CompanyA), ("o", Ot1)), "ck_company_ot_mandate_rules_configured_by_scope");
        await ShouldBeRejectedAsync(() => ExecAsync(cn,
            "INSERT INTO admin.transit_office_mandate_config (transit_office_id, template_code, requires_for_natural_person, created_at, configured_by_scope) VALUES (@o, 'generico', false, now(), 'plataforma')",
            ("o", Ot1)), "ck_transit_office_mandate_config_configured_by_scope");
        await ShouldBeRejectedAsync(() => ExecAsync(cn,
            "INSERT INTO admin.mandate_signer_companies (mandate_signer_id, transit_office_id, company_tenant_id, created_at, configured_by_scope) VALUES (@s, @o, @c, now(), 'plataforma')",
            ("s", signer), ("o", Ot1), ("c", CompanyA)), "ck_mandate_signer_companies_configured_by_scope");

        foreach (var scope in new[] { "organismo", "compania", "super_admin" })
        {
            await ExecAsync(cn,
                "INSERT INTO admin.mandate_signer_companies (mandate_signer_id, transit_office_id, company_tenant_id, created_at, configured_by_scope) VALUES (@s, @o, @c, now(), @sc)",
                ("s", signer), ("o", Ot1), ("c", CompanyA), ("sc", scope));
            await ExecAsync(cn, "DELETE FROM admin.mandate_signer_companies");
        }
    }

    [PostgresFact]
    public async Task AC5_RangoConFechasIncoherentes_LoRechazaLaBase()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        var d = new DateOnly(2026, 10, 1);

        await ShouldBeRejectedAsync(
            () => InsertSignerAsync(cn, kind: "range", from: d, to: d.AddDays(-1)), "ck_mandate_signers_validity_range");
        await ShouldBeRejectedAsync(
            () => InsertSignerAsync(cn, kind: "range", from: d, to: null), "ck_mandate_signers_validity_range");
        await ShouldBeRejectedAsync(
            () => InsertSignerAsync(cn, kind: "fixed", from: d, to: d), "ck_mandate_signers_validity_fixed");

        await InsertSignerAsync(cn, kind: "range", from: d, to: d);
        await InsertSignerAsync(cn, kind: "range", from: d, to: d.AddDays(30));
    }

    [PostgresFact]
    public async Task AC4_JuridicaYFormatoBlanco_NoAdmitenFormaDeFirmaNiRango()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        var d = new DateOnly(2026, 10, 1);

        await ShouldBeRejectedAsync(
            () => InsertSignerAsync(cn, model: "juridica", method: "baul"), "ck_mandate_signers_model_coherence");
        await ShouldBeRejectedAsync(
            () => InsertSignerAsync(cn, model: "formato_blanco", document: null, kind: "range", from: d, to: d),
            "ck_mandate_signers_model_coherence");
    }

    [PostgresFact]
    public async Task AC8_FormatoBlancoSinDocumento_SeAcepta_NaturalYJuridicaLoExigen()
    {
        await using var cn = await Fixture.OpenConnectionAsync();

        await InsertSignerAsync(cn, model: "formato_blanco", document: null);
        await ShouldBeRejectedAsync(
            () => InsertSignerAsync(cn, model: "natural", document: null), "ck_mandate_signers_document_required");
        await ShouldBeRejectedAsync(
            () => InsertSignerAsync(cn, model: "juridica", document: null), "ck_mandate_signers_document_required");
    }

    // ── AC6: idempotencia y reversa ─────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC6_Idempotente_ReejecutarElDdlNoFallaNiPisaDatos()
    {
        await MigrateToPreviousAsync();
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedLegacyAsync(cn);
        await MigrateToHu13128Async();

        // Un cambio posterior de la aplicación no debe ser pisado por el backfill al re-ejecutar.
        await ExecAsync(cn, "UPDATE admin.mandate_signers SET signature_method='baul' WHERE id=@id", ("id", S5Nada));
        await ExecAsync(cn, "UPDATE admin.mandate_signer_companies SET configured_by_scope='compania' WHERE mandate_signer_id=@id", ("id", S1Baul));
        var before = (await ColumnsAsync(cn, "mandate_signers")).Count;

        var ddl = await File.ReadAllTextAsync(Ddl122Path(), TestContext.Current.CancellationToken);
        await ExecAsync(cn, ddl);
        await ExecAsync(cn, ddl);

        (await ColumnsAsync(cn, "mandate_signers")).Count.Should().Be(before, "no duplica columnas");
        (await SignatureMethodsAsync(cn))[S5Nada].Should().Be("baul");
        (await ScalarAsync<string>(cn,
            "SELECT configured_by_scope FROM admin.mandate_signer_companies WHERE mandate_signer_id=@id", ("id", S1Baul)))
            .Should().Be("compania");
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM pg_indexes WHERE schemaname='admin' AND indexname='ix_mandate_signers_alive'"))
            .Should().Be(1);
    }

    [PostgresFact]
    public async Task AC6_Reversa_EliminaSoloLoAgregadoYSeReaplica()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await InsertSignerAsync(cn, model: "formato_blanco", document: null);

        await MigrateToPreviousAsync();

        (await ColumnsAsync(cn, "mandate_signers")).Should().NotContain(
            ["signer_model", "signature_method", "validity_kind", "valid_from", "valid_to", "deleted_at", "deleted_by"]);
        (await ColumnsAsync(cn, "mandate_signers")).Should().Contain(
            ["document_number", "full_name", "is_active", "signature_vault_id", "identity_validation_ref"], "lo previo se conserva");
        (await ColumnsAsync(cn, "company_ot_mandate_rules")).Should().NotContain("configured_by_scope");
        (await ColumnsAsync(cn, "transit_office_mandate_config")).Should().NotContain("configured_by_scope");
        (await ColumnsAsync(cn, "mandate_signer_companies")).Should().NotContain("configured_by_scope");
        (await ScalarAsync<string>(cn,
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema='admin' AND table_name='mandate_signers' AND column_name='document_number'"))
            .Should().Be("NO", "la reversa restaura NOT NULL");
        (await ScalarAsync<string>(cn, "SELECT document_number FROM admin.mandate_signers")).Should().Be("N/A");
        (await ScalarAsync<long>(cn,
            "SELECT count(*) FROM pg_indexes WHERE schemaname='admin' AND indexname='ix_mandate_signers_alive'")).Should().Be(0);

        await MigrateToLatestAsync();
        (await ColumnsAsync(cn, "mandate_signers")).Should().Contain("signer_model");
    }
}
