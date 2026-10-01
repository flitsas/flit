using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// Bug #13194 (P3-26) — el trigger <c>tramites.trg_field_value_immutable</c> (DDL 129) deja escribir
/// <c>soat_vencimiento</c> con el trámite en <c>asignado</c> (fecha del PDF del SOAT leída por OCR, soporte
/// manual del gate de «Enviar al OT») y sigue rechazando cualquier otra llave fuera de la allowlist.
/// <para>
/// Uso de ejemplo: con la instancia en <c>asignado</c>, <c>INSERT … field_key='soat_vencimiento'</c> pasa y
/// <c>INSERT … field_key='soat_poliza'</c> falla con 23514 (check_violation).
/// </para>
/// </summary>
public sealed class FieldValueInmutabilidadSoatAsignadoTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string CheckViolation = "23514";
    private static readonly Guid Compania = new("5a5a5a5a-0001-4000-8000-000000013194");
    private static readonly Guid Instancia = new("5a5a5a5a-0002-4000-8000-000000013194");

    [PostgresFact]
    public async Task Asignado_admiteSoatVencimiento_yRechazaOtraLlave()
    {
        await SembrarAsignadoAsync();
        await using var conn = await Fixture.OpenConnectionAsync();

        await InsertarAsync(conn, "soat_vencimiento", "2027-01-31");
        await ActualizarAsync(conn, "soat_vencimiento", "2027-02-28");

        var rechazo = async () => await InsertarAsync(conn, "soat_poliza", "QA-POLIZA-1");
        (await rechazo.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation);
    }

    [PostgresFact]
    public async Task Entregado_sigueRechazandoSoatVencimiento()
    {
        await SembrarAsignadoAsync(estado: "entregado");
        await using var conn = await Fixture.OpenConnectionAsync();

        var rechazo = async () => await InsertarAsync(conn, "soat_vencimiento", "2027-01-31");
        (await rechazo.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation);
    }

    private async Task SembrarAsignadoAsync(string estado = "asignado")
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Compania, "IT-13194-SOAT", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var conn = await Fixture.OpenConnectionAsync();
        foreach (var sql in new[]
                 {
                     """
                     INSERT INTO tramites.procedure_instances
                         (id, tenant_id, procedure_type_id, reference_number, status, vin, created_at)
                     VALUES (@id, @tenant, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1),
                             'IT-13194', 'borrador', 'SINTVIN13194', now())
                     """,
                     "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @id, @estado)",
                     "UPDATE tramites.procedure_instances SET status = @estado WHERE id = @id",
                 })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", Instancia);
            cmd.Parameters.AddWithValue("tenant", Compania);
            cmd.Parameters.AddWithValue("estado", estado);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    private static async Task InsertarAsync(NpgsqlConnection conn, string clave, string valor)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text, source)
            VALUES (@tenant, @id, @clave, @valor, 'ocr')
            """, conn);
        cmd.Parameters.AddWithValue("tenant", Compania);
        cmd.Parameters.AddWithValue("id", Instancia);
        cmd.Parameters.AddWithValue("clave", clave);
        cmd.Parameters.AddWithValue("valor", valor);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task ActualizarAsync(NpgsqlConnection conn, string clave, string valor)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE tramites.procedure_instance_field_values SET value_text = @valor WHERE procedure_instance_id = @id AND field_key = @clave",
            conn);
        cmd.Parameters.AddWithValue("id", Instancia);
        cmd.Parameters.AddWithValue("clave", clave);
        cmd.Parameters.AddWithValue("valor", valor);
        (await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).Should().Be(1);
    }
}
