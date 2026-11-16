using Flit.Admin.Domain.ProcedureSnapshots;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Tramites.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #14318 (Feature #14315, Épica #14296) contra PostgreSQL real: el canal y el OT del canal llegan al mismo INSERT por los dos
/// puntos de creación (el handler de trámites y el repositorio administrativo), no cambian después (AC5), el detalle los
/// devuelve con el nombre del organismo (AC10) y el backfill (DDL 170) asigna canal a los trámites del portal e ICT ya creados
/// sin disparar los triggers de la tabla y sin cambiar nada en una segunda corrida (AC9).
/// <para>Uso de ejemplo: <c>CrearAsync(new CreateProcedureInstanceRequest(..., Channel: "pn_punto", ChannelTransitOfficeId: ot))</c>
/// deja <c>channel = 'pn_punto'</c> y <c>channel_transit_office_id = ot</c> en la fila.</para>
/// Datos sintéticos: ningún documento ni correo reales.
/// </summary>
public sealed class CanalAlCrearTramitePostgresTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private const string MarcaPunto = "[Canal: punto de atención (OT)]";

    // ── AC1 a AC4: el INSERT de la creación lleva el canal ───────────────────────────────────────

    [PostgresTheory]
    [InlineData("tramites", false)]
    [InlineData("pn_web", false)]
    [InlineData("pn_punto", true)]
    [InlineData("integracion", false)]
    public async Task AC1_AC4_ElHandlerDeCreacionGuardaElCanalYElOtDelCanalEnElMismoInsert(string canal, bool conOt)
    {
        var tipo = await TipoHabilitadoAsync();

        var id = await CrearAsync(tipo, canal, conOt ? HierarchyScenario.Ot1 : null);

        var (guardado, ot) = await LeerCanalAsync(id);
        guardado.Should().Be(canal);
        ot.Should().Be(conOt ? HierarchyScenario.Ot1 : null);
    }

    [PostgresFact]
    public async Task AC1_SinCanalExplicito_ElHandlerDejaTramites()
    {
        var tipo = await TipoHabilitadoAsync();
        await using var ctx = NewContext();
        var handler = new CreateProcedureInstanceHandler(new ProcedureInstanceRepository(ctx), new ProcedureTypeRepository(ctx));

        var (resultado, error) = await handler.HandleAsync(
            new CreateProcedureInstanceRequest(HierarchyScenario.C1, tipo, HierarchyScenario.UserOf(HierarchyScenario.C1), TransitOfficeId: null));

        error.Should().BeNull();
        (await LeerCanalAsync(resultado!.Id)).Should().Be((TramiteCanal.Tramites, (Guid?)null));
    }

    [PostgresFact]
    public async Task AC1_LaAltaAdministrativaDejaCanalTramitesSinOt()
    {
        var tipo = await TipoHabilitadoAsync();
        Guid id;
        await using (var ctx = NewContext())
        {
            var repo = new AdminProcedureInstanceRepository(ctx);
            id = (await repo.CreateWithSnapshotAsync(
                new NewProcedureInstance(HierarchyScenario.C1, tipo, string.Empty, null, HierarchyScenario.UserOf(HierarchyScenario.C1)),
                "{}", null)).Id;
        }

        (await LeerCanalAsync(id)).Should().Be((TramiteCanal.Tramites, (Guid?)null));
    }

    // ── AC8: dos trámites del mismo vehículo por canales distintos ──────────────────────────────

    [PostgresFact]
    public async Task AC8_DosTramitesDelMismoVehiculo_PorWebYPorPunto_ConservanCadaUnoSuCanal()
    {
        var tipo = await TipoHabilitadoAsync();
        var web = await CrearAsync(tipo, TramiteCanal.PersonaNaturalWeb, null);
        var punto = await CrearAsync(tipo, TramiteCanal.PersonaNaturalOt, HierarchyScenario.Ot1);
        await using (var conn = await Fixture.OpenConnectionAsync())
        await using (var cmd = new NpgsqlCommand("UPDATE tramites.procedure_instances SET plate = 'ZZZ998' WHERE id = ANY(@ids)", conn))
        {
            cmd.Parameters.AddWithValue("ids", new[] { web, punto });
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        (await LeerCanalAsync(web)).Should().Be(("pn_web", (Guid?)null));
        (await LeerCanalAsync(punto)).Should().Be(("pn_punto", (Guid?)HierarchyScenario.Ot1));
    }

    // ── AC5: no cambian después de crear ─────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC5_EfRechazaCambiarElCanalOElOtDelCanalDeUnTramiteYaCreado()
    {
        var tipo = await TipoHabilitadoAsync();
        var id = await CrearAsync(tipo, TramiteCanal.PersonaNaturalOt, HierarchyScenario.Ot1);

        await using (var ctx = NewContext())
        {
            var fila = await ctx.ProcedureInstances.SingleAsync(p => p.Id == id, TestContext.Current.CancellationToken);
            fila.Channel = TramiteCanal.Tramites;
            fila.ChannelTransitOfficeId = null;

            var guardar = () => ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

            await guardar.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Channel*");
        }

        (await LeerCanalAsync(id)).Should().Be(("pn_punto", (Guid?)HierarchyScenario.Ot1));
    }

    [PostgresFact]
    public async Task AC5_LaRutaGenericaDeActualizacionDelWizardNoTocaElCanal()
    {
        var tipo = await TipoHabilitadoAsync();
        var id = await CrearAsync(tipo, TramiteCanal.PersonaNaturalOt, HierarchyScenario.Ot1);

        await using (var ctx = NewContext())
        {
            var desconectada = await ctx.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == id, TestContext.Current.CancellationToken);
            desconectada.Plate = "ZZZ999";
            desconectada.TransitOfficeId = HierarchyScenario.Ot2; // el organismo del trámite sí cambia
            desconectada.Channel = TramiteCanal.Tramites;          // un bug lo intentaría así
            desconectada.ChannelTransitOfficeId = null;
            await new ProcedureInstanceRepository(ctx).UpdateAsync(desconectada, TestContext.Current.CancellationToken);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var verificar = NewContext();
        var fila = await verificar.ProcedureInstances.AsNoTracking().SingleAsync(p => p.Id == id, TestContext.Current.CancellationToken);
        fila.Plate.Should().Be("ZZZ999", "el resto de la fila sí se actualiza");
        fila.TransitOfficeId.Should().Be(HierarchyScenario.Ot2);
        fila.Channel.Should().Be("pn_punto");
        fila.ChannelTransitOfficeId.Should().Be(HierarchyScenario.Ot1);
    }

    // ── AC10: el detalle trae el canal y el nombre del OT del canal ──────────────────────────────

    [PostgresFact]
    public async Task AC10_ElDetalleDeUnTramiteDelPuntoTraeElCanalYElNombreDelOt()
    {
        var tipo = await TipoHabilitadoAsync();
        var id = await CrearAsync(tipo, TramiteCanal.PersonaNaturalOt, HierarchyScenario.Ot1);

        var detalle = await DetalleAsync(id);

        detalle.Channel.Should().Be("pn_punto");
        detalle.ChannelTransitOfficeId.Should().Be(HierarchyScenario.Ot1);
        detalle.ChannelTransitOfficeName.Should().Contain("BOGOTA", "es el nombre del catálogo del organismo del punto");
    }

    [PostgresFact]
    public async Task AC10_ElDetalleDeUnTramiteHistoricoSinCanalSeLeeComoTramites()
    {
        var tipo = await TipoHabilitadoAsync();
        var id = await CrearAsync(tipo, TramiteCanal.Tramites, null);
        await using (var conn = await Fixture.OpenConnectionAsync())
        await using (var cmd = new NpgsqlCommand(
                         "UPDATE tramites.procedure_instances SET channel = NULL WHERE id = @id", conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var detalle = await DetalleAsync(id);

        detalle.Channel.Should().Be("tramites");
        detalle.ChannelTransitOfficeId.Should().BeNull();
        detalle.ChannelTransitOfficeName.Should().BeNull();
    }

    // ── AC9: backfill ────────────────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC9_ElBackfillAsignaCanalASoloLosTramitesNoMigradosConOrigenIctOPersonaNatural()
    {
        var tipo = await TipoHabilitadoAsync();
        var ict = await SembrarAsync(tipo, origin: "ict");
        var ictMayusculas = await SembrarAsync(tipo, origin: " ICT ");
        var punto = await SembrarAsync(tipo, origin: "persona_natural", ot: HierarchyScenario.Ot1, observaciones: MarcaPunto);
        var puntoConTexto = await SembrarAsync(tipo, origin: "persona_natural", ot: HierarchyScenario.Ot2, observaciones: $"{MarcaPunto} texto del ciudadano");
        var puntoSinOt = await SembrarAsync(tipo, origin: "persona_natural", ot: null, observaciones: MarcaPunto);
        var web = await SembrarAsync(tipo, origin: "persona_natural", ot: HierarchyScenario.Ot1, observaciones: "texto del ciudadano");
        var webSinObservaciones = await SembrarAsync(tipo, origin: "persona_natural");
        var marcaEnElMedio = await SembrarAsync(tipo, origin: "persona_natural", ot: HierarchyScenario.Ot1, observaciones: $"texto {MarcaPunto}");
        var ictMigrado = await SembrarAsync(tipo, origin: "ict", migrado: true);
        var portalMigrado = await SembrarAsync(tipo, origin: "persona_natural", ot: HierarchyScenario.Ot1, observaciones: MarcaPunto, migrado: true);
        var gestor = await SembrarAsync(tipo, origin: null);
        var otroOrigen = await SembrarAsync(tipo, origin: "otro_sistema");
        var yaAsignado = await SembrarAsync(tipo, origin: "ict", canal: "aplicacion");

        var antes = await SnapshotAsync();

        await EjecutarBackfillAsync();

        var despues = await SnapshotAsync();
        Par(despues, ict).Should().Be(("integracion", null));
        Par(despues, ictMayusculas).Should().Be(("integracion", null));
        Par(despues, punto).Should().Be(("pn_punto", HierarchyScenario.Ot1));
        Par(despues, puntoConTexto).Should().Be(("pn_punto", HierarchyScenario.Ot2), "la marca es un prefijo y el resto del texto no importa");
        Par(despues, puntoSinOt).Should().Be(("pn_web", null), "sin organismo el CHECK rechazaría pn_punto: queda como pn_web");
        Par(despues, web).Should().Be(("pn_web", null));
        Par(despues, webSinObservaciones).Should().Be(("pn_web", null));
        Par(despues, marcaEnElMedio).Should().Be(("pn_web", null), "solo cuenta la marca al inicio de la observación");
        Par(despues, ictMigrado).Should().Be((null, null), "un migrado de V1 queda sin dato");
        Par(despues, portalMigrado).Should().Be((null, null));
        Par(despues, gestor).Should().Be((null, null));
        Par(despues, otroOrigen).Should().Be((null, null));
        Par(despues, yaAsignado).Should().Be(("aplicacion", null), "no se pisa un canal ya asignado");

        // Es un dato derivado: ni sube row_version ni mueve sync_version (los triggers de usuario estuvieron apagados)...
        foreach (var id in new[] { ict, punto, web })
            despues[id].RowVersion.Should().Be(antes[id].RowVersion);
        // ...y quedan encendidos al terminar.
        (await TriggersDeUsuarioApagadosAsync()).Should().Be(0);
    }

    [PostgresFact]
    public async Task AC9_UnaSegundaCorridaDelBackfillNoCambiaNada()
    {
        var tipo = await TipoHabilitadoAsync();
        await SembrarAsync(tipo, origin: "ict");
        await SembrarAsync(tipo, origin: "persona_natural", ot: HierarchyScenario.Ot1, observaciones: MarcaPunto);
        await SembrarAsync(tipo, origin: "persona_natural");
        await SembrarAsync(tipo, origin: null);
        await EjecutarBackfillAsync();
        var primera = await SnapshotAsync();

        await EjecutarBackfillAsync();

        (await SnapshotAsync()).Should().BeEquivalentTo(primera);
    }

    [PostgresFact]
    public async Task AC9_ElBackfillSinTramitesNoFalla() => await EjecutarBackfillAsync();

    [Fact]
    public void AC9_ElBackfillNoSeRevierte()
    {
        var down = new Flit.Infrastructure.Migrations.HU14318_BackfillCanalTramite().DownOperations;

        down.Should().BeEmpty("el backfill no se revierte: documentado en la migración");
    }

    // ── datos ────────────────────────────────────────────────────────────────────────────────────

    private async Task<Guid> TipoHabilitadoAsync()
    {
        var tipo = await HierarchyScenario.SeedAsync(Fixture);
        await using var ctx = NewContext();
        var fila = await ctx.ProcedureTypes.SingleAsync(t => t.Id == tipo.Id, TestContext.Current.CancellationToken);
        fila.PublicationStatus = Flit.Tramites.Domain.Enums.PublicationStatus.Published;
        fila.WizardEnabled = true;
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return tipo.Id;
    }

    private async Task<Guid> CrearAsync(Guid tipo, string canal, Guid? ot)
    {
        await using var ctx = NewContext();
        var handler = new CreateProcedureInstanceHandler(new ProcedureInstanceRepository(ctx), new ProcedureTypeRepository(ctx));

        var (resultado, error) = await handler.HandleAsync(
            new CreateProcedureInstanceRequest(
                HierarchyScenario.C1, tipo, HierarchyScenario.UserOf(HierarchyScenario.C1), TransitOfficeId: null,
                Channel: canal, ChannelTransitOfficeId: ot),
            TestContext.Current.CancellationToken);

        error.Should().BeNull();
        return resultado!.Id;
    }

    private async Task<(string? Canal, Guid? Ot)> LeerCanalAsync(Guid id)
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT channel, channel_transit_office_id FROM tramites.procedure_instances WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }

    private async Task<ProcedureInstanceDetailDto> DetalleAsync(Guid id)
    {
        await using var ctx = NewContext();
        var handler = new GetProcedureInstanceHandler(new ProcedureInstanceRepository(ctx));

        var (detalle, error) = await handler.HandleAsync(id, HierarchyScenario.C1, TestContext.Current.CancellationToken);

        error.Should().BeNull();
        return detalle!;
    }

    private async Task<Guid> SembrarAsync(
        Guid tipo, string? origin, Guid? ot = null, string? observaciones = null, bool migrado = false, string? canal = null)
    {
        var id = Guid.NewGuid();
        await using var conn = await Fixture.OpenConnectionAsync();
        await using (var cmd = new NpgsqlCommand(
                         """
                         INSERT INTO tramites.procedure_instances
                             (id, tenant_id, procedure_type_id, reference_number, status, created_by_user_id, created_at,
                              origin, transit_office_id, is_migrated, channel)
                         VALUES (@id, @tenant, @tipo, @ref, 'borrador', @gestor, now(), @origin, @ot, @migrado, @canal)
                         """, conn))
        {
            cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("tenant", HierarchyScenario.C1);
            cmd.Parameters.AddWithValue("tipo", tipo);
            cmd.Parameters.AddWithValue("ref", "IT-14318-" + id.ToString("N")[..10]);
            cmd.Parameters.AddWithValue("gestor", HierarchyScenario.UserOf(HierarchyScenario.C1));
            cmd.Parameters.AddWithValue("origin", (object?)origin ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ot", (object?)ot ?? DBNull.Value);
            cmd.Parameters.AddWithValue("migrado", migrado);
            cmd.Parameters.AddWithValue("canal", (object?)canal ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        if (observaciones is not null)
        {
            await using var campo = new NpgsqlCommand(
                """
                INSERT INTO tramites.procedure_instance_field_values (tenant_id, procedure_instance_id, field_key, value_text, source)
                VALUES (@tenant, @id, 'observaciones', @texto, 'user')
                """, conn);
            campo.Parameters.AddWithValue("tenant", HierarchyScenario.C1);
            campo.Parameters.AddWithValue("id", id);
            campo.Parameters.AddWithValue("texto", observaciones);
            await campo.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        return id;
    }

    /// <summary>Ejecuta el SQL de la migración tal como lo corre EF (<c>migrationBuilder.Sql</c>).</summary>
    private async Task EjecutarBackfillAsync()
    {
        var sql = new Flit.Infrastructure.Migrations.HU14318_BackfillCanalTramite()
            .UpOperations.OfType<SqlOperation>().Single().Sql;
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        await tx.CommitAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Dictionary<Guid, (string? Canal, Guid? Ot, long RowVersion)>> SnapshotAsync()
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT id, channel, channel_transit_office_id, row_version FROM tramites.procedure_instances", conn);
        await using var reader = await cmd.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var filas = new Dictionary<Guid, (string?, Guid?, long)>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            filas[reader.GetGuid(0)] = (
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetInt64(3));
        }

        return filas;
    }

    private static (string? Canal, Guid? Ot) Par(Dictionary<Guid, (string? Canal, Guid? Ot, long RowVersion)> filas, Guid id) =>
        (filas[id].Canal, filas[id].Ot);

    private async Task<long> TriggersDeUsuarioApagadosAsync()
    {
        await using var conn = await Fixture.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_trigger
             WHERE tgrelid = 'tramites.procedure_instances'::regclass AND NOT tgisinternal AND tgenabled = 'D'
            """, conn);
        return (long)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
