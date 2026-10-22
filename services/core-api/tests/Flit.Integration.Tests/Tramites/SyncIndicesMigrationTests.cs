using Flit.Infrastructure.Migrations;
using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Integration.Tests.Postgres;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13075 (Feature #13062, Épica #12737) contra Postgres real con TODAS las migraciones: las
/// consultas de la sincronización pueden resolverse con sus índices (AC1, AC2) y los guardados que
/// tocan el trámite y varias tablas hijas en una operación no fallan por concurrencia (AC3).
/// <para>
/// Los planes se piden con <c>enable_seqscan = off</c>: con una base de pruebas casi vacía el
/// planificador preferiría recorrer la tabla aunque el índice exista. Lo que se prueba es que el
/// índice existe y sirve a esa consulta exacta, no la elección por costes con datos de producción
/// (eso lo mide la prueba de carga de la HU #13083).
/// </para>
/// </summary>
public sealed class SyncIndicesMigrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid Cliente = new("5a5a5a5a-0001-4000-8000-000000013075");
    private static readonly Guid Gestor = new("5a5a5a5a-0002-4000-8000-000000013075");
    private static readonly Guid Tramite = new("5a5a5a5a-0003-4000-8000-000000013075");
    private static readonly Guid Otro = new("5a5a5a5a-0004-4000-8000-000000013075");

    [PostgresFact]
    public async Task AC1_ElRecorridoPorVersionUsaElIndiceUnico()
    {
        await SembrarAsync();

        var plan = await PlanAsync(
            "SELECT id FROM tramites.procedure_instances WHERE sync_version > 0 ORDER BY sync_version LIMIT 500");

        plan.Should().Contain("uq_procedure_instances_sync_version").And.NotContain("Seq Scan");
    }

    [PostgresFact]
    public async Task AC1_LaVersionNoSePuedeRepetir()
    {
        await SembrarAsync();
        await SembrarTramiteAsync(Otro, "IT13075-2");

        // Se desactivan los triggers para poder forzar un duplicado: con ellos la versión la pone la secuencia.
        var duplicar = async () => await EjecutarAsync(
            "ALTER TABLE tramites.procedure_instances DISABLE TRIGGER USER; "
            + "UPDATE tramites.procedure_instances SET sync_version = (SELECT sync_version FROM tramites.procedure_instances WHERE id = @id) "
            + "WHERE id = '5a5a5a5a-0004-4000-8000-000000013075'");

        await duplicar.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23505");
    }

    [PostgresTheory]
    [InlineData(
        "SELECT 1 FROM tramites.procedure_instance_status_history h WHERE h.procedure_instance_id = @id AND h.to_status IN ('preasignacion','entregado') LIMIT 1",
        "ix_pi_status_history_radicado")]
    [InlineData(
        "SELECT max(changed_at) FROM tramites.procedure_instance_status_history WHERE procedure_instance_id = @id AND to_status = 'aprobado'",
        "ix_pi_status_history_aprobado")]
    [InlineData(
        "SELECT id FROM tramites.procedure_instance_attachments WHERE procedure_instance_id = @id AND tipo = 'factura' ORDER BY uploaded_at DESC LIMIT 1",
        "ix_pi_attachments_factura")]
    public async Task AC2_LasConsultasDeApoyoUsanSuIndice(string consulta, string indice)
    {
        await SembrarAsync();

        (await PlanAsync(consulta)).Should().Contain(indice);
    }

    /// <summary>
    /// Guardado de EF que modifica el trámite y agrega filas en cuatro tablas hijas en un solo
    /// <c>SaveChanges</c>. Se usan datos que NO disparan los triggers de denormalización del DDL 47
    /// (propietario en vez de comprador, marca en vez de placa): esos ya suben <c>row_version</c> desde
    /// antes de esta Feature y el código los esquiva recargando (<c>ConsolidadoVigenciaTracker</c>,
    /// <c>AssignPlateAsync</c>). Lo que se prueba aquí es que la propagación de sincronización no añade
    /// ese problema a ninguna otra tabla hija.
    /// <para>No se prueba un segundo guardado sin recargar: EF no relee el <c>row_version</c> que sube
    /// el trigger con el propio UPDATE del trámite, así que ese caso falla igual antes y después de la
    /// Feature y el código ya recarga cuando lo necesita.</para>
    /// </summary>
    [PostgresFact]
    public async Task AC3_EfGuardaTramiteYVariasTablasHijasJuntosSinErrorDeConcurrencia()
    {
        await SembrarAsync();
        var entidad = await EscalarAsync<Guid>("SELECT id FROM tramites.procedure_entities ORDER BY id LIMIT 1");

        await using var ctx = NewContext();
        var tramite = await ctx.ProcedureInstances.SingleAsync(p => p.Id == Tramite);
        var rowVersionAntes = tramite.RowVersion;
        tramite.Prioritario = true;
        ctx.ProcedureInstanceActors.Add(new ProcedureInstanceActor
        {
            Id = Guid.CreateVersion7(),
            TenantId = Cliente,
            ProcedureInstanceId = Tramite,
            ProcedureEntityId = entidad,
            ActorType = "propietario",
            DocumentType = "CC",
            DocumentNumber = "1000000000",
            FullName = "PERSONA EJEMPLO",
        });
        ctx.ProcedureInstanceFieldValues.Add(new ProcedureInstanceFieldValue
        {
            Id = Guid.CreateVersion7(),
            TenantId = Cliente,
            ProcedureInstanceId = Tramite,
            FieldKey = "vehicle_brand",
            ValueText = "MARCA EJEMPLO",
        });
        ctx.ProcedureInstanceStatusHistories.Add(new ProcedureInstanceStatusHistory
        {
            Id = Guid.CreateVersion7(),
            TenantId = Cliente,
            ProcedureInstanceId = Tramite,
            ToStatus = TramiteEstado.Borrador,
            ChangedAt = DateTimeOffset.UtcNow,
        });

        var guardar = async () => await ctx.SaveChangesAsync();
        await guardar.Should().NotThrowAsync<DbUpdateConcurrencyException>();

        // row_version sube UNA vez: por el UPDATE del propio trámite. Las tres filas hijas del mismo
        // guardado no lo mueven (la propagación solo toca columnas sync_*).
        (await EscalarAsync<long>($"SELECT row_version FROM tramites.procedure_instances WHERE id = '{Tramite}'"))
            .Should().Be(rowVersionAntes + 1);
    }

    [PostgresFact]
    public async Task ReaplicarElUp_NoProduceError()
    {
        var up = new HU13075_SyncIndices().UpOperations.OfType<SqlOperation>().Single().Sql;
        await EjecutarAsync(up);
        await EjecutarAsync(up);
    }

    // ── Siembra y utilidades ────────────────────────────────────────────────

    private async Task SembrarAsync()
    {
        await using (var ctx = NewContext())
        {
            ctx.Tenants.Add(TenantSeed.New(Cliente, "IT-13075", isGroupParent: false, parentId: null));
            await ctx.SaveChangesAsync();
            ctx.Users.Add(new User
            {
                Id = Gestor,
                Email = "it-13075@flit.test",
                DisplayName = "Gestor 13075",
                Status = "active",
                HomeTenantId = Cliente,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            });
            await ctx.SaveChangesAsync();
        }

        await SembrarTramiteAsync(Tramite, "IT13075-1");

        await EjecutarAsync("ANALYZE tramites.procedure_instances, tramites.procedure_instance_status_history, tramites.procedure_instance_attachments");
    }

    private async Task SembrarTramiteAsync(Guid id, string referencia)
    {
        await using var ctx = NewContext();
        var tipo = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync();
        ctx.ProcedureInstances.Add(new ProcedureInstance
        {
            Id = id,
            TenantId = Cliente,
            ProcedureTypeId = tipo,
            ReferenceNumber = referencia,
            Status = TramiteEstado.Borrador,
            CreatedByUserId = Gestor,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<string> PlanAsync(string consulta)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using (var off = new NpgsqlCommand("SET enable_seqscan = off", conn))
            await off.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        await using var cmd = new NpgsqlCommand("EXPLAIN " + consulta, conn);
        cmd.Parameters.AddWithValue("id", Tramite);
        var lineas = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await r.ReadAsync(TestContext.Current.CancellationToken))
            lineas.Add(r.GetString(0));
        return string.Join('\n', lineas);
    }

    private async Task EjecutarAsync(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        if (sql.Contains("@id", StringComparison.Ordinal)) cmd.Parameters.AddWithValue("id", Tramite);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task<T> EscalarAsync<T>(string sql)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (T)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
