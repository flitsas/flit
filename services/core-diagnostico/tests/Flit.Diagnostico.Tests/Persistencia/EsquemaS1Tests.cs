using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Persistencia;

/// <summary>
/// HU #14576 AC28 y AC29 (ADR-0087, schema.md con SP-2 y SP-19) — migración <c>S1_Saneamiento</c>: tres tablas con sus CHECK e
/// índices, la columna <c>vehiculos.saneamiento_estado</c>, el CHECK de notificaciones con los cuatro tipos nuevos, el borrado
/// lógico del renglón (<c>quitado_en</c>) y el contador de reemplazos. Up y Down sobre la base de prueba; el Down se bloquea si
/// hay saneamientos. Las reglas las garantiza la base: se prueban por SQL directo. Placas ficticias.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EsquemaS1Tests
/// </code>
/// </example>
public sealed class EsquemaS1Tests
{
    private const string MigracionAnterior = "20261113070402_R9_AjusteConTramite";

    private static readonly string[] Indices =
    [
        "ux_saneamiento_vigente", "ix_saneamientos_vehiculo", "ix_saneamientos_diagnostico", "ix_saneamientos_bandeja",
        "ux_saneamiento_renglones_fuente", "ix_saneamiento_renglones_saneamiento", "ix_saneamiento_historial_saneamiento",
        "ix_vehiculos_saneamiento", "ix_vehiculos_identificador_prefijo",
    ];

    private static readonly string[] Checks =
    [
        "ck_saneamientos_estado", "ck_saneamientos_tomado", "ck_saneamientos_cierre", "ck_saneamientos_motivo_cierre", "ck_saneamientos_anulacion",
        "ck_saneamientos_motivo_anulacion", "ck_saneamientos_observacion", "ck_saneamiento_renglones_fuente", "ck_saneamiento_renglones_hallazgo",
        "ck_saneamiento_renglones_resultado", "ck_saneamiento_renglones_otro", "ck_saneamiento_renglones_hallazgo_fuente", "ck_saneamiento_renglones_nota",
        "ck_saneamiento_renglones_evidencia", "ck_saneamiento_renglones_evidencia_sha256", "ck_saneamiento_renglones_evidencia_tamano",
        "ck_saneamiento_renglones_actualizado", "ck_saneamiento_renglones_reemplazos", "ck_saneamiento_historial_evento",
        "ck_saneamiento_historial_estado_anterior", "ck_saneamiento_historial_estado_nuevo", "ck_saneamiento_historial_fuente",
        "ck_saneamiento_historial_renglon", "ck_saneamiento_historial_transicion", "ck_saneamiento_historial_motivo", "ck_vehiculos_saneamiento_estado",
    ];

