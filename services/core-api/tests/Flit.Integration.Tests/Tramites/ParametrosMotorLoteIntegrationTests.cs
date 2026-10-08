using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Repositories;
using Flit.Infrastructure.Security;
using Flit.Integration.Tests.Postgres;
using Flit.Integration.Tests.Tenancy;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Flit.Tramites.Domain.Entities;
using Flit.Tramites.Domain.Entities.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.Estados;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flit.Integration.Tests.Tramites;

/// <summary>
/// HU #13420 (épica #13216) — edición de los parámetros del motor contra PostgreSQL real:
/// <see cref="ActualizarParametrosMotorLoteHandler"/> + <see cref="ConsolidadoExportSettingsRepository"/>. Cubre la
/// persistencia con <c>updated_by</c>/<c>updated_at</c> y el nombre visible, la fila de <c>audit.audit_logs</c> que
/// deja <c>tr_consolidado_export_settings_audit</c> con los valores anterior y nuevo, el 409 real por
/// <c>row_version</c> (también en la carrera entre la lectura y el UPDATE), el efecto en el alta del lote (tope 5.000 ⇒
/// 422 sin tocar los lotes ya creados; motor apagado ⇒ 503) y los extremos de cada CHECK del DDL 133 frente a
/// <see cref="ConsolidadoExportSettingsRangos"/>.
/// <para>Uso de ejemplo:
/// <code>
/// var r = await Actualizar(ctx).HandleAsync(Comando(rowVersion: 0) with { MaxItemsPerBatch = 5000 }, ct);
/// </code></para>
/// </summary>
public sealed class ParametrosMotorLoteIntegrationTests(PostgresDatabaseFixture fixture) : PostgresTestBase(fixture)
{
    private static readonly Guid C1 = HierarchyScenario.C1;
    private static readonly Guid C2 = HierarchyScenario.C2;
    private static readonly Guid SuperAdmin = HierarchyScenario.UserOf(HierarchyScenario.C1);

    private readonly EphemeralDataProtectionProvider _dataProtection = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ActualizarParametrosMotorLoteHandler Actualizar(FlitDbContext ctx) =>
        new(new ConsolidadoExportSettingsRepository(ctx));

    private static ObtenerParametrosMotorLoteHandler Obtener(FlitDbContext ctx) =>
        new(new ConsolidadoExportSettingsRepository(ctx));

    /// <summary>Los valores sembrados por el DDL 133.</summary>
    private static ActualizarParametrosMotorLoteCommand Comando(long rowVersion) =>
        new(10_000, 500, 250, 2, 300, 600, 3, 30, 1200, 1800, 3, 24, true, rowVersion, SuperAdmin);

