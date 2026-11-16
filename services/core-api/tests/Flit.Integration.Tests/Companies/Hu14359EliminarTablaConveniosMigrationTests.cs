using Flit.Admin.Application.Auditing;
using Flit.Admin.Application.Companies.MandateSigners.CompanyMandateSigners;
using Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.OtRules;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Integration.Tests.TransitOffices;
using Flit.Tramites.Application.Documents;
using Flit.Tramites.Domain.Documents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Companies;

/// <summary>
/// HU #14359 (Feature #14356) — eliminar <c>admin.company_transit_office_agreements</c>, contra PostgreSQL real.
/// El arnés ya aplicó todas las migraciones en orden sobre una base nueva, así que el Down se ejecuta dentro de la
/// prueba y un <c>finally</c> vuelve a aplicar el Up: la colección comparte la base y otra prueba no debe heredar la tabla.
/// <para>La migración se busca por nombre para que sus pruebas fallen con su motivo real antes de existir.</para>
/// <para>Uso de ejemplo: <c>await EjecutarAsync(conn, Migracion().DownOperations)</c> recrea la tabla vacía.</para>
/// </summary>
public sealed class Hu14359EliminarTablaConveniosMigrationTests(PostgresDatabaseFixture fixture)
    : PostgresTestBase(fixture)
{
    private const string MigrationId = "20261105120000_HU14359_EliminarTablaConvenios";
    private const string ClaseMigracion = "Flit.Infrastructure.Migrations.HU14359_EliminarTablaConvenios";

    // ── AC1: la tabla y los convenios desaparecen ───────────────────────────────────────────
    [PostgresFact]
    public async Task AC1_TrasMigrar_LaTablaNoExiste()
    {
        await using var conn = await Fixture.OpenConnectionAsync();

        (await ExisteTablaAsync(conn)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task AC1_ConConveniosEnLaTabla_ElUp_NoDejaNinguno()
    {
        var migracion = Migracion();
        await HierarchyScenario.SeedAsync(Fixture);
        await using var conn = await Fixture.OpenConnectionAsync();
        try
        {
            await EjecutarAsync(conn, migracion.DownOperations);
            await ComandoAsync(conn,
                $"""
                INSERT INTO admin.company_transit_office_agreements (company_tenant_id, transit_office_id)
                VALUES ('{HierarchyScenario.C1}', '{HierarchyScenario.Ot1}')
                """);

            await EjecutarAsync(conn, migracion.UpOperations);

            (await ExisteTablaAsync(conn)).Should().BeFalse();
        }
        finally
        {
            await EjecutarAsync(conn, migracion.UpOperations);
        }
    }

    // ── AC2: base donde ya no existe y base nueva en orden ──────────────────────────────────
    [PostgresFact]
    public async Task AC2_BaseNueva_AplicaTodasLasMigraciones_YRegistraEstaEnElHistorial()
    {
        await using var conn = await Fixture.OpenConnectionAsync();

        var registrada = await EscalarAsync(conn,
            $"""SELECT count(*) FROM "__EFMigrationsHistory" WHERE migration_id = '{MigrationId}'""");

        registrada.Should().Be(1L, "sin [DbContext] y [Migration] EF no la descubre y nunca se aplicaría");
        (await ExisteTablaAsync(conn)).Should().BeFalse();
    }

    [PostgresFact]
    public async Task AC2_SinLaTabla_ReaplicarElUp_TerminaSinError()
    {
        var migracion = Migracion();
        await using var conn = await Fixture.OpenConnectionAsync();

        var reaplicar = async () => await EjecutarAsync(conn, migracion.UpOperations);

        await reaplicar.Should().NotThrowAsync();
        (await ExisteTablaAsync(conn)).Should().BeFalse();
    }

    // ── AC3: Down recrea según el DDL original, sin filas ───────────────────────────────────
    [PostgresFact]
    public async Task AC3_ElDown_RecreaLaTablaYSusIndices_SegunElDdlOriginal_SinFilas()
    {
        var migracion = Migracion();
        await using var conn = await Fixture.OpenConnectionAsync();
        try
        {
            await EjecutarAsync(conn, migracion.DownOperations);

            (await EscalarAsync(conn, "SELECT count(*) FROM admin.company_transit_office_agreements")).Should().Be(0L);
            (await ListaAsync(conn,
                """
                SELECT column_name || ' ' || data_type || ' ' || is_nullable
                  FROM information_schema.columns
                 WHERE table_schema = 'admin' AND table_name = 'company_transit_office_agreements'
                 ORDER BY ordinal_position
                """)).Should().Equal(
                "id uuid NO",
                "company_tenant_id uuid NO",
                "transit_office_id uuid NO",
                "is_active boolean NO",
                "created_at timestamp with time zone NO",
                "created_by uuid YES",
                "updated_at timestamp with time zone YES",
                "updated_by uuid YES");
            (await ListaAsync(conn,
                """
                SELECT indexdef FROM pg_indexes
                 WHERE schemaname = 'admin' AND tablename = 'company_transit_office_agreements'
                 ORDER BY indexname
                """)).Should().Equal(
                "CREATE INDEX ix_company_transit_office_agreements_office ON admin.company_transit_office_agreements USING btree (transit_office_id, is_active)",
                "CREATE UNIQUE INDEX pk_company_transit_office_agreements ON admin.company_transit_office_agreements USING btree (id)",
                "CREATE UNIQUE INDEX uq_company_transit_office_agreements ON admin.company_transit_office_agreements USING btree (company_tenant_id, transit_office_id)");
            (await ListaAsync(conn,
                """
                SELECT conname || ': ' || pg_get_constraintdef(oid) FROM pg_constraint
                 WHERE conrelid = 'admin.company_transit_office_agreements'::regclass AND contype = 'f'
                """)).Should().Equal(
                "fk_ctoa_tenant: FOREIGN KEY (company_tenant_id) REFERENCES identity.tenants(id) ON UPDATE CASCADE ON DELETE CASCADE");
        }
        finally
        {
            await EjecutarAsync(conn, migracion.UpOperations);
        }
    }

    [PostgresFact]
    public async Task AC3_UpYDown_ConservanLosValoresDeSignsPhysically()
    {
        var migracion = Migracion();
        var signerId = await AltaMandatarioConFirmaFisicaEnOt2Async();
        await using var conn = await Fixture.OpenConnectionAsync();
        var consulta =
            $"""
            SELECT transit_office_id::text || '=' || signs_physically::text
              FROM admin.mandate_signer_transit_offices
             WHERE mandate_signer_id = '{signerId}'
             ORDER BY transit_office_id
            """;
        var antes = await ListaAsync(conn, consulta);
        antes.Should().Contain($"{HierarchyScenario.Ot2}=true").And.Contain($"{HierarchyScenario.Ot1}=false");
        try
        {
            await EjecutarAsync(conn, migracion.DownOperations);
            (await ListaAsync(conn, consulta)).Should().Equal(antes, "el DDL 52 usa ADD COLUMN IF NOT EXISTS");

            await EjecutarAsync(conn, migracion.UpOperations);
            (await ListaAsync(conn, consulta)).Should().Equal(antes);
        }
        finally
        {
            await EjecutarAsync(conn, migracion.UpOperations);
        }
    }

    // ── AC5: caracterización, la configuración del recuadro se lee sin la tabla ─────────────
    [PostgresTheory]
    [InlineData(MandatoAssignmentModeCodes.Signer, true, MandatarioFirmaModo.Estampada)]
    [InlineData(MandatoAssignmentModeCodes.Signer, false, MandatarioFirmaModo.Manual)]
    [InlineData(MandatoAssignmentModeCodes.Open, true, MandatarioFirmaModo.Manual)]
    [InlineData(MandatoAssignmentModeCodes.Institutional, true, MandatarioFirmaModo.SinBloque)]
    public async Task AC5_Caracterizacion_ElModoDeFirma_SaleDeLaConfiguracionDelOrganismo(
        string modo, bool conEstampa, MandatarioFirmaModo esperado)
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        ctx.TransitOfficeMandateConfigs.Add(new TransitOfficeMandateConfigEntity
        {
            Id = Guid.NewGuid(),
            TransitOfficeId = HierarchyScenario.Ot1,
            AssignmentMode = modo,
        });
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        var config = await new MandateRequirementPolicy(ctx)
            .ResolveByOfficeIdAsync(HierarchyScenario.Ot1, HierarchyScenario.C1, TestContext.Current.CancellationToken);

        config.Should().NotBeNull();
        MandatoFirmaModoResolver.Resolve(config!.AssignmentMode, conEstampa).Should().Be(esperado);
    }

    // ── AC6: caracterización, lo que comparte pantalla con el convenio sigue igual ──────────
    [PostgresFact]
    public async Task AC6_Caracterizacion_MandatarioPorOrganismoYFirmaFisica_SeLeenIgual()
    {
        var signerId = await AltaMandatarioConFirmaFisicaEnOt2Async();
        await using var ctx = NewContext();

        var mandatario = await new DbMandateSignerReader(ctx).GetByIdAsync(signerId);

        mandatario.Should().NotBeNull();
        mandatario!.TransitOfficeIds.Should().BeEquivalentTo([HierarchyScenario.Ot1, HierarchyScenario.Ot2]);
        mandatario.PhysicalSignatureOfficeIds.Should().Equal([HierarchyScenario.Ot2]);
    }

    [PostgresFact]
    public async Task AC6_Caracterizacion_HabilitarYRetirarOrganismo_FuncionanIgual()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var grants = new TransitGrantRepository(ctx, NullAuditContextAccessor.Instance);
        (await grants.ListEnabledOfficeIdsAsync(HierarchyScenario.S)).Should().NotContain(HierarchyScenario.Ot1);

        (await grants.AddGrantAsync(HierarchyScenario.S, HierarchyScenario.Ot1, null, null)).Should().BeTrue();
        (await grants.ListEnabledOfficeIdsAsync(HierarchyScenario.S)).Should().Contain(HierarchyScenario.Ot1);

        (await grants.RemoveGrantAsync(HierarchyScenario.S, HierarchyScenario.Ot1, null, null)).Should().BeTrue();
        (await grants.ListEnabledOfficeIdsAsync(HierarchyScenario.S)).Should().NotContain(HierarchyScenario.Ot1);
    }

    [PostgresFact]
    public async Task AC6_Caracterizacion_ConfigurarConsultasDelOrganismo_FuncionaIgual()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var restricciones = new OtConsultationRestrictionRepository(ctx, NullAuditContextAccessor.Instance);

        await restricciones.SetAsync(
            HierarchyScenario.C1, HierarchyScenario.Ot1, RestrictedConsultationKinds.Rnmc, false, null, null);

        (await restricciones.ListForOfficeAsync(HierarchyScenario.C1, HierarchyScenario.Ot1)).Should().ContainSingle()
            .Which.Should().Be(new OtConsultationRestrictionItem(
                HierarchyScenario.Ot1, RestrictedConsultationKinds.Rnmc, false));
    }

    /// <summary>
    /// Alta real con Ot1 y fila del puente sembrada con firma a mano en Ot2: la firma física ya no se da de alta por
    /// el handler (HU #13131), pero las filas históricas se conservan y son las que el Down no debe tocar.
    /// </summary>
    private async Task<Guid> AltaMandatarioConFirmaFisicaEnOt2Async()
    {
        await TransitNetworkSeed.SeedMarcaBlancaNetworkAsync(Fixture);
        Guid signerId;
        await using (var ctx = NewContext())
        {
            await TransitNetworkSeed.SetHeadGrantsAsync(ctx, HierarchyScenario.P, HierarchyScenario.Ot1);
            var reader = new DbMandateSignerReader(ctx, effectiveOffices: NewResolver(ctx));
            var alta = await new CreateCompanyMandateSignerHandler(
                    reader,
                    new CreateMandateSignerHandler(
                        new DbTransitOfficeOperationalStatusReader(ctx), reader, new MandateSignerRepository(ctx)))
                .HandleAsync(
                    HierarchyScenario.C2,
                    new CompanyMandateSignerRequest(
                        "Ana Restrepo", "1020304050", [HierarchyScenario.Ot1], "CC", "ana@flit.test",
                        SignatureMethod: "biometria"),
                    null);
            alta.Errors.Should().BeEmpty();
            signerId = alta.MandateSignerId!.Value;
        }

        await using var seed = NewContext();
        seed.MandateSignerTransitOffices.Add(new MandateSignerTransitOffice
        {
            Id = Guid.NewGuid(),
            MandateSignerId = signerId,
            TransitOfficeId = HierarchyScenario.Ot2,
            IsActive = true,
            SignsPhysically = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        return signerId;
    }

    private static EffectiveTransitOfficeListResolver NewResolver(FlitDbContext ctx) =>
        new(
            new CompanyHierarchyRepository(ctx),
            new TransitGrantRepository(ctx, NullAuditContextAccessor.Instance),
            new TenantTransitOfficeBlockRepository(ctx, NullAuditContextAccessor.Instance),
            new DbTransitOfficeOperationalStatusReader(ctx),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EffectiveTransitOfficeListResolver>.Instance);

    private static Migration Migracion()
    {
        var tipo = typeof(FlitDbContext).Assembly.GetType(ClaseMigracion);
        tipo.Should().NotBeNull($"la migración {ClaseMigracion} debe existir");
        return (Migration)Activator.CreateInstance(tipo!)!;
    }

    private static async Task EjecutarAsync(NpgsqlConnection conn, IReadOnlyList<MigrationOperation> operaciones)
    {
        foreach (var sql in operaciones.OfType<SqlOperation>().Select(o => o.Sql))
        {
            await ComandoAsync(conn, sql);
        }
    }

    private static async Task ComandoAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<object?> EscalarAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> ExisteTablaAsync(NpgsqlConnection conn) =>
        await EscalarAsync(conn, "SELECT to_regclass('admin.company_transit_office_agreements') IS NOT NULL") is true;

    private static async Task<List<string>> ListaAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var filas = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            filas.Add(reader.GetString(0));
        }

        return filas;
    }
}
