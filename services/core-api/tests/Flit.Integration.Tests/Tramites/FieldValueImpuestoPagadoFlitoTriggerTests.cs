using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13264 AC4 (Feature #13261, Épica #12741) y Bug #14625 — el trigger <c>tramites.trg_field_value_immutable</c>
/// (DDL 130, ampliado por el 203) deja escribir <c>impuesto_departamental_pagado</c> en <c>preasignacion</c>, <c>asignado</c>
/// y <c>rechazado</c> con subsanación. En los demás estados, salvo anulado y revocado, la admite solo con
/// <c>source = flito</c>. Sigue rechazando con <c>check_violation</c> cualquier otra llave de <c>preasignacion</c>.
/// <para>Uso de ejemplo: con la instancia en <c>preasignacion</c>, <c>INSERT … 'impuesto_departamental_pagado'</c> pasa y
/// <c>INSERT … 'soat_pagado'</c> falla con 23514.</para>
/// </summary>
public sealed class FieldValueImpuestoPagadoFlitoTriggerTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string CheckViolation = "23514";
    private const string Clave = "impuesto_departamental_pagado";
    private static readonly Guid Compania = new("5a5a5a5a-0001-4000-8000-000000013264");
    private static readonly Guid Instancia = new("5a5a5a5a-0002-4000-8000-000000013264");
    private static readonly Guid Gestor = new("5a5a5a5a-0003-4000-8000-000000013264");

    [PostgresFact]
    public async Task AC4_Preasignacion_AdmiteLaMarcaDeImpuesto_InsertYUpdate()
    {
        await SembrarAsync("preasignacion");
        await using var conn = await Fixture.OpenConnectionAsync();

        await EjecutarAsync(conn, "INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text, source) VALUES (@tenant, @id, @clave, 'true', 'flito')", Clave);
        await EjecutarAsync(conn, "UPDATE tramites.procedure_instance_field_values SET value_text = 'false', source = 'user' WHERE procedure_instance_id = @id AND field_key = @clave", Clave);
    }

    [PostgresFact]
    public async Task AC4_Preasignacion_SigueRechazandoCualquierOtraLlave()
    {
        await SembrarAsync("preasignacion");
        await using var conn = await Fixture.OpenConnectionAsync();

        foreach (var otra in new[] { "soat_pagado", "soat_estado", "soat_vencimiento", "vin" })
        {
            var rechazo = async () => await EjecutarAsync(conn, "INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text, source) VALUES (@tenant, @id, @clave, 'true', 'flito')", otra);
            (await rechazo.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation, otra);
        }
    }

    [PostgresTheory]
    [InlineData("asignado", false)]
    [InlineData("rechazado", true)]
    public async Task AC4_LosOtrosEstadosEditablesLaSiguenAdmitiendo(string estado, bool subsanacion)
    {
        await SembrarAsync(estado, subsanacion);
        await using var conn = await Fixture.OpenConnectionAsync();

        await EjecutarAsync(conn, "INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text, source) VALUES (@tenant, @id, @clave, 'true', 'flito')", Clave);
    }

    /// <summary>
    /// Bug #14625 (DDL 203) — en los estados cerrados o no editables la marca se admite solo con <c>source = flito</c>.
    /// Con otra fuente sigue el <c>check_violation</c>, así que el gestor no gana ninguna edición.
    /// </summary>
    [PostgresTheory]
    [InlineData("entregado", false)]
    [InlineData("aprobado", false)]
    [InlineData("preparado", false)]
    [InlineData("rechazado", false)]
    public async Task BUG14625_EnEstadosNoEditablesSoloAdmiteLaMarcaConSourceFlito(string estado, bool subsanacion)
    {
        await SembrarAsync(estado, subsanacion);
        await using var conn = await Fixture.OpenConnectionAsync();

        var delGestor = async () => await EjecutarAsync(conn, "INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text, source) VALUES (@tenant, @id, @clave, 'true', 'user')", Clave);
        (await delGestor.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation);

        await EjecutarAsync(conn, "INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text, source) VALUES (@tenant, @id, @clave, 'true', 'flito')", Clave);
        await EjecutarAsync(conn, "UPDATE tramites.procedure_instance_field_values SET value_text = 'true', source = 'flito', updated_at = now() WHERE procedure_instance_id = @id AND field_key = @clave", Clave);

        var borrar = async () => await EjecutarAsync(conn, "DELETE FROM tramites.procedure_instance_field_values WHERE procedure_instance_id = @id AND field_key = @clave", Clave);
        (await borrar.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation, "solo INSERT/UPDATE de FLITO");
    }

    [PostgresTheory]
    [InlineData("anulado")]
    [InlineData("revocado")]
    public async Task BUG14625_AnuladoYRevocadoRechazanLaMarcaAunConSourceFlito(string estado)
    {
        await SembrarAsync(estado);
        await using var conn = await Fixture.OpenConnectionAsync();

        var rechazo = async () => await EjecutarAsync(conn, "INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text, source) VALUES (@tenant, @id, @clave, 'true', 'flito')", Clave);
        (await rechazo.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(CheckViolation);
    }

    private async Task SembrarAsync(string estado, bool subsanacion = false)
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Compania, "IT-13264-TRG", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13264-trg@flit.test",
                DisplayName = "Gestor sintético 13264",
                Status = "active",
                HomeTenantId = Compania,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var conn = await Fixture.OpenConnectionAsync();
        foreach (var sql in new[]
                 {
                     """
                     INSERT INTO tramites.procedure_instances
                         (id, tenant_id, procedure_type_id, reference_number, status, vin, created_by_user_id, created_at)
                     VALUES (@id, @tenant, (SELECT id FROM tramites.procedure_types ORDER BY code LIMIT 1),
                             'IT-13264', 'borrador', 'SINTVIN13264', @gestor, now())
                     """,
                     "INSERT INTO tramites.procedure_instance_status_history (tenant_id, procedure_instance_id, to_status) VALUES (@tenant, @id, @estado)",
                     "UPDATE tramites.procedure_instances SET status = @estado, subsanacion_activa = @sub WHERE id = @id",
                 })
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", Instancia);
            cmd.Parameters.AddWithValue("tenant", Compania);
            cmd.Parameters.AddWithValue("estado", estado);
            cmd.Parameters.AddWithValue("gestor", Gestor);
            cmd.Parameters.AddWithValue("sub", subsanacion);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }

    private static async Task EjecutarAsync(NpgsqlConnection conn, string sql, string clave)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("tenant", Compania);
        cmd.Parameters.AddWithValue("id", Instancia);
        cmd.Parameters.AddWithValue("clave", clave);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
