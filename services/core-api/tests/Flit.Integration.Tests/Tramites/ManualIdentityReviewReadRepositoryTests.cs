using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Repositories;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13296 (Feature #13282, Épica #13202) — listado cross-tenant de validaciones manuales contra PostgreSQL real.
/// Dos compañías con filas manuales, más ruido que NO debe salir (Kyverum, aprobada automática). Prueba
/// filtros, orden, paginación, origen y la búsqueda ILIKE con comodines escapados.
/// </summary>
public sealed class ManualIdentityReviewReadRepositoryTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid TenantA = new("c1000000-0000-7000-8000-00000000013a");
    private static readonly Guid TenantB = new("c2000000-0000-7000-8000-00000000013b");
    private static readonly Guid OtTenant = new("c3000000-0000-7000-8000-00000000013c");
    private static readonly Guid Office = new("0199c300-0000-7000-8000-000000000001");
    private static readonly Guid Signer = new("0199c300-0000-7000-8000-000000000101");
    private static readonly Guid Gestor = new("c4000000-0000-7000-8000-00000000013d");
    private static readonly Guid Instancia = new("c5000000-0000-7000-8000-00000000013e");

    private static readonly Guid VTramitePendiente = new("c6000000-0000-7000-8000-000000000001");
    private static readonly Guid VPrevalPendienteVieja = new("c6000000-0000-7000-8000-000000000002");
    private static readonly Guid VPrevalEsperandoCaptura = new("c6000000-0000-7000-8000-000000000003");
    private static readonly Guid VAprobadaManual = new("c6000000-0000-7000-8000-000000000004");
    private static readonly Guid VMandatarioRechazada = new("c6000000-0000-7000-8000-000000000005");
    private static readonly Guid VAprobadaAutomatica = new("c6000000-0000-7000-8000-000000000006");
    private static readonly Guid VKyverum = new("c6000000-0000-7000-8000-000000000007");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ManualIdentityReviewFilter SinFiltros = new(null, null, null);

    private async Task SeedAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(TenantA, "IT-MAN-A", false, null));
            ctx.Tenants.Add(TenantSeed.New(TenantB, "IT-MAN-B", false, null));
            ctx.Tenants.Add(TenantSeed.New(OtTenant, "IT-MAN-OT", false, null));
            await ctx.SaveChangesAsync(Ct);
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13296@flit.test",
                DisplayName = "Gestor sintético 13296",
                Status = "active",
                HomeTenantId = TenantA,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync(Ct);
        }

        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn,
            """
            INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code)
            VALUES (@o, 'IT-OT-C1', 'OT integración C1', '05', '05001');
            INSERT INTO admin.transit_office_profiles (tenant_id, transit_office_id) VALUES (@ott, @o);
            INSERT INTO admin.mandate_signers
              (id, transit_office_id, full_name, document_type, document_number, integrity_hash, registered_at,
               created_at, signer_model, signature_method, is_active, email)
            VALUES (@s, @o, 'Mandatario Cuatro', 'CC', '4004', 'h', now(), now(), 'natural', 'biometria', true, 'm@flit.test');
            INSERT INTO tramites.procedure_instances
              (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
            VALUES (@i, @ta, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1),
                    'IT-13296', 'borrador', 'SINTVIN13296', @g, now());
            """,
            ("o", Office), ("ott", OtTenant), ("s", Signer), ("i", Instancia), ("ta", TenantA), ("g", Gestor));

        // Tramite en A, pendiente desde hace 2 h.
        await InsertValidationAsync(cn, VTramitePendiente, TenantA, "Ana Gómez", "1001", "pendiente_revision_manual",
            "manual", minutesAgo: 120, procedureId: Instancia);
        // Standalone en B, pendiente desde hace 5 h (la más antigua: sale primero).
        await InsertValidationAsync(cn, VPrevalPendienteVieja, TenantB, "Beto Ruiz", "2002", "pendiente_revision_manual",
            "manual", minutesAgo: 300, standalonePerson: true);
        // Standalone en B, esperando captura.
        await InsertValidationAsync(cn, VPrevalEsperandoCaptura, TenantB, "Carla 100%_Díaz", "3003", "manual_activo",
            "manual", minutesAgo: 10, standalonePerson: true);
        // Aprobada por el flujo manual (entra) en A.
        await InsertValidationAsync(cn, VAprobadaManual, TenantA, "Dora Eslava", "5005", "aprobado",
            "manual", minutesAgo: 600, standalonePerson: true, approvalOrigin: "manual");
        // Mandatario rechazada en B.
        await InsertValidationAsync(cn, VMandatarioRechazada, TenantB, "Mandatario Cuatro", "4004", "rechazado",
            "manual", minutesAgo: 900, mandatario: true);
        // Ruido: provider manual pero aprobación automática; y Kyverum.
        await InsertValidationAsync(cn, VAprobadaAutomatica, TenantA, "Eva Ruido", "6006", "aprobado",
            "manual", minutesAgo: 50, standalonePerson: true, approvalOrigin: "automatica");
        await InsertValidationAsync(cn, VKyverum, TenantB, "Fer Kyverum", "7007", "enviado",
            "kyverum", minutesAgo: 20, standalonePerson: true, activated: false);
    }

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static async Task InsertValidationAsync(
        NpgsqlConnection cn,
        Guid id,
        Guid tenant,
        string name,
        string document,
        string status,
        string provider,
        int minutesAgo,
        Guid? procedureId = null,
        bool standalonePerson = false,
        bool mandatario = false,
        string? approvalOrigin = null,
        bool activated = true)
    {
        Guid? personId = null;
        if (standalonePerson || procedureId is not null)
        {
            personId = Guid.NewGuid();
            await ExecAsync(cn,
                """
                INSERT INTO tramites.persons (id, tenant_id, document_type, document_number, full_name, email, person_type, created_at)
                VALUES (@p, @t, 'CC', @d, @n, 'p@flit.test', 'natural', now())
                """,
                ("p", personId), ("t", tenant), ("d", document), ("n", name));
        }

        await ExecAsync(cn,
            """
            INSERT INTO tramites.procedure_instance_biometric_validations
              (id, tenant_id, procedure_instance_id, person_id, party_role, mandate_signer_id, name, document_type,
               document_number, email, status, provider, token_hash, expires_at, created_at,
               manual_activated_at, approval_origin)
            VALUES (@id, @t, @pi, @pe, @role, @ms, @n, 'CC', @d, 'p@flit.test', @st, @pr, @h,
                    now() + interval '1 day', now(),
                    CASE WHEN @act THEN now() - make_interval(mins => @mins) END, @ao)
            """,
            ("id", id), ("t", tenant), ("pi", procedureId), ("pe", personId),
            ("role", mandatario ? "mandatario" : null), ("ms", mandatario ? Signer : null),
            ("n", name), ("d", document), ("st", status), ("pr", provider), ("h", Guid.NewGuid().ToString("N")),
            ("act", activated), ("mins", minutesAgo), ("ao", approvalOrigin));
    }

    private async Task<(IReadOnlyList<ManualIdentityReviewRow> Items, int Total)> ListAsync(
        ManualIdentityReviewFilter filter, int skip = 0, int take = 50)
    {
        await using var ctx = NewContext();
        return await new ManualIdentityReviewReadRepository(ctx).ListAsync(filter, skip, take, Ct);
    }

    [PostgresFact]
    public async Task AC1_ElSuperAdminVeFilasDeVariasCompaniasConSuNombre()
    {
        await SeedAsync();

        var (items, total) = await ListAsync(SinFiltros);

        total.Should().Be(5);
        items.Select(i => i.TenantName).Distinct().Should().HaveCount(2)
            .And.Contain(["Cliente de integración IT-MAN-A", "Cliente de integración IT-MAN-B"]);
        items.Select(i => i.Id).Should().BeEquivalentTo(
            [VTramitePendiente, VPrevalPendienteVieja, VPrevalEsperandoCaptura, VAprobadaManual, VMandatarioRechazada]);
    }

    [PostgresFact]
    public async Task HU13296_WaitingSince_usa_el_evento_de_captura_mas_reciente_o_UpdatedAt_y_solo_en_pendientes()
    {
        await SeedAsync();
        await using (var cn = await Fixture.OpenConnectionAsync())
        {
            await ExecAsync(cn,
                """
                UPDATE tramites.procedure_instance_biometric_validations
                   SET updated_at = now() - interval '45 minutes' WHERE id IN (@a, @b, @c);
                INSERT INTO tramites.identity_validation_audit (occurred_at, stage, outcome, validation_id, created_at)
                VALUES (now() - interval '3 hours', 'manual_captura_recibida', 'ok', @a, now()),
                       (now() - interval '30 minutes', 'manual_captura_recibida', 'ok', @a, now());
                """,
                ("a", VTramitePendiente), ("b", VPrevalPendienteVieja), ("c", VPrevalEsperandoCaptura));
        }

        var (items, _) = await ListAsync(SinFiltros);

        var conEvento = items.Single(i => i.Id == VTramitePendiente).WaitingSince!.Value;
        (DateTimeOffset.UtcNow - conEvento).TotalMinutes.Should().BeInRange(29, 35, "gana el evento más reciente (hace 30 min)");
        var sinEvento = items.Single(i => i.Id == VPrevalPendienteVieja).WaitingSince!.Value;
        (DateTimeOffset.UtcNow - sinEvento).TotalMinutes.Should().BeInRange(44, 50, "sin evento cae a UpdatedAt");
        items.Single(i => i.Id == VPrevalEsperandoCaptura).WaitingSince.Should().BeNull("manual_activo no mide revisión");
        items.Single(i => i.Id == VAprobadaManual).WaitingSince.Should().BeNull();

        await using var ctx = NewContext();
        var detalle = await new ManualIdentityReviewReadRepository(ctx).GetDetailAsync(VTramitePendiente, Ct);
        (DateTimeOffset.UtcNow - detalle!.WaitingSince!.Value).TotalMinutes.Should().BeInRange(29, 35);
    }

    [PostgresFact]
    public async Task AC4_SoloManuales_NoSalenKyverumNiLaAprobadaAutomatica()
    {
        await SeedAsync();

        var (items, _) = await ListAsync(SinFiltros);

        items.Select(i => i.Id).Should().NotContain([VKyverum, VAprobadaAutomatica]);
    }

    [PostgresFact]
    public async Task Orden_PendientesPrimeroLosMasAntiguosArriba_LuegoEsperandoCaptura_LuegoCerradas()
    {
        await SeedAsync();

        var (items, _) = await ListAsync(SinFiltros);

        items.Select(i => i.Id).Should().Equal(
            VPrevalPendienteVieja,   // pendiente, hace 5 h
            VTramitePendiente,       // pendiente, hace 2 h
            VPrevalEsperandoCaptura, // esperando captura
            VAprobadaManual,         // cerradas: la más reciente primero (hace 10 h)
            VMandatarioRechazada);   // hace 15 h
    }

    [PostgresFact]
    public async Task Origen_SeCalculaPorAncla()
    {
        await SeedAsync();

        var (items, _) = await ListAsync(SinFiltros);

        items.Single(i => i.Id == VTramitePendiente).Origin.Should().Be("tramite");
        items.Single(i => i.Id == VPrevalPendienteVieja).Origin.Should().Be("prevalidacion");
        items.Single(i => i.Id == VMandatarioRechazada).Origin.Should().Be("mandatario");
    }

    [PostgresTheory]
    [InlineData("tramite", 1)]
    [InlineData("prevalidacion", 3)]
    [InlineData("mandatario", 1)]
    [InlineData("representante_legal", 0)]
    public async Task AC2_FiltroPorOrigen(string origin, int esperadas)
    {
        await SeedAsync();

        var (items, total) = await ListAsync(new ManualIdentityReviewFilter(null, origin, null));

        total.Should().Be(esperadas);
        items.Count(i => i.Origin != origin).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC2_FiltroPorEstado()
    {
        await SeedAsync();

        var (items, total) = await ListAsync(new ManualIdentityReviewFilter("pendiente_revision_manual", null, null));

        total.Should().Be(2);
        items.Should().OnlyContain(i => i.Status == "pendiente_revision_manual");

        // Aprobado: solo la de origen manual.
        var (aprobadas, _) = await ListAsync(new ManualIdentityReviewFilter("aprobado", null, null));
        aprobadas.Select(i => i.Id).Should().Equal(VAprobadaManual);
    }

    [PostgresTheory]
    [InlineData("ruiz", 1)]        // nombre, sin distinguir mayúsculas
    [InlineData("GÓMEZ", 1)]
    [InlineData("5005", 1)]        // número de documento
    [InlineData("100", 2)]         // "1001" (Ana) y "Carla 100%_Díaz"
    [InlineData("%", 1)]           // el comodín se trata como literal: solo "Carla 100%_Díaz"
    [InlineData("_", 1)]           // idem para "_"
    [InlineData("inexistente", 0)]
    public async Task AC2_FiltroDeTextoEnNombreYDocumento_ConComodinesEscapados(string text, int esperadas)
    {
        await SeedAsync();

        var (_, total) = await ListAsync(new ManualIdentityReviewFilter(null, null, text));

        total.Should().Be(esperadas);
    }

    [PostgresFact]
    public async Task Paginacion_DevuelveElTotalYPaginasSinRepetirFilas()
    {
        await SeedAsync();

        var (pagina1, total1) = await ListAsync(SinFiltros, skip: 0, take: 2);
        var (pagina2, total2) = await ListAsync(SinFiltros, skip: 2, take: 2);
        var (pagina3, _) = await ListAsync(SinFiltros, skip: 4, take: 2);

        total1.Should().Be(5);
        total2.Should().Be(5);
        pagina1.Should().HaveCount(2);
        pagina2.Should().HaveCount(2);
        pagina3.Should().HaveCount(1);
        pagina1.Concat(pagina2).Concat(pagina3).Select(i => i.Id).Should().OnlyHaveUniqueItems();
    }
}
