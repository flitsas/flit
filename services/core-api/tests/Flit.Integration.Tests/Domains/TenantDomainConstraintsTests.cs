using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Domains;

/// <summary>
/// HU #12416 (AC1, AC2, AC4, AC5, AC6, AC7) — el dominio de la red es un dato de la plataforma que el
/// MOTOR protege (DDL 116): host único global y un dominio vigente por red (índices únicos parciales),
/// formato RFC 1123 en minúsculas y punycode (CHECK), solo cabezas MARCA_BLANCA
/// (<c>identity.trg_require_marca_blanca_head()</c>, ERRCODE 23514 con
/// <c>ck_tenant_domains_marca_blanca</c>), estados cerrados con sus precondiciones, auditoría técnica,
/// RLS y la vista <c>admin.v_active_network_domains</c> que solo expone dominios <c>active</c> de
/// cabezas activas. Contra PostgreSQL real y con tres clientes: cabeza MARCA_BLANCA (con hija), cabeza
/// CONCESION y cliente sin jerarquía.
/// <para>
/// Uso de ejemplo: registrar <c>red.example.com</c> para la cabeza CONCESION debe fallar con 23514 y
/// <c>ConstraintName == "ck_tenant_domains_marca_blanca"</c>; registrarlo dos veces vigente falla con
/// 23505 y <c>uq_tenant_domains_host</c>.
/// </para>
/// </summary>
public sealed class TenantDomainConstraintsTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    private static readonly Guid MarcaBlancaHeadId = TenantSeed.ParentId;
    private static readonly Guid ConcesionHeadId = new("55555555-5555-4555-8555-555555555555");
    private static readonly Guid OtherMarcaBlancaHeadId = new("77777777-7777-4777-8777-777777777777");
    private static readonly Guid AdminId = new("66666666-6666-4666-8666-666666666666");

    // ── AC6 · paridad: la migración no puebla ────────────────────────────────────────────

    [PostgresFact]
    public async Task AC6_La_tabla_de_dominios_nace_vacia_y_la_vista_tambien()
    {
        await using var ctx = NewContext();

        (await ctx.TenantDomains.CountAsync()).Should().Be(0);
        (await ctx.ActiveNetworkDomains.CountAsync()).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC7_El_esquema_lleva_RLS_disparadores_indices_parciales_y_vista()
    {
        await using var connection = await Fixture.OpenConnectionAsync();

        await using (var cmd = new NpgsqlCommand(
            "SELECT relrowsecurity FROM pg_class WHERE oid = 'admin.tenant_domains'::regclass", connection))
        {
            (await cmd.ExecuteScalarAsync()).Should().Be(true);
        }

        await using (var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM pg_policies WHERE schemaname = 'admin' AND tablename = 'tenant_domains' AND policyname = 'tenant_isolation'",
            connection))
        {
            (await cmd.ExecuteScalarAsync()).Should().Be(1L);
        }

        await using (var cmd = new NpgsqlCommand(
            "SELECT string_agg(tgname, ',' ORDER BY tgname) FROM pg_trigger WHERE tgrelid = 'admin.tenant_domains'::regclass AND NOT tgisinternal AND tgenabled = 'O'",
            connection))
        {
            (await cmd.ExecuteScalarAsync()).Should().Be("tr_tenant_domains_audit,tr_tenant_domains_marca_blanca,tr_tenant_domains_row_version");
        }

        await using (var cmd = new NpgsqlCommand(
            "SELECT string_agg(indexname, ',' ORDER BY indexname) FROM pg_indexes WHERE schemaname = 'admin' AND tablename = 'tenant_domains' AND indexdef LIKE '%WHERE%'",
            connection))
        {
            (await cmd.ExecuteScalarAsync()).Should().Be(
                "ix_tenant_domains_host_active,ix_tenant_domains_next_check,uq_tenant_domains_host,uq_tenant_domains_tenant_id",
                "los únicos de host y de red son parciales (WHERE deleted_at IS NULL) para permitir el re-registro tras retiro");
        }

        await using (var cmd = new NpgsqlCommand(
            "SELECT relkind FROM pg_class WHERE oid = 'admin.v_active_network_domains'::regclass", connection))
        {
            (await cmd.ExecuteScalarAsync()).Should().Be('v');
        }
    }

    // ── AC1 · registro con formato válido y unicidad ─────────────────────────────────────

    [PostgresFact]
    public async Task AC1_Cabeza_MARCA_BLANCA_registra_su_dominio_en_estado_pendiente()
    {
        await SeedClientsAsync();

        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");

        await using var check = NewContext();
        var domain = await check.TenantDomains.AsNoTracking().SingleAsync(d => d.TenantId == MarcaBlancaHeadId);
        domain.Id.Should().NotBeEmpty("id uuidv7 generado por la base");
        domain.Status.Should().Be(TenantDomainStatuses.Pending);
        domain.VerifiedAt.Should().BeNull();
        domain.ActivatedAt.Should().BeNull();
        domain.DeletedAt.Should().BeNull();
        domain.RowVersion.Should().Be(0);
    }

    [PostgresFact]
    public async Task AC1_Un_host_ya_registrado_por_otra_red_es_rechazado()
    {
        await SeedClientsAsync();
        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");

        var pg = await ExpectPostgresErrorAsync(() => AddDomainAsync(OtherMarcaBlancaHeadId, "red.example.com", "tok-0000000000000002"));

        pg.SqlState.Should().Be(UniqueViolation);
        pg.ConstraintName.Should().Be("uq_tenant_domains_host");
    }

    [PostgresTheory]
    [InlineData("Red.Example.com", "ck_tenant_domains_host_format")]
    [InlineData("https://red.example.com", "ck_tenant_domains_host_format")]
    [InlineData("red.example.com:443", "ck_tenant_domains_host_format")]
    [InlineData("red.example.com/login", "ck_tenant_domains_host_format")]
    [InlineData("españa.com", "ck_tenant_domains_host_format")]
    [InlineData("-red.example.com", "ck_tenant_domains_host_format")]
    [InlineData("red..example.com", "ck_tenant_domains_host_format")]
    [InlineData("localhost", "ck_tenant_domains_host_format")]
    [InlineData("*.example.com", "ck_tenant_domains_host_format")]
    [InlineData("a.b", "ck_tenant_domains_host_format")]
    public async Task AC1_Formato_invalido_o_IDN_sin_codificar_es_rechazado_por_el_motor(string host, string constraint)
    {
        await SeedClientsAsync();

        var pg = await ExpectPostgresErrorAsync(() => AddDomainAsync(MarcaBlancaHeadId, host, "tok-0000000000000003"));

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be(constraint);
        await AssertNoDomainAsync(MarcaBlancaHeadId);
    }

    [PostgresTheory]
    [InlineData("xn--espaa-rta.com")]
    [InlineData("portal.xn--p1ai")]
    [InlineData("a-b.red.example.com")]
    public async Task AC1_Punycode_y_subdominios_con_guion_son_aceptados(string host)
    {
        await SeedClientsAsync();

        await AddDomainAsync(MarcaBlancaHeadId, host, "tok-0000000000000004");

        await using var check = NewContext();
        (await check.TenantDomains.AnyAsync(d => d.Host == host)).Should().BeTrue();
    }

    [PostgresFact]
    public async Task AC1_El_token_de_verificacion_nunca_se_reutiliza_ni_tras_retiro()
    {
        await SeedClientsAsync();
        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");
        await RetireDomainAsync(MarcaBlancaHeadId);

        var pg = await ExpectPostgresErrorAsync(() => AddDomainAsync(OtherMarcaBlancaHeadId, "otra.example.com", "tok-0000000000000001"));

        pg.SqlState.Should().Be(UniqueViolation);
        pg.ConstraintName.Should().Be("uq_tenant_domains_verification_token");
    }

    // ── AC2 · un dominio por red y sólo para Marca Blanca ────────────────────────────────

    [PostgresFact]
    public async Task AC2_Un_segundo_dominio_vigente_a_la_misma_red_es_rechazado()
    {
        await SeedClientsAsync();
        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");

        var pg = await ExpectPostgresErrorAsync(() => AddDomainAsync(MarcaBlancaHeadId, "otra.example.com", "tok-0000000000000002"));

        pg.SqlState.Should().Be(UniqueViolation);
        pg.ConstraintName.Should().Be("uq_tenant_domains_tenant_id");
    }

    [PostgresFact]
    public async Task AC2_Cabeza_CONCESION_no_puede_tener_dominio()
    {
        await SeedClientsAsync();

        var pg = await ExpectPostgresErrorAsync(() => AddDomainAsync(ConcesionHeadId, "cn.example.com", "tok-0000000000000005"));

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be("ck_tenant_domains_marca_blanca");
        pg.TableName.Should().Be("tenant_domains");
        pg.MessageText.Should().Contain("no es cabeza de tipo MARCA_BLANCA");
        await AssertNoDomainAsync(ConcesionHeadId);
    }

    [PostgresFact]
    public async Task AC2_Hija_de_la_MARCA_BLANCA_no_puede_tener_dominio_propio()
    {
        await SeedClientsAsync();

        var pg = await ExpectPostgresErrorAsync(() => AddDomainAsync(TenantSeed.ChildId, "hija.example.com", "tok-0000000000000006"));

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be("ck_tenant_domains_marca_blanca");
        await AssertNoDomainAsync(TenantSeed.ChildId);
    }

    [PostgresFact]
    public async Task AC2_Cliente_sin_jerarquia_no_puede_tener_dominio()
    {
        await SeedClientsAsync();

        var pg = await ExpectPostgresErrorAsync(() => AddDomainAsync(TenantSeed.LoneId, "lone.example.com", "tok-0000000000000007"));

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be("ck_tenant_domains_marca_blanca");
        await AssertNoDomainAsync(TenantSeed.LoneId);
    }

    [PostgresFact]
    public async Task AC2_Borrar_la_cabeza_con_dominio_es_rechazado_por_la_FK()
    {
        await SeedClientsAsync();
        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            await ctx.Tenants.Where(t => t.Id == TenantSeed.ChildId).ExecuteDeleteAsync();
            await ctx.Tenants.Where(t => t.Id == MarcaBlancaHeadId).ExecuteDeleteAsync();
        });

        pg.SqlState.Should().Be(ForeignKeyViolation);
        pg.ConstraintName.Should().Be("fk_tenant_domains_tenants");
    }

    // ── AC4 · estados válidos y resolución por la vista ──────────────────────────────────

    [PostgresTheory]
    [InlineData("disabled", "ck_tenant_domains_status")]
    [InlineData("ACTIVE", "ck_tenant_domains_status")]
    public async Task AC4_Estados_fuera_del_vocabulario_son_rechazados(string status, string constraint)
    {
        await SeedClientsAsync();
        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");

        var pg = await ExpectPostgresErrorAsync(() => UpdateDomainAsync(MarcaBlancaHeadId, d => d.Status = status));

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be(constraint);
    }

    [PostgresFact]
    public async Task AC4_Activo_exige_verificacion_activacion_y_certificado_y_fallido_exige_motivo()
    {
        await SeedClientsAsync();
        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");

        var comprobadoSinFecha = await ExpectPostgresErrorAsync(() => UpdateDomainAsync(MarcaBlancaHeadId, d => d.Status = TenantDomainStatuses.Verified));
        comprobadoSinFecha.SqlState.Should().Be(CheckViolation);
        comprobadoSinFecha.ConstraintName.Should().Be("ck_tenant_domains_verified_requires_verified_at");

        // PostgreSQL evalúa los CHECK en orden alfabético: con active sin nada, el primero que falla es
        // active_requires_verified (y es el nombre que verá la aplicación).
        var activoSinNada = await ExpectPostgresErrorAsync(() => UpdateDomainAsync(MarcaBlancaHeadId, d => d.Status = TenantDomainStatuses.Active));
        activoSinNada.SqlState.Should().Be(CheckViolation);
        activoSinNada.ConstraintName.Should().Be("ck_tenant_domains_active_requires_verified");

        var sinCertificado = await ExpectPostgresErrorAsync(() => UpdateDomainAsync(MarcaBlancaHeadId, d =>
        {
            d.Status = TenantDomainStatuses.Active;
            d.VerifiedAt = DateTimeOffset.UtcNow;
            d.ActivatedAt = DateTimeOffset.UtcNow;
        }));
        sinCertificado.SqlState.Should().Be(CheckViolation);
        sinCertificado.ConstraintName.Should().Be("ck_tenant_domains_active_requires_verified");

        var sinMotivo = await ExpectPostgresErrorAsync(() => UpdateDomainAsync(MarcaBlancaHeadId, d => d.Status = TenantDomainStatuses.Failed));
        sinMotivo.SqlState.Should().Be(CheckViolation);
        sinMotivo.ConstraintName.Should().Be("ck_tenant_domains_failed_requires_reason");
    }

    [PostgresFact]
    public async Task AC4_Solo_el_dominio_activo_de_una_cabeza_activa_aparece_en_la_vista()
    {
        await SeedClientsAsync();
        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");
        await AddDomainAsync(OtherMarcaBlancaHeadId, "otra.example.com", "tok-0000000000000002");

        await using (var ctx = NewContext())
        {
            (await ctx.ActiveNetworkDomains.CountAsync()).Should().Be(0, "pendiente no resuelve");
        }

        await UpdateDomainAsync(MarcaBlancaHeadId, d =>
        {
            d.Status = TenantDomainStatuses.Verified;
            d.VerifiedAt = DateTimeOffset.UtcNow;
        });
        await using (var ctx = NewContext())
        {
            (await ctx.ActiveNetworkDomains.CountAsync()).Should().Be(0, "comprobado aún no resuelve");
        }

        await ActivateAsync(MarcaBlancaHeadId);
        await UpdateDomainAsync(OtherMarcaBlancaHeadId, d =>
        {
            d.Status = TenantDomainStatuses.Failed;
            d.FailedAt = DateTimeOffset.UtcNow;
            d.FailureReason = "TXT_NOT_FOUND";
        });

        await using (var ctx = NewContext())
        {
            var resolubles = await ctx.ActiveNetworkDomains.AsNoTracking().ToListAsync();
            resolubles.Should().ContainSingle();
            resolubles[0].Host.Should().Be("red.example.com");
            resolubles[0].HeadTenantId.Should().Be(MarcaBlancaHeadId);
        }

        // AC5: inactivar la cabeza la saca de la vista sin tocar tenant_domains.
        await using (var ctx = NewContext())
        {
            var head = await ctx.Tenants.SingleAsync(t => t.Id == MarcaBlancaHeadId);
            head.IsActive = false;
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            (await ctx.ActiveNetworkDomains.CountAsync()).Should().Be(0);
            (await ctx.TenantDomains.CountAsync(d => d.TenantId == MarcaBlancaHeadId && d.Status == TenantDomainStatuses.Active)).Should().Be(1);
        }
    }

    // ── AC5 · cambio y retiro: re-registro tras baja, tipo cambiado conserva el dato, auditoría ─

    [PostgresFact]
    public async Task AC5_Retirar_conserva_la_fila_y_permite_reregistrar_el_mismo_host()
    {
        await SeedClientsAsync();
        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");
        await ActivateAsync(MarcaBlancaHeadId);
        await RetireDomainAsync(MarcaBlancaHeadId);

        await using (var ctx = NewContext())
        {
            (await ctx.ActiveNetworkDomains.CountAsync()).Should().Be(0, "retirado deja de resolver");
        }

        // El mismo host puede volver a registrarse (por la misma red o por otra) con token nuevo.
        await AddDomainAsync(OtherMarcaBlancaHeadId, "red.example.com", "tok-0000000000000002");
        await AddDomainAsync(MarcaBlancaHeadId, "nueva.example.com", "tok-0000000000000003");

        await using var check = NewContext();
        var filas = await check.TenantDomains.AsNoTracking().OrderBy(d => d.CreatedAt).ToListAsync();
        filas.Should().HaveCount(3, "la fila retirada se conserva como historia");
        filas[0].DeletedAt.Should().NotBeNull();
        filas[0].Status.Should().Be(TenantDomainStatuses.Active, "el retiro no reescribe el estado histórico");
    }

    [PostgresFact]
    public async Task AC5_Cambiar_la_cabeza_a_CONCESION_conserva_el_dominio_lo_saca_de_la_vista_y_permite_retirarlo()
    {
        await SeedClientsAsync();
        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");
        await ActivateAsync(MarcaBlancaHeadId);

        await using (var ctx = NewContext())
        {
            await ctx.Tenants.Where(t => t.Id == TenantSeed.ChildId).ExecuteDeleteAsync();
            var head = await ctx.Tenants.SingleAsync(t => t.Id == MarcaBlancaHeadId);
            head.TenantType = GroupKindCodes.Concesion;
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            (await ctx.TenantDomains.CountAsync(d => d.TenantId == MarcaBlancaHeadId)).Should().Be(1, "el dato se conserva");
            (await ctx.ActiveNetworkDomains.CountAsync()).Should().Be(0, "sin clase MARCA_BLANCA no resuelve");
        }

        // Cambiar el host ya no es posible; cambiar estado o retirar sí (el disparador solo vigila tenant_id y host).
        var pg = await ExpectPostgresErrorAsync(() => UpdateDomainAsync(MarcaBlancaHeadId, d => d.Host = "otra.example.com"));
        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be("ck_tenant_domains_marca_blanca");

        await RetireDomainAsync(MarcaBlancaHeadId);
        await using var check = NewContext();
        (await check.TenantDomains.AsNoTracking().SingleAsync(d => d.TenantId == MarcaBlancaHeadId)).DeletedAt.Should().NotBeNull();
    }

    [PostgresFact]
    public async Task AC5_Toda_escritura_incrementa_row_version_y_deja_rastro_en_audit_logs()
    {
        await SeedClientsAsync();
        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");
        await ActivateAsync(MarcaBlancaHeadId);
        await RetireDomainAsync(MarcaBlancaHeadId);

        await using (var ctx = NewContext())
        {
            (await ctx.TenantDomains.AsNoTracking().SingleAsync()).RowVersion.Should().Be(2, "activar + retirar = dos UPDATE");
        }

        await using var connection = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT string_agg(action, ',' ORDER BY id) FROM audit.audit_logs WHERE schema_name = 'admin' AND table_name = 'tenant_domains'",
            connection);

        (await cmd.ExecuteScalarAsync()).Should().Be("I,U,U");
    }

    [PostgresFact]
    public async Task AC5_Concurrencia_optimista_por_row_version()
    {
        await SeedClientsAsync();
        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");

        await using var primero = NewContext();
        await using var segundo = NewContext();
        var d1 = await primero.TenantDomains.SingleAsync(d => d.TenantId == MarcaBlancaHeadId);
        var d2 = await segundo.TenantDomains.SingleAsync(d => d.TenantId == MarcaBlancaHeadId);

        d1.NextCheckAt = DateTimeOffset.UtcNow;
        await primero.SaveChangesAsync();

        d2.CheckAttempts = 1;
        var act = () => segundo.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>("row_version cambió por el trigger y EF lo usa como token");
    }

    // ── AC7 · RLS: la política existe y filtra para un rol que no es owner ni superusuario ──

    [PostgresFact]
    public async Task AC7_La_politica_tenant_isolation_filtra_cuando_el_rol_no_es_owner()
    {
        await SeedClientsAsync();
        await AddDomainAsync(MarcaBlancaHeadId, "red.example.com", "tok-0000000000000001");
        await AddDomainAsync(OtherMarcaBlancaHeadId, "otra.example.com", "tok-0000000000000002");

        await using var connection = await Fixture.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();

        // El arnés conecta como owner/superusuario, a quien PostgreSQL nunca aplica RLS (ni con FORCE).
        // Para probar la política tal cual está escrita se crea un rol efímero dentro de la transacción
        // (CREATE ROLE es transaccional: el ROLLBACK lo retira) y se asume con SET LOCAL ROLE.
        await using (var can = new NpgsqlCommand(
            "SELECT rolsuper OR rolcreaterole FROM pg_roles WHERE rolname = current_user", connection, tx))
        {
            if ((await can.ExecuteScalarAsync()) is not true)
            {
                return; // sin privilegio para crear roles no se puede ejercitar RLS; la política se verifica en AC7_El_esquema_*
            }
        }

        await using (var role = new NpgsqlCommand(
            """
            CREATE ROLE flit_rls_probe_12416 NOLOGIN;
            GRANT USAGE ON SCHEMA admin, identity TO flit_rls_probe_12416;
            GRANT SELECT ON admin.tenant_domains TO flit_rls_probe_12416;
            SET LOCAL ROLE flit_rls_probe_12416;
            """, connection, tx))
        {
            await role.ExecuteNonQueryAsync();
        }

        await using (var set = new NpgsqlCommand(
            "SELECT set_config('app.is_superadmin', 'false', true), set_config('app.current_tenant_id', @tenant, true)", connection, tx))
        {
            set.Parameters.AddWithValue("tenant", MarcaBlancaHeadId.ToString());
            await set.ExecuteNonQueryAsync();
        }

        await using (var count = new NpgsqlCommand("SELECT string_agg(host, ',') FROM admin.tenant_domains", connection, tx))
        {
            (await count.ExecuteScalarAsync()).Should().Be("red.example.com", "solo el dominio del tenant en sesión");
        }

        await using (var set = new NpgsqlCommand("SELECT set_config('app.is_superadmin', 'true', true)", connection, tx))
        {
            await set.ExecuteNonQueryAsync();
        }

        await using (var count = new NpgsqlCommand("SELECT count(*) FROM admin.tenant_domains", connection, tx))
        {
            (await count.ExecuteScalarAsync()).Should().Be(2L, "el SuperAdmin ve todos");
        }

        await tx.RollbackAsync();
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────

    /// <summary>Dos cabezas MARCA_BLANCA (la primera con hija), una cabeza CONCESION y un cliente sin jerarquía.</summary>
    private async Task SeedClientsAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.AddRange(
            TenantSeed.New(MarcaBlancaHeadId, "IT-MB-HEAD", isGroupParent: true, parentId: null, GroupKindCodes.MarcaBlanca),
            TenantSeed.New(OtherMarcaBlancaHeadId, "IT-MB-HEAD-2", isGroupParent: true, parentId: null, GroupKindCodes.MarcaBlanca),
            TenantSeed.New(ConcesionHeadId, "IT-CN-HEAD", isGroupParent: true, parentId: null, GroupKindCodes.Concesion),
            TenantSeed.Lone());
        await ctx.SaveChangesAsync();

        await using var ctx2 = NewContext();
        ctx2.Tenants.Add(TenantSeed.ChildOf(MarcaBlancaHeadId));
        await ctx2.SaveChangesAsync();
    }

    private async Task AddDomainAsync(Guid tenantId, string host, string token)
    {
        await using var ctx = NewContext();
        ctx.TenantDomains.Add(new TenantDomainEntity { TenantId = tenantId, Host = host, VerificationToken = token, CreatedBy = AdminId });
        await ctx.SaveChangesAsync();
    }

    private async Task UpdateDomainAsync(Guid tenantId, Action<TenantDomainEntity> mutate)
    {
        await using var ctx = NewContext();
        var domain = await ctx.TenantDomains.SingleAsync(d => d.TenantId == tenantId && d.DeletedAt == null);
        mutate(domain);
        await ctx.SaveChangesAsync();
    }

    private Task ActivateAsync(Guid tenantId) => UpdateDomainAsync(tenantId, d =>
    {
        d.Status = TenantDomainStatuses.Active;
        d.VerifiedAt = DateTimeOffset.UtcNow;
        d.ActivatedAt = DateTimeOffset.UtcNow;
        d.CertificateIssuedAt = DateTimeOffset.UtcNow;
    });

    private Task RetireDomainAsync(Guid tenantId) => UpdateDomainAsync(tenantId, d =>
    {
        d.DeletedAt = DateTimeOffset.UtcNow;
        d.DeletedBy = AdminId;
    });

    private async Task AssertNoDomainAsync(Guid tenantId)
    {
        await using var check = NewContext();
        (await check.TenantDomains.AnyAsync(d => d.TenantId == tenantId)).Should().BeFalse();
    }

    private static async Task<PostgresException> ExpectPostgresErrorAsync(Func<Task> action)
    {
        var caught = await action.Should().ThrowAsync<Exception>();
        var ex = caught.Which;
        var pg = ex as PostgresException ?? ex.InnerException as PostgresException;
        pg.Should().NotBeNull($"se esperaba PostgresException (directa o como InnerException de {ex.GetType().Name})");
        return pg!;
    }
}