    /// <summary>El reset del arnés vacía la tabla de parámetros: se siembra con los DEFAULT del DDL 133.</summary>
    private async Task<Guid> SembrarSettingsAsync()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        await ExecAsync(cn, "DELETE FROM tramites.consolidado_export_settings;");
        return (await ScalarAsync<Guid>(cn,
            "INSERT INTO tramites.consolidado_export_settings (id) VALUES (uuidv7()) RETURNING id"))!;
    }

    private CrearLoteConsolidadosHandler Crear(FlitDbContext ctx)
    {
        var procedimientos = new ProcedureInstanceRepository(ctx);
        return new CrearLoteConsolidadosHandler(
            new ConsolidadoLoteRepository(ctx),
            new LoteSeleccionResolverPorOrigen(
                [new TramitesSeleccionResolver(new ListProcedureInstancesFilteredHandler(procedimientos), procedimientos)]),
            new ConsolidadoLoteCipher(_dataProtection));
    }

    private static CrearLoteConsolidadosCommand Gestor(Guid tenant, LoteSeleccion seleccion) => new()
    {
        Origen = ConsolidadoExportOrigin.Tramites,
        TenantId = tenant,
        UsuarioId = HierarchyScenario.UserOf(tenant),
        RolCodigo = "Radicador",
        TipoDocumento = ConsolidadoExportDocumentType.Consolidado,
        Seleccion = seleccion,
        ConfirmaEfectos = true,
        ClientIp = System.Net.IPAddress.Parse("10.1.3.4"),
        UserAgent = "pruebas-13420",
    };

    private async Task SembrarTramitesPreparadosAsync(Guid tenant, int cuantos)
    {
        await using var ctx = NewContext();
        var type = await ctx.ProcedureTypes.AsNoTracking().OrderBy(t => t.Code).Select(t => t.Id).FirstAsync(Ct);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < cuantos; i++)
        {
            ctx.ProcedureInstances.Add(new ProcedureInstance
            {
                Id = Guid.NewGuid(),
                TenantId = tenant,
                ProcedureTypeId = type,
                ReferenceNumber = $"{700000 + i}",
                Status = TramiteEstado.Preparado,
                Plate = $"QP{i:D5}",
                Vin = $"VINP13420{i:D8}",
                CreatedByUserId = HierarchyScenario.UserOf(tenant),
                CreatedAt = now.AddMinutes(-i - 10),
            });
        }

        await ctx.SaveChangesAsync(Ct);
    }

    // ── AC1 + AC2 — persistencia y auditoría ───────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_Guardar_PersisteValores_UpdatedByYAt_SubeRowVersion_YAuditLogConAnteriorYNuevo()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        var id = await SembrarSettingsAsync();

        ParametrosMotorLoteDto antes;
        await using (var ctx = NewContext())
            antes = (await Obtener(ctx).HandleAsync(Ct))!;
        antes.RowVersion.Should().Be(0);
        antes.UpdatedBy.Should().BeNull();
        antes.UpdatedByName.Should().BeNull();

        ParametrosMotorLoteResultado r;
        await using (var ctx = NewContext())
            r = await Actualizar(ctx).HandleAsync(Comando(antes.RowVersion) with { MaxItemsPerBatch = 5000, RetentionHours = 48 }, Ct);

        r.Estado.Should().Be(ParametrosMotorLoteEstado.Actualizado, string.Join(";", r.Errores.Keys));
        r.Parametros!.MaxItemsPerBatch.Should().Be(5000);
        r.Parametros.RowVersion.Should().Be(1, "el trigger tr_consolidado_export_settings_row_version la sube en el UPDATE");

        string nombre;
        await using (var db = NewContext())
            nombre = await db.Users.AsNoTracking().Where(u => u.Id == SuperAdmin).Select(u => u.DisplayName).SingleAsync(Ct);
        r.Parametros.UpdatedBy.Should().Be(SuperAdmin);
        r.Parametros.UpdatedByName.Should().Be(nombre).And.NotBeNullOrWhiteSpace();
        r.Parametros.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));

        await using var cn = await Fixture.OpenConnectionAsync();
        (await ScalarAsync<string>(cn,
                "SELECT concat_ws(',', max_items_per_batch, retention_hours, row_version, updated_by) FROM tramites.consolidado_export_settings"))
            .Should().Be($"5000,48,1,{SuperAdmin}");
        (await ScalarAsync<long>(cn,
                """
                SELECT count(*) FROM audit.audit_logs
                 WHERE schema_name = 'tramites' AND table_name = 'consolidado_export_settings' AND record_id = @id
                   AND action = 'U'
                   AND old_data->>'max_items_per_batch' = '10000' AND new_data->>'max_items_per_batch' = '5000'
                   AND old_data->>'retention_hours' = '24' AND new_data->>'retention_hours' = '48'
                   AND old_data->>'updated_by' IS NULL AND new_data->>'updated_by' = @u
                   AND new_data->>'updated_at' IS NOT NULL
                """, ("id", id), ("u", SuperAdmin.ToString())))
            .Should().Be(1, "trg_audit_log deja la fila anterior y la nueva completas, con el usuario y la fecha");
    }

    // ── AC4 — 409 real ─────────────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC4_DosSuperAdminConLaMismaVersion_ElSegundoRecibeConflicto_YNoPisaAlPrimero()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();

        ParametrosMotorLoteResultado primero, segundo;
        await using (var ctx = NewContext())
            primero = await Actualizar(ctx).HandleAsync(Comando(0) with { MaxItemsPerBatch = 4000 }, Ct);
        await using (var ctx = NewContext())
            segundo = await Actualizar(ctx).HandleAsync(Comando(0) with { MaxItemsPerBatch = 6000 }, Ct);

        primero.Estado.Should().Be(ParametrosMotorLoteEstado.Actualizado);
        segundo.Estado.Should().Be(ParametrosMotorLoteEstado.Conflicto);
        await using var cn = await Fixture.OpenConnectionAsync();
        (await ScalarAsync<int>(cn, "SELECT max_items_per_batch FROM tramites.consolidado_export_settings")).Should().Be(4000);
    }

    [PostgresFact]
    public async Task AC4_CambioEntreLaLecturaYElUpdate_ElWhereRowVersionNoCasa_Conflicto()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();

        // Otra sesión cambia la fila y retiene el lock: el repositorio lee la versión 0 (MVCC), pasa su chequeo previo y
        // su UPDATE ... WHERE row_version = 0 espera. Al confirmar la otra sesión, PostgreSQL reevalúa el WHERE contra la
        // versión 1 y no actualiza nada: es la concurrencia optimista de EF, no el chequeo previo, la que da el 409.
        await using var otra = await Fixture.OpenConnectionAsync();
        await using var tx = await otra.BeginTransactionAsync(Ct);
        await using (var cmd = new NpgsqlCommand(
                         "UPDATE tramites.consolidado_export_settings SET max_items_per_batch = 4000", otra, tx))
            await cmd.ExecuteNonQueryAsync(Ct);

        await using var ctx = NewContext();
        var guardar = Actualizar(ctx).HandleAsync(Comando(0) with { MaxItemsPerBatch = 6000 }, Ct);
        await EsperarBloqueoAsync();
        await tx.CommitAsync(Ct);

        (await guardar).Estado.Should().Be(ParametrosMotorLoteEstado.Conflicto);
        await using var cn = await Fixture.OpenConnectionAsync();
        (await ScalarAsync<string>(cn,
                "SELECT concat_ws(',', max_items_per_batch, row_version) FROM tramites.consolidado_export_settings"))
            .Should().Be("4000,1");
    }

    /// <summary>Espera a que una sesión quede bloqueada por lock sobre la tabla de parámetros (máx. 10 s).</summary>
    private async Task EsperarBloqueoAsync()
    {
        await using var cn = await Fixture.OpenConnectionAsync();
        for (var i = 0; i < 200; i++)
        {
            var esperando = await ScalarAsync<long>(cn,
                """
                SELECT count(*) FROM pg_stat_activity
                 WHERE wait_event_type = 'Lock' AND query ILIKE '%consolidado_export_settings%'
                """);
            if (esperando > 0)
                return;
            await Task.Delay(50, Ct);
        }

        throw new TimeoutException("El UPDATE del repositorio nunca quedó esperando el lock.");
    }

    // ── AC2 — efecto en el alta del lote ───────────────────────────────────────────────

    [PostgresFact]
    public async Task AC2_TrasGuardarTope5000_ElSiguienteLoteDe5001Recibe422_YElLoteYaCreadoNoCambia()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();

        // Lote creado ANTES del cambio (otro usuario, otra compañía).
        CrearLoteConsolidadosResultado previo;
        await using (var ctx = NewContext())
            previo = await Crear(ctx).HandleAsync(Gestor(C2, new SeleccionPorIds(HierarchyScenario.ProceduresOf(C2).ToList())), Ct);
        previo.Creado.Should().BeTrue(previo.Error);
        var loteId = previo.Lote!.Id;
        string FotoSql() =>
            $"SELECT concat_ws(',', status, total_items, row_version) FROM tramites.consolidado_export_batches WHERE id = '{loteId}'";
        string fotoAntes;
        await using (var cn = await Fixture.OpenConnectionAsync())
            fotoAntes = (await ScalarAsync<string>(cn, FotoSql()))!;

        await using (var ctx = NewContext())
            (await Actualizar(ctx).HandleAsync(Comando(0) with { MaxItemsPerBatch = 5000 }, Ct))
                .Estado.Should().Be(ParametrosMotorLoteEstado.Actualizado);

        await SembrarTramitesPreparadosAsync(C1, 5001);
        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Crear(ctx).HandleAsync(Gestor(C1, new SeleccionPorFiltro(
                new TramitesLoteFiltro(new ProcedureInstanceListRequest { Estados = [TramiteEstado.Preparado] }), null)), Ct);

        r.Creado.Should().BeFalse();
        r.Error.Should().Be(LoteSeleccionInvalidaException.CodigoExcedeTope);
        r.Total.Should().Be(5001);
        r.Tope.Should().Be(5000, "el alta lee max_items_per_batch en cada uso, sin caché");

        await using var cn2 = await Fixture.OpenConnectionAsync();
        (await ScalarAsync<long>(cn2,
                "SELECT count(*) FROM tramites.consolidado_export_batches WHERE requested_by_user_id = @u",
                ("u", HierarchyScenario.UserOf(C1))))
            .Should().Be(0, "el 422 no crea nada");
        (await ScalarAsync<string>(cn2, FotoSql())).Should().Be(fotoAntes, "los lotes ya creados no cambian");
    }

    // ── AC6 — apagar el motor ──────────────────────────────────────────────────────────

    [PostgresFact]
    public async Task AC6_TrasApagarElMotor_ElAltaResponde503MotorInactivo_SinCrearLote()
    {
        await HierarchyScenario.SeedAsync(Fixture);
        await SembrarSettingsAsync();

        await using (var ctx = NewContext())
            (await Actualizar(ctx).HandleAsync(Comando(0) with { IsActive = false }, Ct))
                .Parametros!.IsActive.Should().BeFalse();

        CrearLoteConsolidadosResultado r;
        await using (var ctx = NewContext())
            r = await Crear(ctx).HandleAsync(Gestor(C1, new SeleccionPorIds(HierarchyScenario.ProceduresOf(C1).ToList())), Ct);

        r.Creado.Should().BeFalse();
        r.Error.Should().Be(CrearLoteConsolidadosErrores.MotorInactivo);
        await using var cn = await Fixture.OpenConnectionAsync();
        (await ScalarAsync<long>(cn, "SELECT count(*) FROM tramites.consolidado_export_batches")).Should().Be(0);
    }

    // ── AC3 — extremos de cada CHECK contra la base real ───────────────────────────────

    [PostgresFact]
    public async Task AC3_LosExtremosDeRangos_SonLosDeLosCheckDelDdl133_EnLaBaseReal()
    {
        await SembrarSettingsAsync();
        await using var cn = await Fixture.OpenConnectionAsync();

        foreach (var rango in ConsolidadoExportSettingsRangos.Todos)
        {
            (await IntentarAsync(cn, rango.Columna, rango.Minimo)).Should().BeNull($"{rango.Columna} = {rango.Minimo} es válido");
            (await IntentarAsync(cn, rango.Columna, rango.Minimo - 1)).Should().Be(rango.Restriccion);
            if (rango.SinMaximo)
                continue;
            (await IntentarAsync(cn, rango.Columna, rango.Maximo)).Should().BeNull($"{rango.Columna} = {rango.Maximo} es válido");
            (await IntentarAsync(cn, rango.Columna, rango.Maximo + 1)).Should().Be(rango.Restriccion);
        }

        foreach (var regla in ConsolidadoExportSettingsRangos.ReglasLease)
            (await IntentarAsync(cn, regla.ColumnaLease, null, $"{regla.ColumnaTimeout}")).Should().Be(regla.Restriccion);
    }

    /// <summary>UPDATE en una transacción revertida; devuelve la restricción violada o <c>null</c> si se aceptó.</summary>
    private static async Task<string?> IntentarAsync(NpgsqlConnection cn, string columna, int? valor, string? expresion = null)
    {
        await using var tx = await cn.BeginTransactionAsync(Ct);
        try
        {
            // Columna del catálogo de Rangos (constante), nunca de entrada externa; el valor va parametrizado.
            await using var cmd = new NpgsqlCommand(
                $"UPDATE tramites.consolidado_export_settings SET {columna} = {expresion ?? "@v"}", cn, tx);
            if (valor is { } v)
                cmd.Parameters.AddWithValue("v", v);
            await cmd.ExecuteNonQueryAsync(Ct);
            return null;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            return ex.ConstraintName;
        }
        finally
        {
            await tx.RollbackAsync(Ct);
        }
    }

    // ── SQL ────────────────────────────────────────────────────────────────────────────

    private static async Task ExecAsync(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection cn, string sql, params (string Name, object? Value)[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, cn);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(Ct);
        return result is null or DBNull ? default : (T)result;
    }
}
