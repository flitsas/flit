using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Npgsql;

namespace Flit.Integration.Tests.Security;

/// <summary>
/// HU #13440 (Feature #13437) — el DDL 134 contra PostgreSQL real, aplicado por la migración
/// <c>20261008140000_HU13440_RolesPorTenant</c> sobre la base efímera del arnés: unicidad por tenant, trigger de code
/// global, FK y policy RLS (AC1 a AC4 de HU-B1). Nunca toca <c>flit_v2_local</c>.
/// <para>
/// La policy se ejercita con un rol NO owner creado (y borrado) aquí: la conexión del arnés es dueña de las tablas y
/// por eso bypassa la RLS, igual que la app en los ambientes (ver DDL 134).
/// </para>
/// </summary>
public sealed class RolesPorTenantPostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid TenantA = new("aaaaaaaa-1340-4000-8000-00000000000a");
    private static readonly Guid TenantB = new("bbbbbbbb-1340-4000-8000-00000000000b");

    private async Task<NpgsqlConnection> SeedAsync()
    {
        var cn = await Fixture.OpenConnectionAsync();
        await Exec(cn, """
            INSERT INTO platform.products (code, name, icon) VALUES
              ('plataforma','Plataforma','layout'),('tramites','Tramites','file'),
              ('comparendos','Comparendos','file'),('diagnostico','Diagnostico','file')
            ON CONFLICT (code) DO NOTHING;
            INSERT INTO identity.tenants (id, code, legal_name, tax_id, tenant_type, is_active, is_group_parent, created_at)
              VALUES ('aaaaaaaa-1340-4000-8000-00000000000a','IT-ROLES-A','A','9134000000001','CONCESIONARIO',true,false,now()),
                     ('bbbbbbbb-1340-4000-8000-00000000000b','IT-ROLES-B','B','9134000000002','CONCESIONARIO',true,false,now());
            INSERT INTO security.roles (code, name, target_entity_type, is_system)
              VALUES ('AdminCompany','Admin','COMPANY',true), ('ot_admin','OT','TRANSIT_OFFICE',true);
            """);
        return cn;
    }

    private static async Task Exec(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> Scalar(NpgsqlConnection cn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<PostgresException> Rejected(NpgsqlConnection cn, string sql)
    {
        var act = async () => await Exec(cn, sql);
        return (await act.Should().ThrowAsync<PostgresException>()).Which;
    }

    private static string Insert(string code, Guid? tenant, string target = "COMPANY") =>
        $"INSERT INTO security.roles (code, name, target_entity_type, tenant_id) VALUES ('{code}', '{code}', '{target}', "
        + (tenant is null ? "NULL" : $"'{tenant}'") + ")";

    // AC1
    [PostgresFact]
    public async Task AC1_tenant_id_es_nullable_con_FK_y_los_roles_existentes_quedan_en_NULL()
    {
        await using var cn = await SeedAsync();

        (await Scalar(cn, """
            SELECT count(*) FROM information_schema.columns
             WHERE table_schema = 'security' AND table_name = 'roles' AND column_name = 'tenant_id' AND is_nullable = 'YES'
            """)).Should().Be(1);
        (await Scalar(cn, "SELECT count(*) FROM pg_constraint WHERE conname = 'fk_roles_tenants'")).Should().Be(1);
        (await Scalar(cn, "SELECT count(*) FROM security.roles WHERE tenant_id IS NOT NULL")).Should().Be(0);
    }

    // AC2
    [PostgresFact]
    public async Task AC2_dos_companias_aceptan_el_mismo_code_y_el_duplicado_en_el_mismo_tenant_se_rechaza()
    {
        await using var cn = await SeedAsync();

        await Exec(cn, Insert("contador", TenantA));
        await Exec(cn, Insert("contador", TenantB));
        (await Scalar(cn, "SELECT count(*) FROM security.roles WHERE code = 'contador'")).Should().Be(2);

        var dup = await Rejected(cn, Insert("contador", TenantA));
        dup.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        dup.ConstraintName.Should().Be("uq_roles_tenant_code");

        var dupCase = await Rejected(cn, Insert("CONTADOR", TenantA));
        dupCase.ConstraintName.Should().Be("uq_roles_tenant_code", "la unicidad es case-insensitive");
    }

    [PostgresFact]
    public async Task AC2_un_tenant_no_puede_repetir_el_code_de_un_rol_global_en_ningun_tipo_ni_con_otras_mayusculas()
    {
        await using var cn = await SeedAsync();

        foreach (var sql in new[]
                 {
                     Insert("AdminCompany", TenantA),
                     Insert("admincompany", TenantA),
                     Insert("ot_admin", TenantA, "COMPANY"), // el global es TRANSIT_OFFICE: igual se rechaza
                 })
        {
            var ex = await Rejected(cn, sql);
            ex.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
            ex.MessageText.Should().StartWith("ROLE_CODE_DUPLICATE");
        }
    }

    [PostgresFact]
    public async Task AC2_admin_de_producto_queda_reservado_aunque_no_exista_la_fila_global()
    {
        await using var cn = await SeedAsync();

        foreach (var code in new[] { "admin_tramites", "Admin_Comparendos", "ADMIN_DIAGNOSTICO" })
        {
            var ex = await Rejected(cn, Insert(code, TenantA));
            ex.MessageText.Should().Contain("reservado");
        }

        await Exec(cn, Insert("admin_ventas", TenantA)); // no es un producto: se permite
    }

    [PostgresFact]
    public async Task AC2_un_rol_global_nuevo_no_puede_pisar_el_code_de_un_rol_de_tenant()
    {
        await using var cn = await SeedAsync();
        await Exec(cn, Insert("contador", TenantA));

        var ex = await Rejected(cn, Insert("Contador", null));

        ex.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        ex.MessageText.Should().StartWith("ROLE_CODE_DUPLICATE");
    }

    [PostgresFact]
    public async Task AC2_eliminar_un_rol_libera_su_code_y_el_FK_rechaza_un_tenant_inexistente()
    {
        await using var cn = await SeedAsync();
        await Exec(cn, Insert("contador", TenantA));
        await Exec(cn, "UPDATE security.roles SET deleted_at = now() WHERE code = 'contador'");

        await Exec(cn, Insert("contador", TenantA));

        var fk = await Rejected(cn, Insert("x", Guid.Parse("99999999-9999-4999-8999-999999999999")));
        fk.SqlState.Should().Be(PostgresErrorCodes.ForeignKeyViolation);
        fk.ConstraintName.Should().Be("fk_roles_tenants");
    }

    // AC3 y AC4
    [PostgresFact]
    public async Task AC3_AC4_la_policy_muestra_globales_y_propios_al_tenant_y_todo_al_SuperAdmin()
    {
        await using var cn = await SeedAsync();
        await Exec(cn, Insert("contador", TenantA));
        await Exec(cn, Insert("auditor", TenantB));

        var probe = "flit_it_rls_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await Exec(cn, $"CREATE ROLE {probe}; GRANT USAGE ON SCHEMA security TO {probe}; GRANT SELECT ON security.roles TO {probe};");
            await Exec(cn, $"SET ROLE {probe}");

            await Exec(cn, $"SELECT set_config('app.current_tenant_id', '{TenantA}', false)");
            (await Scalar(cn, "SELECT count(*) FROM security.roles WHERE code = 'contador'")).Should().Be(1, "ve el propio");
            (await Scalar(cn, "SELECT count(*) FROM security.roles WHERE code = 'auditor'")).Should().Be(0, "nunca el de otro tenant");
            (await Scalar(cn, "SELECT count(*) FROM security.roles WHERE tenant_id IS NULL")).Should().Be(2, "ve los globales");

            await Exec(cn, $"SELECT set_config('app.current_tenant_id', '{TenantB}', false)");
            (await Scalar(cn, "SELECT count(*) FROM security.roles WHERE code = 'contador'")).Should().Be(0);

            await Exec(cn, "SELECT set_config('app.is_superadmin', 'true', false)");
            (await Scalar(cn, "SELECT count(*) FROM security.roles")).Should().Be(4, "SuperAdmin ve globales y de cualquier tenant");
        }
        finally
        {
            await Exec(cn, "RESET ROLE; RESET app.current_tenant_id; RESET app.is_superadmin");
            await Exec(cn, $"DROP OWNED BY {probe}; DROP ROLE IF EXISTS {probe};");
        }
    }

    [PostgresFact]
    public async Task El_Down_borra_invitaciones_y_asignaciones_de_roles_de_tenant_sin_romper_la_FK()
    {
        // La migración se puede revertir en dev: el Down no puede fallar por user_invitations.role_id (FK RESTRICT).
        await using var cn = await SeedAsync();
        await Exec(cn, Insert("contador", TenantA));
        await Exec(cn, """
            INSERT INTO identity.users (id, email, display_name, status, created_at)
              VALUES ('cccccccc-1340-4000-8000-00000000000c','inv@it.local','Inv','active',now()) ON CONFLICT DO NOTHING;
            """);
        await Exec(cn, """
            INSERT INTO security.user_invitations (tenant_id, email, role_id, token_hash, status, invited_by)
              SELECT 'aaaaaaaa-1340-4000-8000-00000000000a', 'p@it.local', id, 'hash-down-1', 'pending',
                     'cccccccc-1340-4000-8000-00000000000c'
                FROM security.roles WHERE code = 'contador';
            """);

        var down = typeof(Flit.Infrastructure.Migrations.HU13440_RolesPorTenant)
            .GetMethod("Down", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var builder = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        down.Invoke(new Flit.Infrastructure.Migrations.HU13440_RolesPorTenant(), [builder]);
        var sql = string.Join("\n", builder.Operations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Select(o => o.Sql));

        // En una transacción que se revierte: la base efímera es compartida por todas las pruebas de la colección y
        // el reset no restaura el esquema, así que el Down no puede quedar aplicado.
        await Exec(cn, "BEGIN");
        try
        {
            await Exec(cn, sql);

            (await Scalar(cn, "SELECT count(*) FROM security.user_invitations WHERE token_hash = 'hash-down-1'")).Should().Be(0);
            (await Scalar(cn, "SELECT count(*) FROM information_schema.columns WHERE table_schema='security' AND table_name='roles' AND column_name='tenant_id'"))
                .Should().Be(0, "el Down quitó la columna");
        }
        finally
        {
            await Exec(cn, "ROLLBACK");
        }

        (await Scalar(cn, "SELECT count(*) FROM information_schema.columns WHERE table_schema='security' AND table_name='roles' AND column_name='tenant_id'"))
            .Should().Be(1, "el rollback dejó el esquema como estaba");
    }
}