    // AC28 — Up crea todo y el Down lo retira; volver a subir deja la base sin pendientes.
    [Fact]
    public async Task HU14576_AC28_S1_crea_tablas_checks_e_indices_y_es_reversible()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);

        (await ListaAsync(conexion, "SELECT indexname FROM pg_indexes WHERE schemaname = 'diagnostico'", ct)).Should().Contain(Indices);
        (await ListaAsync(conexion, "SELECT conname FROM pg_constraint WHERE contype = 'c'", ct)).Should().Contain(Checks);
        (await ListaAsync(conexion, "SELECT conname FROM pg_constraint WHERE contype = 'f' AND conname LIKE 'fk_saneamiento%'", ct)).Should().BeEquivalentTo(
            "fk_saneamientos_diagnosticos", "fk_saneamientos_vehiculos", "fk_saneamiento_renglones_saneamientos", "fk_saneamiento_historial_saneamientos");
        (await ColumnasAsync(conexion, "saneamiento_renglones", ct)).Should().Contain(["quitado_en:YES", "evidencia_reemplazos:NO"]);
        (await ColumnasAsync(conexion, "vehiculos", ct)).Should().Contain("saneamiento_estado:YES");
        (await DefinicionAsync(conexion, "ck_notificaciones_tipo", ct)).Should()
            .ContainAll("SANEAMIENTO_TOMADO", "SANEAMIENTO_CERRADO", "SANEAMIENTO_REABIERTO", "SANEAMIENTO_ANULADO", "LOTE_TERMINADO");
        (await IndiceAsync(conexion, "ux_saneamiento_renglones_fuente", ct)).Should().Contain("quitado_en IS NULL", "SP-2: el quitado libera la fuente");

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior, ct);
            (await ListaAsync(conexion, "SELECT tablename FROM pg_tables WHERE schemaname = 'diagnostico' AND tablename LIKE 'saneamiento%'", ct))
                .Should().BeEmpty("el Down retira las tres tablas");
            (await ColumnasAsync(conexion, "vehiculos", ct)).Should().NotContain("saneamiento_estado:YES");
            (await DefinicionAsync(conexion, "ck_notificaciones_tipo", ct)).Should().NotContain("SANEAMIENTO_");
            await db.Database.MigrateAsync(ct);
            (await db.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
        }

        (await ListaAsync(conexion, "SELECT tablename FROM pg_tables WHERE schemaname = 'diagnostico' AND tablename LIKE 'saneamiento%'", ct))
            .Should().BeEquivalentTo("saneamientos", "saneamiento_renglones", "saneamiento_historial");
    }

    // AC28 / AC4 — un solo no anulado por vehículo; AC29 — un evento fuera de la lista viola el CHECK; el Down se bloquea con datos.
    [Fact]
    public async Task HU14576_AC28_AC29_la_base_impide_estados_imposibles_y_bloquea_el_Down_con_datos()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote, vehiculo) = await SembrarVehiculoAsync(conexion, ct);

        var primero = await InsertarSaneamientoAsync(conexion, tenant, lote, vehiculo, ct);
        (await Violacion(() => InsertarSaneamientoAsync(conexion, tenant, lote, vehiculo, ct))).Should().Be(("23505", "ux_saneamiento_vigente"));
        (await Violacion(() => EjecutarAsync(conexion,
            $"INSERT INTO diagnostico.saneamiento_historial (id, tenant_id, saneamiento_id, evento, estado_nuevo, usuario_id, es_flit, ocurrido_en) VALUES ('{Guid.CreateVersion7()}', '{tenant}', '{primero}', 'PAGADO', 'SOLICITADO', '{Guid.NewGuid()}', false, now())", ct)))
            .Should().Be(("23514", "ck_saneamiento_historial_evento"));
        (await Violacion(() => EjecutarAsync(conexion,
            $"INSERT INTO diagnostico.saneamiento_renglones (id, tenant_id, saneamiento_id, fuente, hallazgo, automatico, evidencia_referencia, creado_por, creado_en) VALUES ('{Guid.CreateVersion7()}', '{tenant}', '{primero}', 'SOAT', 'NO_VIGENTE', true, 'ref', '{Guid.NewGuid()}', now())", ct)))
            .Should().Be(("23514", "ck_saneamiento_renglones_evidencia"));
        (await Violacion(() => EjecutarAsync(conexion,
            $"INSERT INTO diagnostico.saneamiento_renglones (id, tenant_id, saneamiento_id, fuente, hallazgo, automatico, evidencia_reemplazos, creado_por, creado_en) VALUES ('{Guid.CreateVersion7()}', '{tenant}', '{primero}', 'SOAT', 'NO_VIGENTE', true, 21, '{Guid.NewGuid()}', now())", ct)))
            .Should().Be(("23514", "ck_saneamiento_renglones_reemplazos"));
        (await Violacion(() => EjecutarAsync(conexion, $"UPDATE diagnostico.vehiculos SET saneamiento_estado = 'ANULADO' WHERE id = '{vehiculo}'", ct)))
            .Should().Be(("23514", "ck_vehiculos_saneamiento_estado"));

        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        var error = (await FluentActions.Awaiting(() => db.GetService<IMigrator>().MigrateAsync(MigracionAnterior, ct)).Should().ThrowAsync<PostgresException>()).Which;
        error.MessageText.Should().Contain("S1_Saneamiento");
        (await db.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty("el rollback fallido no bajó nada");
    }

    private static async Task<(string SqlState, string? Constraint)> Violacion(Func<Task> accion)
    {
        var error = (await FluentActions.Awaiting(accion).Should().ThrowAsync<PostgresException>()).Which;
        return (error.SqlState, error.ConstraintName);
    }

    private static async Task<Guid> InsertarSaneamientoAsync(NpgsqlConnection conexion, Guid tenant, Guid lote, Guid vehiculo, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        await EjecutarAsync(conexion,
            $"INSERT INTO diagnostico.saneamientos (id, tenant_id, diagnostico_id, vehiculo_id, estado, solicitado_por, solicitado_en) VALUES ('{id}', '{tenant}', '{lote}', '{vehiculo}', 'SOLICITADO', '{Guid.NewGuid()}', now())", ct);
        return id;
    }

    private static async Task EjecutarAsync(NpgsqlConnection conexion, string sql, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<string>> ListaAsync(NpgsqlConnection conexion, string sql, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(sql, conexion);
        var lista = new List<string>();
        await using var fila = await comando.ExecuteReaderAsync(ct);
        while (await fila.ReadAsync(ct))
            lista.Add(fila.GetString(0));
        return lista;
    }

    private static Task<List<string>> ColumnasAsync(NpgsqlConnection conexion, string tabla, CancellationToken ct) =>
        ListaAsync(conexion, $"SELECT column_name || ':' || is_nullable FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name = '{tabla}'", ct);

    private static async Task<string> DefinicionAsync(NpgsqlConnection conexion, string nombre, CancellationToken ct) =>
        string.Join(" ", await ListaAsync(conexion, $"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = '{nombre}'", ct));

    private static async Task<string> IndiceAsync(NpgsqlConnection conexion, string nombre, CancellationToken ct) =>
        string.Join(" ", await ListaAsync(conexion, $"SELECT indexdef FROM pg_indexes WHERE indexname = '{nombre}'", ct));

    private static async Task<NpgsqlConnection> AbrirAsync(ApiDePrueba api, CancellationToken ct)
    {
        var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        return conexion;
    }

    private static async Task<(Guid Tenant, Guid Lote, Guid Vehiculo)> SembrarVehiculoAsync(NpgsqlConnection conexion, CancellationToken ct)
    {
        var tenant = Guid.NewGuid();
        var lote = Guid.CreateVersion7();
        var vehiculo = Guid.CreateVersion7();
        await EjecutarAsync(conexion,
            "INSERT INTO diagnostico.diagnosticos (id, tenant_id, nombre, modo, alcance, configuracion, estado, usuario_creador_id, creado_en) " +
            $"VALUES ('{lote}', '{tenant}', 'Lote', 'SIMPLE', '[\"RUNT\"]'::jsonb, '{{}}'::jsonb, 'CREADO', '{Guid.NewGuid()}', now())", ct);
        await EjecutarAsync(conexion,
            "INSERT INTO diagnostico.vehiculos (id, tenant_id, diagnostico_id, identificador_original, identificador_norm, tipo_identificador, placa, orden) " +
            $"VALUES ('{vehiculo}', '{tenant}', '{lote}', 'SAN401', 'SAN401', 'PLACA_CARRO', 'SAN401', 1)", ct);
        return (tenant, lote, vehiculo);
    }
}
