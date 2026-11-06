using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #14317 (Feature #14315, Épica #14296) AC1 a AC4, contra PostgreSQL real: <c>tramites.procedure_instances.channel</c> y
/// <c>channel_transit_office_id</c> (DDL 169) aceptan el catálogo de cinco códigos, rechazan lo que no pertenece a él, exigen
/// el OT si y solo si el canal es <c>pn_punto</c> y dejan en NULL a los trámites anteriores.
/// <para>Uso de ejemplo: <c>InsertarAsync(id, "pn_punto", OficinaId)</c> pasa; <c>InsertarAsync(id, "pn_web", OficinaId)</c> viola
/// <c>ck_procedure_instances_channel_office</c> (23514).</para>
/// </summary>
public sealed class CanalTramiteSchemaTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string CheckViolation = "23514";
    private const string ForeignKeyViolation = "23503";
    private static readonly Guid Compania = new("5a5a5a5a-0001-4000-8000-000000014317");
    private static readonly Guid Gestor = new("5a5a5a5a-0003-4000-8000-000000014317");
    private static readonly Guid Oficina = new("5a5a5a5a-0004-4000-8000-000000014317");

    [PostgresTheory]
    [InlineData("tramites", false)]
    [InlineData("pn_web", false)]
    [InlineData("pn_punto", true)]
    [InlineData("integracion", false)]
    [InlineData("aplicacion", false)]
    public async Task AC1_LaBaseAceptaLosCincoCanalesDelCatalogo(string canal, bool conOficina)
    {
        await SembrarBaseAsync();
        var id = Guid.NewGuid();

        await InsertarAsync(id, canal, conOficina ? Oficina : null);

        var (canalGuardado, oficinaGuardada) = await LeerAsync(id);
        canalGuardado.Should().Be(canal);
        oficinaGuardada.Should().Be(conOficina ? Oficina : null);
    }

    [PostgresTheory]
    [InlineData("web")]
    [InlineData("TRAMITES")]
    [InlineData("")]
    public async Task AC2_LaBaseRechazaUnCanalFueraDelCatalogo(string canal)
    {
        await SembrarBaseAsync();

        var insertar = async () => await InsertarAsync(Guid.NewGuid(), canal, null);

        (await insertar.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation);
    }

    [PostgresTheory]
    [InlineData("tramites")]
    [InlineData("pn_web")]
    [InlineData("integracion")]
    [InlineData("aplicacion")]
    public async Task AC3_LaBaseRechazaUnOtDelCanalConCanalDistintoDePnPunto(string canal)
    {
        await SembrarBaseAsync();

        var insertar = async () => await InsertarAsync(Guid.NewGuid(), canal, Oficina);

        (await insertar.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation);
    }

    [PostgresFact]
    public async Task AC3_LaBaseRechazaUnOtDelCanalSinCanal()
    {
        await SembrarBaseAsync();

        var insertar = async () => await InsertarAsync(Guid.NewGuid(), null, Oficina);

        (await insertar.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation);
    }

    [PostgresFact]
    public async Task AC3_LaBaseRechazaPnPuntoSinOt()
    {
        await SembrarBaseAsync();

        var insertar = async () => await InsertarAsync(Guid.NewGuid(), "pn_punto", null);

        (await insertar.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation);
    }

    [PostgresFact]
    public async Task AC3_ElOtDelCanalDebeSerUnOrganismoExistente()
    {
        await SembrarBaseAsync();

        var insertar = async () => await InsertarAsync(Guid.NewGuid(), "pn_punto", Guid.NewGuid());

        (await insertar.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(ForeignKeyViolation);
    }

    [PostgresFact]
    public async Task AC4_UnTramiteCreadoSinCanalConservaSusDatosYQuedaConNull()
    {
        await SembrarBaseAsync();
        var id = Guid.NewGuid();

        // Un trámite «anterior al cambio»: el INSERT no menciona las columnas nuevas.
        await using (var conn = await Fixture.OpenConnectionAsync())
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO tramites.procedure_instances
                    (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
                VALUES (@id, @tenant, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1),
                        @ref, 'borrador', 'SINTVIN14317', @gestor, now())
                """, conn);
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("tenant", Compania);
            cmd.Parameters.AddWithValue("ref", "IT-14317-" + id.ToString("N")[..8]);
            cmd.Parameters.AddWithValue("gestor", Gestor);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var (canal, oficina) = await LeerAsync(id);
        canal.Should().BeNull();
        oficina.Should().BeNull();

        await using var conn2 = await Fixture.OpenConnectionAsync();
        await using var leer = new NpgsqlCommand(
            "SELECT status, vin FROM tramites.procedure_instances WHERE id = @id", conn2);
        leer.Parameters.AddWithValue("id", id);
        await using var reader = await leer.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.GetString(0).Should().Be("borrador");
        reader.GetString(1).Should().Be("SINTVIN14317");
    }

    [PostgresFact]
    public async Task AC4_LasColumnasSonNulablesSinDefault()
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT column_name, is_nullable, column_default
              FROM information_schema.columns
             WHERE table_schema = 'tramites' AND table_name = 'procedure_instances'
               AND column_name IN ('channel', 'channel_transit_office_id')
             ORDER BY column_name
            """, conn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var filas = new List<(string Nombre, string Nulable, bool SinDefault)>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            filas.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2)));

        filas.Should().Equal(("channel", "YES", true), ("channel_transit_office_id", "YES", true));
    }

    [PostgresFact]
    public async Task Idempotencia_ReaplicarElUpNoProduceError()
    {
        var up = new Flit.Infrastructure.Migrations.HU14317_CanalTramite()
            .UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Single().Sql;
        await using var conn = await Fixture.OpenConnectionAsync();

        for (var i = 0; i < 2; i++)
        {
            await using var cmd = new NpgsqlCommand(up, conn);
            var reaplicar = async () => await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            await reaplicar.Should().NotThrowAsync();
        }
    }

    // ── datos ────────────────────────────────────────────────────────────────

    private async Task SembrarBaseAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Compania, "IT-14317", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-14317@flit.test",
                DisplayName = "Gestor sintético 14317",
                Status = "active",
                HomeTenantId = Compania,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO catalogs.transit_offices (id, code, name, department_code, city_code)
            VALUES (@id, 'IT14317', 'Organismo sintético 14317', '11', '11001')
            """, conn);
        cmd.Parameters.AddWithValue("id", Oficina);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task InsertarAsync(Guid id, string? canal, Guid? oficina)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO tramites.procedure_instances
                (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at,
                 channel, channel_transit_office_id)
            VALUES (@id, @tenant, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1),
                    @ref, 'borrador', 'SINTVIN14317', @gestor, now(), @canal, @oficina)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("tenant", Compania);
        cmd.Parameters.AddWithValue("ref", "IT-14317-" + id.ToString("N")[..8]);
        cmd.Parameters.AddWithValue("gestor", Gestor);
        cmd.Parameters.AddWithValue("canal", (object?)canal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("oficina", (object?)oficina ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<(string? Canal, Guid? Oficina)> LeerAsync(Guid id)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT channel, channel_transit_office_id FROM tramites.procedure_instances WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }
}
