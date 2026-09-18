using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Queries.Domain.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Branding;

/// <summary>
/// HU #12412 (AC2, AC4, AC6, AC7, AC8) — la marca y el logotipo solo existen sobre una cabeza de tipo
/// MARCA_BLANCA y lo fuerza el MOTOR (DDL 115: <c>identity.trg_require_marca_blanca_head()</c>,
/// ERRCODE 23514 con el nombre lógico del constraint), contra PostgreSQL real y con tres clientes:
/// cabeza MARCA_BLANCA, cabeza CONCESION y cliente sin jerarquía (más la hija de la MB). También
/// prueba la coherencia de publicación, el versionado del logotipo, que cambiar el tipo conserva el
/// dato (AC6) y que la migración no puebla nada (AC8).
/// <para>
/// Uso de ejemplo: <c>new TenantBrandingEntity { TenantId = TenantSeed.ParentId }</c> sobre una cabeza
/// sembrada con <c>tenantType: GroupKindCodes.Concesion</c> debe fallar con 23514 y
/// <c>ck_tenant_brandings_marca_blanca</c>.
/// </para>
/// </summary>
public sealed class TenantBrandingConstraintsTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string CheckViolation = "23514";
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    private static readonly Guid MarcaBlancaHeadId = TenantSeed.ParentId;
    private static readonly Guid ConcesionHeadId = new("55555555-5555-4555-8555-555555555555");
    private static readonly Guid PublisherId = new("66666666-6666-4666-8666-666666666666");

    // ── AC8 · la migración no puebla ─────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC8_Las_tablas_de_marca_nacen_vacias()
    {
        await using var ctx = NewContext();

        (await ctx.TenantBrandings.CountAsync()).Should().Be(0);
        (await ctx.TenantBrandLogos.CountAsync()).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC8_El_esquema_lleva_RLS_disparadores_y_funcion_compartida()
    {
        await using var connection = await Fixture.OpenConnectionAsync();

        await using (var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'admin' AND c.relname IN ('tenant_brandings', 'tenant_brand_logos') AND c.relrowsecurity",
            connection))
        {
            (await cmd.ExecuteScalarAsync()).Should().Be(2L, "ambas tablas con ROW LEVEL SECURITY");
        }

        await using (var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM pg_policies WHERE schemaname = 'admin' AND tablename IN ('tenant_brandings', 'tenant_brand_logos') AND policyname = 'tenant_isolation'",
            connection))
        {
            (await cmd.ExecuteScalarAsync()).Should().Be(2L);
        }

        await using (var cmd = new NpgsqlCommand(
            "SELECT string_agg(tgname, ',' ORDER BY tgname) FROM pg_trigger WHERE tgrelid IN ('admin.tenant_brandings'::regclass, 'admin.tenant_brand_logos'::regclass) AND NOT tgisinternal AND tgenabled = 'O'",
            connection))
        {
            (await cmd.ExecuteScalarAsync()).Should().Be(
                "tr_tenant_brand_logos_audit,tr_tenant_brand_logos_marca_blanca,tr_tenant_brand_logos_row_version,tr_tenant_brandings_audit,tr_tenant_brandings_marca_blanca,tr_tenant_brandings_row_version");
        }

        await using (var cmd = new NpgsqlCommand("SELECT to_regproc('identity.trg_require_marca_blanca_head') IS NOT NULL", connection))
        {
            (await cmd.ExecuteScalarAsync()).Should().Be(true);
        }
    }

    // ── AC1/AC2 · solo la cabeza MARCA_BLANCA tiene marca ────────────────────────────────

    [PostgresFact]
    public async Task AC1_Cabeza_MARCA_BLANCA_crea_su_marca_en_borrador()
    {
        await SeedThreeClientsAsync();

        await using (var ctx = NewContext())
        {
            ctx.TenantBrandings.Add(new TenantBrandingEntity { TenantId = MarcaBlancaHeadId });
            await ctx.SaveChangesAsync();
        }

        await using var check = NewContext();
        var branding = await check.TenantBrandings.AsNoTracking().SingleAsync(b => b.TenantId == MarcaBlancaHeadId);

        branding.Id.Should().NotBeEmpty("id uuidv7 generado por la base");
        branding.Draft.Should().Contain("\"schemaVersion\"");
        branding.Published.Should().BeNull("publicar es una operación explícita y separada");
        branding.PublishedVersion.Should().Be(0);
        branding.DeletedAt.Should().BeNull();
    }

    [PostgresFact]
    public async Task AC2_Cabeza_CONCESION_no_puede_tener_marca()
    {
        await SeedThreeClientsAsync();

        var pg = await ExpectPostgresErrorAsync(() => AddBrandingAsync(ConcesionHeadId));

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be("ck_tenant_brandings_marca_blanca");
        pg.TableName.Should().Be("tenant_brandings");
        pg.MessageText.Should().Contain("no es cabeza de tipo MARCA_BLANCA");
        await AssertNoBrandingAsync(ConcesionHeadId);
    }

    [PostgresFact]
    public async Task AC2_Cliente_sin_jerarquia_no_puede_tener_marca()
    {
        await SeedThreeClientsAsync();

        var pg = await ExpectPostgresErrorAsync(() => AddBrandingAsync(TenantSeed.LoneId));

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be("ck_tenant_brandings_marca_blanca");
        await AssertNoBrandingAsync(TenantSeed.LoneId);
    }

    [PostgresFact]
    public async Task AC2_Hija_de_la_cabeza_MARCA_BLANCA_no_puede_tener_marca_propia()
    {
        await SeedThreeClientsAsync();

        var pg = await ExpectPostgresErrorAsync(() => AddBrandingAsync(TenantSeed.ChildId));

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be("ck_tenant_brandings_marca_blanca");
        await AssertNoBrandingAsync(TenantSeed.ChildId);
    }

    [PostgresFact]
    public async Task AC2_Una_sola_marca_por_cabeza()
    {
        await SeedThreeClientsAsync();
        await AddBrandingAsync(MarcaBlancaHeadId);

        var pg = await ExpectPostgresErrorAsync(() => AddBrandingAsync(MarcaBlancaHeadId));

        pg.SqlState.Should().Be(UniqueViolation);
        pg.ConstraintName.Should().Be("uq_tenant_brandings_tenant_id");
    }

    [PostgresFact]
    public async Task AC2_Borrar_la_cabeza_con_marca_es_rechazado_por_la_FK()
    {
        await SeedThreeClientsAsync();
        await AddBrandingAsync(MarcaBlancaHeadId);

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            await ctx.Tenants.Where(t => t.Id == TenantSeed.ChildId).ExecuteDeleteAsync();
            await ctx.Tenants.Where(t => t.Id == MarcaBlancaHeadId).ExecuteDeleteAsync();
        });

        pg.SqlState.Should().Be(ForeignKeyViolation);
        pg.ConstraintName.Should().Be("fk_tenant_brandings_tenants");
    }

    // ── AC4 · borrador y publicación ─────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC4_Publicar_exige_snapshot_instante_autor_y_version_juntos()
    {
        await SeedThreeClientsAsync();
        await AddBrandingAsync(MarcaBlancaHeadId);

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            var branding = await ctx.TenantBrandings.SingleAsync(b => b.TenantId == MarcaBlancaHeadId);
            branding.Published = branding.Draft; // sin published_at / published_by / version
            await ctx.SaveChangesAsync();
        });

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be("ck_tenant_brandings_published_consistent");
    }

    [PostgresFact]
    public async Task AC4_Publicar_copia_el_borrador_incrementa_la_version_y_el_borrador_sigue_editable()
    {
        await SeedThreeClientsAsync();
        await AddBrandingAsync(MarcaBlancaHeadId);

        const string draftV1 = "{\"schemaVersion\":1,\"platformName\":\"Red Uno\",\"colors\":{\"primary\":\"#112233\",\"secondary\":\"#445566\",\"onPrimary\":\"#FFFFFF\"},\"logoId\":null}";

        await using (var ctx = NewContext())
        {
            var branding = await ctx.TenantBrandings.SingleAsync(b => b.TenantId == MarcaBlancaHeadId);
            branding.Draft = draftV1;
            branding.Published = draftV1;
            branding.PublishedVersion = 1;
            branding.PublishedAt = DateTimeOffset.UtcNow;
            branding.PublishedBy = PublisherId;
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = NewContext())
        {
            var branding = await ctx.TenantBrandings.SingleAsync(b => b.TenantId == MarcaBlancaHeadId);
            branding.RowVersion.Should().Be(1, "public.trg_row_version incrementa en cada UPDATE");
            branding.PublishedVersion.Should().Be(1);
            branding.PublishedBy.Should().Be(PublisherId);

            branding.Draft = "{\"schemaVersion\":1,\"platformName\":\"Red Uno v2\"}";
            await ctx.SaveChangesAsync();
        }

        await using var check = NewContext();
        var after = await check.TenantBrandings.AsNoTracking().SingleAsync(b => b.TenantId == MarcaBlancaHeadId);
        after.Published.Should().Contain("Red Uno").And.NotContain("v2", "el borrador no afecta a la publicada hasta la siguiente publicación");
        after.RowVersion.Should().Be(2);
    }

    // ── AC6 · cambiar el tipo de la cabeza conserva el dato ──────────────────────────────

    [PostgresFact]
    public async Task AC6_Cambiar_la_cabeza_sin_hijos_a_CONCESION_conserva_la_marca_y_permite_retirarla()
    {
        await SeedThreeClientsAsync();
        await AddBrandingAsync(MarcaBlancaHeadId);

        await using (var ctx = NewContext())
        {
            await ctx.Tenants.Where(t => t.Id == TenantSeed.ChildId).ExecuteDeleteAsync();
            var head = await ctx.Tenants.SingleAsync(t => t.Id == MarcaBlancaHeadId);
            head.TenantType = GroupKindCodes.Concesion;
            await ctx.SaveChangesAsync();
        }

        await using (var check = NewContext())
        {
            (await check.TenantBrandings.CountAsync(b => b.TenantId == MarcaBlancaHeadId)).Should().Be(1, "el dato se conserva (AC6)");
        }

        // Ya no es MARCA_BLANCA: editar el borrador es rechazado, pero retirar (deleted_at) sigue permitido.
        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            var branding = await ctx.TenantBrandings.SingleAsync(b => b.TenantId == MarcaBlancaHeadId);
            branding.Draft = "{\"schemaVersion\":1,\"platformName\":\"x\"}";
            await ctx.SaveChangesAsync();
        });
        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be("ck_tenant_brandings_marca_blanca");

        await using (var ctx = NewContext())
        {
            var branding = await ctx.TenantBrandings.SingleAsync(b => b.TenantId == MarcaBlancaHeadId);
            branding.DeletedAt = DateTimeOffset.UtcNow;
            branding.DeletedBy = PublisherId;
            await ctx.SaveChangesAsync();
        }

        await using var after = NewContext();
        (await after.TenantBrandings.AsNoTracking().SingleAsync(b => b.TenantId == MarcaBlancaHeadId)).DeletedAt.Should().NotBeNull();
    }

    // ── AC7 · logotipo versionado ────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC7_Reemplazar_el_logotipo_conserva_la_version_anterior_y_deja_una_sola_activa()
    {
        await SeedThreeClientsAsync();

        await using (var ctx = NewContext())
        {
            ctx.TenantBrandLogos.Add(NewLogo(MarcaBlancaHeadId, version: 1, sha: new string('a', 64)));
            await ctx.SaveChangesAsync();
        }

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            ctx.TenantBrandLogos.Add(NewLogo(MarcaBlancaHeadId, version: 2, sha: new string('b', 64)));
            await ctx.SaveChangesAsync();
        });
        pg.SqlState.Should().Be(UniqueViolation);
        pg.ConstraintName.Should().Be("uq_tenant_brand_logos_one_active");

        await using (var ctx = NewContext())
        {
            var v1 = await ctx.TenantBrandLogos.SingleAsync(l => l.TenantId == MarcaBlancaHeadId && l.Version == 1);
            v1.Status = "superseded";
            v1.SupersededAt = DateTimeOffset.UtcNow;
            v1.SupersededBy = PublisherId;
            ctx.TenantBrandLogos.Add(NewLogo(MarcaBlancaHeadId, version: 2, sha: new string('b', 64)));
            await ctx.SaveChangesAsync();
        }

        await using var check = NewContext();
        var logos = await check.TenantBrandLogos.AsNoTracking().Where(l => l.TenantId == MarcaBlancaHeadId).OrderBy(l => l.Version).ToListAsync();
        logos.Should().HaveCount(2, "la versión anterior se conserva");
        logos[0].Status.Should().Be("superseded");
        logos[0].StorageSha256.Should().Be(new string('a', 64), "integridad de la versión anterior intacta");
        logos[1].Status.Should().Be("active");
    }

    [PostgresTheory]
    [InlineData("image/svg+xml", "ck_tenant_brand_logos_content_type")]
    [InlineData("image/gif", "ck_tenant_brand_logos_content_type")]
    public async Task AC7_Formatos_fuera_del_vocabulario_son_rechazados(string contentType, string constraint)
    {
        await SeedThreeClientsAsync();

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            ctx.TenantBrandLogos.Add(NewLogo(MarcaBlancaHeadId, version: 1, sha: new string('a', 64), contentType));
            await ctx.SaveChangesAsync();
        });

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be(constraint);
    }

    [PostgresFact]
    public async Task AC7_Cabeza_CONCESION_no_puede_tener_logotipo()
    {
        await SeedThreeClientsAsync();

        var pg = await ExpectPostgresErrorAsync(async () =>
        {
            await using var ctx = NewContext();
            ctx.TenantBrandLogos.Add(NewLogo(ConcesionHeadId, version: 1, sha: new string('c', 64)));
            await ctx.SaveChangesAsync();
        });

        pg.SqlState.Should().Be(CheckViolation);
        pg.ConstraintName.Should().Be("ck_tenant_brand_logos_marca_blanca");
        pg.TableName.Should().Be("tenant_brand_logos");
    }

    // ── AC5 · rastro técnico por trigger (el old/new legible lo escribe el repositorio) ─────

    [PostgresFact]
    public async Task AC5_Toda_escritura_deja_rastro_en_audit_logs()
    {
        await SeedThreeClientsAsync();
        await AddBrandingAsync(MarcaBlancaHeadId);

        await using var connection = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM audit.audit_logs WHERE schema_name = 'admin' AND table_name = 'tenant_brandings' AND action = 'I'",
            connection);

        (await cmd.ExecuteScalarAsync()).Should().Be(1L);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────

    /// <summary>Cabeza MARCA_BLANCA (con una hija), cabeza CONCESION y cliente sin jerarquía.</summary>
    private async Task SeedThreeClientsAsync()
    {
        await using var ctx = NewContext();
        ctx.Tenants.AddRange(
            TenantSeed.New(MarcaBlancaHeadId, "IT-MB-HEAD", isGroupParent: true, parentId: null, GroupKindCodes.MarcaBlanca),
            TenantSeed.New(ConcesionHeadId, "IT-CN-HEAD", isGroupParent: true, parentId: null, GroupKindCodes.Concesion),
            TenantSeed.Lone());
        await ctx.SaveChangesAsync();

        await using var ctx2 = NewContext();
        ctx2.Tenants.Add(TenantSeed.ChildOf(MarcaBlancaHeadId));
        await ctx2.SaveChangesAsync();
    }

    private async Task AddBrandingAsync(Guid tenantId)
    {
        await using var ctx = NewContext();
        ctx.TenantBrandings.Add(new TenantBrandingEntity { TenantId = tenantId });
        await ctx.SaveChangesAsync();
    }

    private async Task AssertNoBrandingAsync(Guid tenantId)
    {
        await using var check = NewContext();
        (await check.TenantBrandings.AnyAsync(b => b.TenantId == tenantId)).Should().BeFalse();
    }

    private static TenantBrandLogoEntity NewLogo(Guid tenantId, int version, string sha, string contentType = "image/png") => new()
    {
        TenantId = tenantId,
        Version = version,
        Status = "active",
        ContentType = contentType,
        Filename = $"logo-v{version}.png",
        StoragePath = $"brand-logo/{tenantId:N}/v{version}",
        StorageSha256 = sha,
        SizeBytes = 1024,
        WidthPx = 240,
        HeightPx = 80,
    };

    private static async Task<PostgresException> ExpectPostgresErrorAsync(Func<Task> action)
    {
        var caught = await action.Should().ThrowAsync<Exception>();
        var ex = caught.Which;
        var pg = ex as PostgresException ?? ex.InnerException as PostgresException;
        pg.Should().NotBeNull($"se esperaba PostgresException (directa o como InnerException de {ex.GetType().Name})");
        return pg!;
    }
}
