using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13195 (Feature #13116, Épica #13090, ADR-0066 D1 / D-1) — migración 128: colapso sin borrar de los
/// vínculos mandatario-compañía activos y índice único parcial por (organismo, compañía, grupo de origen),
/// contra PostgreSQL real.
/// <para>
/// Para sembrar el caso N&gt;1 la base efímera se lleva al estado ANTERIOR (<c>IMigrator.MigrateAsync(previa)</c>
/// ejecuta el Down, que quita el índice) y luego se vuelve a aplicar. Cada prueba deja la base en la última
/// migración.
/// </para>
/// Uso de ejemplo:
/// <code>
/// await MigrateToPreviousAsync();      // sin el índice: se admiten N vínculos activos
/// await SeedAsync(cn);                 // grupos con varios vínculos
/// await MigrateToLatestAsync();        // colapsa y crea el índice
/// </code>
/// </summary>
public sealed class MandatarioUnoPorOrigenMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string MigrationId = "20260930180000_HU13195_MandatarioUnoPorOrigen";
    private const string IndexName = "uq_mandate_signer_companies_one_per_origin";

    private static readonly Guid Ot1 = new("0199c000-0000-7000-8000-000000000001");
    private static readonly Guid Ot2 = new("0199c000-0000-7000-8000-000000000002");
    private static readonly Guid OtTenant = new("a1000000-0000-7000-8000-0000000000c1");
    private static readonly Guid CompanyA = new("a2000000-0000-7000-8000-0000000000c2");
    private static readonly Guid CompanyB = new("a3000000-0000-7000-8000-0000000000c3");
    private static readonly Guid CompanyC = new("a4000000-0000-7000-8000-0000000000c4");

    private static Guid Signer(int n) => new($"0199c000-0000-7000-8000-0000000001{n:00}");

    private static Guid Link(int n) => new($"0199c000-0000-7000-8000-0000000002{n:00}");

    // Grupo 1 (Ot1, A): designado antiguo + firma válida + el más reciente inválido  => gana el designado.
    private const int G1Designado = 1, G1Valido = 2, G1Reciente = 3;

    // Grupo 2 (Ot1, B): firma válida antigua + inválido más reciente                  => gana la firma válida.
    private const int G2Valido = 4, G2Invalido = 5;

    // Grupo 3 (Ot1, C): dos inválidos, super_admin + organismo son UN grupo           => gana el más reciente.
    private const int G3Viejo = 6, G3Nuevo = 7;

    // Grupo 4 (Ot2, A): organismo + compania NO chocan (grupos distintos).
    private const int G4Organismo = 8, G4Compania = 9;

    // Grupo 5 (Ot2, B): dos de compania                                               => gana el más reciente.
    private const int G5Viejo = 10, G5Nuevo = 11;

    // Grupo 6 (Ot2, C): vínculo inactivo + uno activo: el inactivo no cuenta.
    private const int G6Inactivo = 12, G6Activo = 13;

    // ── infraestructura de la prueba ───────────────────────────────────────────────────────────────

    private bool _downgraded;

    public override async ValueTask DisposeAsync()
    {
        if (_downgraded && PostgresAvailability.IsAvailable)
        {
            await MigrateToLatestAsync();
        }

        await base.DisposeAsync();
    }

    private async Task<string> PreviousMigrationAsync()
    {
        await using var ctx = NewContext();
        var all = ctx.Database.GetMigrations().ToList();
        var idx = all.IndexOf(MigrationId);
        idx.Should().BeGreaterThan(0, "la migración HU13195 debe estar descubierta por EF (atributos inline)");
        return all[idx - 1];
    }

    private async Task MigrateToPreviousAsync()
    {
        _downgraded = true;
        var previous = await PreviousMigrationAsync();
        await using var ctx = NewContext();
        await ctx.GetService<IMigrator>().MigrateAsync(previous, TestContext.Current.CancellationToken);
    }

    private async Task MigrateToLatestAsync()
    {
        await using var ctx = NewContext();
        await ctx.GetService<IMigrator>().MigrateAsync(null, TestContext.Current.CancellationToken);
        _downgraded = false;
    }

    private static string DdlPath(string fileName, params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relative, fileName]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"No se encontró {fileName} subiendo desde el directorio de salida.");
    }

    private static string Ddl128Path() => DdlPath(
        "128-HU13195-mandatario-uno-por-origen.sql", "src", "Flit.Infrastructure", "Persistence", "Sql", "Ddl");

    private static string ReportScriptPath() => DdlPath(
        "hu-13195-reporte-colapso-vinculos-mandatario.sql", "docs", "sql");

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

    private static async Task InsertSignerAsync(
        NpgsqlConnection cn, int n, string method = "baul", string model = "natural", bool isActive = true) =>
        await ExecAsync(cn,
            """
            INSERT INTO admin.mandate_signers
              (id, transit_office_id, full_name, document_type, document_number, integrity_hash, registered_at,
               created_at, signer_model, signature_method, is_active)
            VALUES (@id, @ot, 'Mandatario de prueba', 'CC', @doc, 'h', now(), now(), @model, @method, @act)
            """,
            ("id", Signer(n)), ("ot", Ot1), ("doc", $"7700{n:00}"), ("model", model), ("method", method), ("act", isActive));

    private static Task InsertLinkAsync(
        NpgsqlConnection cn, int n, Guid office, Guid company, string scope, int minutesAgo, bool active = true) =>
        ExecAsync(cn,
            """
            INSERT INTO admin.mandate_signer_companies
              (id, mandate_signer_id, transit_office_id, company_tenant_id, is_active, created_at, configured_by_scope)
            VALUES (@id, @s, @o, @c, @a, now() - make_interval(mins => @m), @sc)
            """,
            ("id", Link(n)), ("s", Signer(n)), ("o", office), ("c", company), ("a", active), ("m", minutesAgo), ("sc", scope));

    private static Task InsertVaultAsync(NpgsqlConnection cn, Guid tenant, int signer, bool vigente = true) =>
        ExecAsync(cn,
            """
            INSERT INTO admin.signature_vault
              (id, tenant_id, document_type, document_number, full_name, signature_hash, storage_path, storage_sha256,
               vigencia_desde, vigencia_hasta)
            VALUES (uuidv7(), @t, 'CC', @doc, 'Firmante', 'sh', 'p', 'sha', @d, @h)
            """,
            ("t", tenant), ("doc", $"7700{signer:00}"),
            ("d", DateTime.UtcNow.Date.AddDays(vigente ? -5 : -60)),
            ("h", DateTime.UtcNow.Date.AddDays(vigente ? 30 : -30)));

    private async Task SeedAsync(NpgsqlConnection cn)
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(OtTenant, "IT-OT-C", false, null));
            ctx.Tenants.Add(TenantSeed.New(CompanyA, "IT-CIA-CA", false, null));
            ctx.Tenants.Add(TenantSeed.New(CompanyB, "IT-CIA-CB", false, null));
            ctx.Tenants.Add(TenantSeed.New(CompanyC, "IT-CIA-CC", false, null));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await ExecAsync(cn,
            """
            INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code) VALUES
              (@o1, 'IT-OT-C1', 'OT integración C1', '05', '05001'),
              (@o2, 'IT-OT-C2', 'OT integración C2', '05', '05002');
            INSERT INTO admin.transit_office_profiles (tenant_id, transit_office_id) VALUES (@ott, @o1);
            """,
            ("o1", Ot1), ("o2", Ot2), ("ott", OtTenant));

        for (var n = 1; n <= 13; n++)
        {
            await InsertSignerAsync(cn, n);
        }

        // G1: designado (más antiguo), válido (baúl vigente) y el más reciente (inválido).
        await InsertLinkAsync(cn, G1Designado, Ot1, CompanyA, "organismo", minutesAgo: 300);
        await InsertLinkAsync(cn, G1Valido, Ot1, CompanyA, "organismo", minutesAgo: 200);
        await InsertLinkAsync(cn, G1Reciente, Ot1, CompanyA, "organismo", minutesAgo: 10);
        await InsertVaultAsync(cn, CompanyA, G1Valido);
        await ExecAsync(cn,
            "INSERT INTO admin.company_ot_mandate_rules (company_tenant_id, transit_office_id, default_mandate_signer_id, created_at) " +
            "VALUES (@c, @o, @s, now())",
            ("c", CompanyA), ("o", Ot1), ("s", Signer(G1Designado)));

        // G2: firma válida antigua vs inválido reciente (baúl declarado, sin firma en el baúl).
        await InsertLinkAsync(cn, G2Valido, Ot1, CompanyB, "organismo", minutesAgo: 120);
        await InsertLinkAsync(cn, G2Invalido, Ot1, CompanyB, "organismo", minutesAgo: 5);
        await InsertVaultAsync(cn, CompanyB, G2Valido);

        // G3: ambos inválidos; super_admin y organismo son el mismo grupo.
        await InsertLinkAsync(cn, G3Viejo, Ot1, CompanyC, "super_admin", minutesAgo: 90);
        await InsertLinkAsync(cn, G3Nuevo, Ot1, CompanyC, "organismo", minutesAgo: 30);

        // G4: grupos de origen distintos: ambos se conservan.
        await InsertLinkAsync(cn, G4Organismo, Ot2, CompanyA, "organismo", minutesAgo: 60);
        await InsertLinkAsync(cn, G4Compania, Ot2, CompanyA, "compania", minutesAgo: 50);

        // G5: dos de la compañía.
        await InsertLinkAsync(cn, G5Viejo, Ot2, CompanyB, "compania", minutesAgo: 80);
        await InsertLinkAsync(cn, G5Nuevo, Ot2, CompanyB, "compania", minutesAgo: 40);

        // G6: el inactivo no cuenta.
        await InsertLinkAsync(cn, G6Inactivo, Ot2, CompanyC, "organismo", minutesAgo: 20, active: false);
        await InsertLinkAsync(cn, G6Activo, Ot2, CompanyC, "organismo", minutesAgo: 70);
    }

    private static async Task<Dictionary<int, bool>> ActiveByLinkAsync(NpgsqlConnection cn)
    {
        var map = new Dictionary<int, bool>();
        await using var cmd = new NpgsqlCommand(
            "SELECT id, is_active FROM admin.mandate_signer_companies", cn);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await r.ReadAsync(TestContext.Current.CancellationToken))
        {
            var id = r.GetGuid(0).ToString();
            map[int.Parse(id[^2..])] = r.GetBoolean(1);
        }

        return map;
    }

    // ── AC2: colapso sin borrar, con los 3 criterios de conservación ───────────────────────────────

    [PostgresFact]
    public async Task AC2_Colapso_ConservaDesignado_LuegoFirmaValida_LuegoMasReciente_SinBorrar()
    {
        await MigrateToPreviousAsync();
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        var totalAntes = await ScalarAsync<long>(cn, "SELECT count(*) FROM admin.mandate_signer_companies");
        var firmantesAntes = await ScalarAsync<long>(cn, "SELECT count(*) FROM admin.mandate_signers");

        await MigrateToLatestAsync();

        var a = await ActiveByLinkAsync(cn);
        // Criterio (a): el designado en la regla compañía×OT, aunque sea el más antiguo y el otro tenga firma válida.
        a[G1Designado].Should().BeTrue();
        a[G1Valido].Should().BeFalse();
        a[G1Reciente].Should().BeFalse();
        // Criterio (b): firma válida sobre el más reciente inválido.
        a[G2Valido].Should().BeTrue();
        a[G2Invalido].Should().BeFalse();
        // Criterio (c): ambos inválidos -> el más reciente; super_admin + organismo son UN grupo.
        a[G3Nuevo].Should().BeTrue();
        a[G3Viejo].Should().BeFalse();
        // AC4: grupos de origen distintos conviven.
        a[G4Organismo].Should().BeTrue();
        a[G4Compania].Should().BeTrue();
        // Mismo grupo compania: el más reciente.
        a[G5Nuevo].Should().BeTrue();
        a[G5Viejo].Should().BeFalse();
        // Un inactivo previo no cuenta y queda inactivo; el activo solo se conserva.
        a[G6Activo].Should().BeTrue();
        a[G6Inactivo].Should().BeFalse();

        (await ScalarAsync<long>(cn, "SELECT count(*) FROM admin.mandate_signer_companies"))
            .Should().Be(totalAntes, "no se borra ningún vínculo");
        (await ScalarAsync<long>(cn, "SELECT count(*) FROM admin.mandate_signers"))
            .Should().Be(firmantesAntes, "no se borra ningún mandatario");
        (await ScalarAsync<long>(cn,
                "SELECT count(*) FROM admin.mandate_signers WHERE is_active = false"))
            .Should().Be(0, "el colapso toca vínculos, no mandatarios");
        (await ScalarAsync<long>(cn,
                "SELECT count(*) FROM admin.mandate_signer_companies WHERE is_active"))
            .Should().Be(a.Count(kv => kv.Value), "conteos coherentes: 7 activos tras el colapso");
        a.Count(kv => kv.Value).Should().Be(7);
    }

    // ── AC1: reporte previo de solo lectura, sin datos personales ──────────────────────────────────

    private sealed record ReportRow(int Link, string Action, string Criterion, int GroupSize, string OriginGroup);

    private static async Task<(List<ReportRow> Rows, List<string> Columns)> RunReportScriptAsync(NpgsqlConnection cn)
    {
        var sql = await File.ReadAllTextAsync(ReportScriptPath(), TestContext.Current.CancellationToken);
        var rows = new List<ReportRow>();
        var columns = new List<string>();
        await using var cmd = new NpgsqlCommand(sql, cn);
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        for (var i = 0; i < r.FieldCount; i++)
        {
            columns.Add(r.GetName(i));
        }

        while (await r.ReadAsync(TestContext.Current.CancellationToken))
        {
            var id = r.GetGuid(r.GetOrdinal("link_id")).ToString();
            rows.Add(new ReportRow(
                int.Parse(id[^2..]),
                r.GetString(r.GetOrdinal("accion")),
                r.GetString(r.GetOrdinal("criterio")),
                (int)(long)r.GetValue(r.GetOrdinal("group_size")),
                r.GetString(r.GetOrdinal("origin_group"))));
        }

        return (rows, columns);
    }

    [PostgresFact]
    public async Task AC1_ReporteScript_DiceQueSeConservaYPorQue_NoModificaNada_SinDatosPersonales()
    {
        await MigrateToPreviousAsync();
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        var antes = await ActiveByLinkAsync(cn);

        var (rows, columns) = await RunReportScriptAsync(cn);

        rows.Select(r => r.Link).Should().BeEquivalentTo(
            [G1Designado, G1Valido, G1Reciente, G2Valido, G2Invalido, G3Viejo, G3Nuevo, G5Viejo, G5Nuevo],
            "solo los grupos con más de un vínculo activo; G4 y G6 no aparecen");
        rows.Single(r => r.Link == G1Designado).Should().Be(new ReportRow(G1Designado, "conservar", "designado_en_regla", 3, "organismo"));
        rows.Single(r => r.Link == G1Valido).Action.Should().Be("inactivar");
        rows.Single(r => r.Link == G2Valido).Should().Be(new ReportRow(G2Valido, "conservar", "firma_valida", 2, "organismo"));
        rows.Single(r => r.Link == G3Nuevo).Should().Be(new ReportRow(G3Nuevo, "conservar", "mas_reciente", 2, "organismo"));
        rows.Single(r => r.Link == G3Viejo).OriginGroup.Should().Be("organismo", "super_admin cae en el grupo organismo");
        rows.Single(r => r.Link == G5Nuevo).Should().Be(new ReportRow(G5Nuevo, "conservar", "mas_reciente", 2, "compania"));

        columns.Should().NotContain(c => c.Contains("document") || c.Contains("name") || c.Contains("email"),
            "el reporte no trae datos personales");
        (await ActiveByLinkAsync(cn)).Should().BeEquivalentTo(antes, "el reporte no modifica ningún dato");

        // Lo que el reporte dijo que se conserva es exactamente lo que deja activo la migración.
        var conservar = rows.Where(r => r.Action == "conservar").Select(r => r.Link).ToHashSet();
        await MigrateToLatestAsync();
        var despues = await ActiveByLinkAsync(cn);
        foreach (var r in rows)
        {
            despues[r.Link].Should().Be(conservar.Contains(r.Link), $"vínculo {r.Link}: reporte y colapso coinciden");
        }
    }

    [PostgresFact]
    public async Task AC1_ReporteDeLaApi_ElLectorCoincideConElScript()
    {
        await MigrateToPreviousAsync();
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        var (script, _) = await RunReportScriptAsync(cn);

        await using var ctx = NewContext();
        var reader = new DbMandateSignerLinkCollapseReader(ctx);
        var api = await reader.ListAsync(null, TestContext.Current.CancellationToken);

        api.Select(r => (r.LinkId.ToString()[^2..], r.Action, r.Criterion, r.GroupSize, r.OriginGroup))
            .Should().BeEquivalentTo(script.Select(r => (r.Link.ToString("00"), r.Action, r.Criterion, r.GroupSize, r.OriginGroup)));
        api.Should().OnlyContain(r => r.TransitOfficeCode.StartsWith("IT-OT-C"));
        (await reader.ListAsync(Ot2, TestContext.Current.CancellationToken)).Should().OnlyContain(r => r.TransitOfficeId == Ot2);
    }

    // ── AC3 / AC4: índice único parcial ────────────────────────────────────────────────────────────

    private static async Task ShouldViolateIndexAsync(Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(action);
        ex.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        ex.ConstraintName.Should().Be(IndexName);
    }

    [PostgresFact]
    public async Task AC3_AC4_Indice_RechazaUnSegundoActivoPorGrupo_PeroNoEntreGruposDeOrigenDistintos()
    {
        await MigrateToPreviousAsync();
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await MigrateToLatestAsync();

        (await ScalarAsync<string>(cn,
                "SELECT indexdef FROM pg_indexes WHERE schemaname='admin' AND indexname=@n", ("n", IndexName)))
            .Should().Contain("WHERE is_active").And.Contain("compania");

        // Mandatarios nuevos para probar (los del seed ya están vinculados).
        await InsertSignerAsync(cn, 20);
        await InsertSignerAsync(cn, 21);
        await InsertSignerAsync(cn, 22);

        // Mismo grupo (Ot1, CompanyA, organismo): otro 'organismo' choca, y 'super_admin' también (mismo grupo).
        await ShouldViolateIndexAsync(() => InsertLinkAsync(cn, 20, Ot1, CompanyA, "organismo", 0));
        await ShouldViolateIndexAsync(() => InsertLinkAsync(cn, 20, Ot1, CompanyA, "super_admin", 0));

        // Grupo de origen distinto: una compañía con vínculo 'organismo' admite UNO 'compania'.
        await InsertLinkAsync(cn, 20, Ot1, CompanyA, "compania", 0);
        await ShouldViolateIndexAsync(() => InsertLinkAsync(cn, 21, Ot1, CompanyA, "compania", 0));

        // Otro organismo u otra compañía no chocan.
        await InsertLinkAsync(cn, 22, Ot1, CompanyB, "compania", 0);

        // Inactivar libera el cupo; reactivar un colapsado ahora choca.
        await ExecAsync(cn, "UPDATE admin.mandate_signer_companies SET is_active = false WHERE id = @id", ("id", Link(G1Designado)));
        await ExecAsync(cn, "UPDATE admin.mandate_signer_companies SET is_active = true WHERE id = @id", ("id", Link(G1Valido)));
        await ShouldViolateIndexAsync(() => ExecAsync(cn,
            "UPDATE admin.mandate_signer_companies SET is_active = true WHERE id = @id", ("id", Link(G1Designado))));
    }

    // ── AC5: idempotencia y reversa ────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_Idempotente_ReejecutarElDdlNoFallaNiCambiaLosDatos()
    {
        await MigrateToPreviousAsync();
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await MigrateToLatestAsync();
        var antes = await ActiveByLinkAsync(cn);

        (await ScalarAsync<long>(cn,
                "SELECT count(*) FROM public.\"__EFMigrationsHistory\" WHERE migration_id = @m", ("m", MigrationId)))
            .Should().Be(1, "EF descubre y registra la migración una sola vez");

        var ddl = await File.ReadAllTextAsync(Ddl128Path(), TestContext.Current.CancellationToken);
        await ExecAsync(cn, ddl);
        await ExecAsync(cn, ddl);

        (await ActiveByLinkAsync(cn)).Should().BeEquivalentTo(antes);
        (await ScalarAsync<long>(cn,
                "SELECT count(*) FROM pg_indexes WHERE schemaname='admin' AND indexname=@n", ("n", IndexName)))
            .Should().Be(1);
        // EF tampoco la vuelve a aplicar.
        await MigrateToLatestAsync();
        (await ActiveByLinkAsync(cn)).Should().BeEquivalentTo(antes);
    }

    [PostgresFact]
    public async Task AC5_Reversa_QuitaSoloElIndice_NoReactivaVinculos_YSeReaplica()
    {
        await MigrateToPreviousAsync();
        await using var cn = await Fixture.OpenConnectionAsync();
        await SeedAsync(cn);
        await MigrateToLatestAsync();
        var colapsado = await ActiveByLinkAsync(cn);

        await MigrateToPreviousAsync(); // ejecuta el Down

        (await ScalarAsync<long>(cn,
                "SELECT count(*) FROM pg_indexes WHERE schemaname='admin' AND indexname=@n", ("n", IndexName)))
            .Should().Be(0, "la reversa elimina el índice");
        (await ScalarAsync<long>(cn,
                "SELECT count(*) FROM pg_indexes WHERE schemaname='admin' AND indexname='uq_mandate_signer_companies_active'"))
            .Should().Be(1, "el índice de ADR-0036 se conserva");
        (await ActiveByLinkAsync(cn)).Should().BeEquivalentTo(colapsado, "la reversa NO reactiva los vínculos inactivados");
        (await ScalarAsync<long>(cn,
                "SELECT count(*) FROM public.\"__EFMigrationsHistory\" WHERE migration_id = @m", ("m", MigrationId)))
            .Should().Be(0);

        await MigrateToLatestAsync();
        (await ScalarAsync<long>(cn,
                "SELECT count(*) FROM pg_indexes WHERE schemaname='admin' AND indexname=@n", ("n", IndexName)))
            .Should().Be(1);
        (await ActiveByLinkAsync(cn)).Should().BeEquivalentTo(colapsado);
    }

    // ── AC3: el repositorio traduce el índice a la excepción que la API responde como 409 ──────────

    private static CreateMandateSignerData Alta(Guid company, string documentNumber) =>
        new(
            TransitOfficeId: Ot1,
            OtTenantId: OtTenant,
            FullName: "Mandatario de prueba",
            DocumentNumber: documentNumber,
            IntegrityHash: new string('a', 64),
            RegisteredAt: DateTimeOffset.UtcNow,
            CompanyTenantIds: [company],
            CreatedBy: null,
            CorrelationId: null);

    [PostgresFact]
    public async Task AC3_Repositorio_TraduceLaViolacionDelIndice_AUnConflicto_EnAltaYEdicion()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(OtTenant, "IT-OT-C", false, null));
            ctx.Tenants.Add(TenantSeed.New(CompanyA, "IT-CIA-CA", false, null));
            ctx.Tenants.Add(TenantSeed.New(CompanyB, "IT-CIA-CB", false, null));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await ExecAsync(cn,
            "INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code) VALUES (@o1, 'IT-OT-C1', 'OT C1', '05', '05001')",
            ("o1", Ot1));

        await using var c1 = NewContext();
        var primero = await new MandateSignerRepository(c1).CreateAsync(Alta(CompanyA, "8800001"), TestContext.Current.CancellationToken);

        // Segundo mandatario activo para la misma compañía y organismo (mismo grupo de origen por defecto) -> conflicto.
        await using var c2 = NewContext();
        var alta = async () => await new MandateSignerRepository(c2)
            .CreateAsync(Alta(CompanyA, "8800002"), TestContext.Current.CancellationToken);
        var ex = (await alta.Should().ThrowAsync<MandateSignerActiveLinkConflictException>()).Which;
        ex.Message.Should().Contain("Ya existe un mandatario activo").And.NotContain("8800002");
        (await ScalarAsync<long>(cn, "SELECT count(*) FROM admin.mandate_signers"))
            .Should().Be(1, "la transacción se revierte completa: no queda el mandatario rechazado");

        // Otro mandatario en OTRA compañía sí se puede; moverlo a la compañía ocupada en una edición -> conflicto.
        await using var c3 = NewContext();
        var segundo = await new MandateSignerRepository(c3).CreateAsync(Alta(CompanyB, "8800003"), TestContext.Current.CancellationToken);
        await using var c4 = NewContext();
        var editar = async () => await new MandateSignerRepository(c4).UpdateAsync(
            new UpdateMandateSignerData(
                segundo, OtTenant, "Mandatario de prueba", "8800003", new string('b', 64),
                [CompanyA], UpdatedBy: null, CorrelationId: null),
            TestContext.Current.CancellationToken);
        await editar.Should().ThrowAsync<MandateSignerActiveLinkConflictException>();

        // Inactivar al primero libera el cupo y la edición pasa.
        await using var c5 = NewContext();
        (await new MandateSignerRepository(c5).InactivateAsync(
            new InactivateMandateSignerData(primero, OtTenant, null, null), TestContext.Current.CancellationToken)).Should().BeTrue();
        await using var c6 = NewContext();
        (await new MandateSignerRepository(c6).UpdateAsync(
            new UpdateMandateSignerData(
                segundo, OtTenant, "Mandatario de prueba", "8800003", new string('b', 64),
                [CompanyA], UpdatedBy: null, CorrelationId: null),
            TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    [PostgresFact]
    public async Task HU13195_OrigenEscritoAlCrear_OtYCompaniaConviven_SegundaDeLaCompaniaEs409_SuperAdminChocaConOt()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(OtTenant, "IT-OT-C", false, null));
            ctx.Tenants.Add(TenantSeed.New(CompanyA, "IT-CIA-CA", false, null));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await ExecAsync(cn,
            "INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code) VALUES (@o1, 'IT-OT-C1', 'OT C1', '05', '05001')",
            ("o1", Ot1));

        async Task<Guid> CrearAsync(string doc, string origen)
        {
            await using var c = NewContext();
            return await new MandateSignerRepository(c).CreateAsync(
                Alta(CompanyA, doc) with { ConfiguredByScope = origen }, TestContext.Current.CancellationToken);
        }

        // El OT ya tiene su mandatario activo; la compañía crea el suyo: grupos distintos, sin conflicto (201).
        var delOt = await CrearAsync("8800101", "organismo");
        var delaCompania = await CrearAsync("8800102", "compania");

        (await ScalarAsync<string>(cn,
            "SELECT configured_by_scope FROM admin.mandate_signer_companies WHERE mandate_signer_id = @id", ("id", delOt)))
            .Should().Be("organismo");
        (await ScalarAsync<string>(cn,
            "SELECT configured_by_scope FROM admin.mandate_signer_companies WHERE mandate_signer_id = @id", ("id", delaCompania)))
            .Should().Be("compania");

        // Segunda alta de la compañía en el mismo grupo -> conflicto (la API lo responde 409).
        var segunda = async () => await CrearAsync("8800103", "compania");
        await segunda.Should().ThrowAsync<MandateSignerActiveLinkConflictException>();

        // Super Admin y OT son el mismo grupo: choca con el del OT; el origen super_admin se guarda si el grupo está libre.
        var superAdmin = async () => await CrearAsync("8800104", "super_admin");
        await superAdmin.Should().ThrowAsync<MandateSignerActiveLinkConflictException>();

        await using (var c = NewContext())
        {
            await new MandateSignerRepository(c).InactivateAsync(
                new InactivateMandateSignerData(delOt, OtTenant, null, null), TestContext.Current.CancellationToken);
        }

        var libre = await CrearAsync("8800105", "super_admin");
        (await ScalarAsync<string>(cn,
            "SELECT configured_by_scope FROM admin.mandate_signer_companies WHERE mandate_signer_id = @id", ("id", libre)))
            .Should().Be("super_admin");
    }
}
