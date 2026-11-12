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
/// Punto 4, incremento 2 (diseño §4, ADR-0084) — migración <c>P4_DerivacionTramite</c> sobre el schema migrado:
/// <c>derivacion_operaciones</c> y <c>derivaciones</c> con FK compuestas por empresa (RESTRICT), CHECK de estados, contadores y
/// coherencia (CREADO ⇔ trámite, OMITIDO/FALLIDO ⇒ motivo, TERMINADA ⇔ terminada_en), el índice único parcial
/// <c>ux_derivaciones_vehiculo_vivo</c> (un ítem PENDIENTE/ENVIADO/CREADO por vehículo; OMITIDO y FALLIDO no lo ocupan) y las
/// columnas <c>tramite_*</c> de <c>vehiculos</c>. Reversible. Las reglas las garantiza la base: se prueban por SQL directo.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EsquemaP4DerivacionTests
/// </code>
/// </example>
public sealed class EsquemaP4DerivacionTests
{
    private const string MigracionAnterior = "20261111180230_P7b_PlacaMotocarro";

    [Fact]
    public async Task Migracion_P4_aplica_idempotente_y_se_revierte()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            (await db.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
            var idempotente = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            await using var script = new NpgsqlCommand(idempotente, conexion);
            await script.ExecuteNonQueryAsync(ct);
        }

        await VerificarAsync(conexion, ct);

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior, ct);
            (await ListarAsync(conexion, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'diagnostico'", ct))
                .Should().NotContain(["derivacion_operaciones", "derivaciones"]);
            (await ListarAsync(conexion, "SELECT column_name FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name = 'vehiculos'", ct))
                .Should().NotContain("tramite_id");
            await db.Database.MigrateAsync(ct);
        }

        await VerificarAsync(conexion, ct);
    }

    [Fact]
    public async Task Un_solo_item_vivo_por_vehiculo_y_omitido_o_fallido_no_lo_ocupan()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote, vehiculo) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);
        var op = await InsertarOperacionAsync(conexion, tenant, lote, ct, total: 10);

        await InsertarItemAsync(conexion, tenant, op, vehiculo, "OMITIDO", ct, motivo: "OT_NO_HABILITADO");
        await InsertarItemAsync(conexion, tenant, op, vehiculo, "FALLIDO", ct, motivo: "TRAMITES_NO_DISPONIBLE");
        await InsertarItemAsync(conexion, tenant, op, vehiculo, "ENVIADO", ct);
        await DebeViolarAsync(() => InsertarItemAsync(conexion, tenant, op, vehiculo, "PENDIENTE", ct), "ux_derivaciones_vehiculo_vivo", PostgresErrorCodes.UniqueViolation);
        await DebeViolarAsync(() => InsertarItemAsync(conexion, tenant, op, vehiculo, "CREADO", ct, tramite: Guid.NewGuid()), "ux_derivaciones_vehiculo_vivo", PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task Checks_de_coherencia_y_FK_compuestas_por_empresa()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (a, loteA, vehiculoA) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);
        var (b, loteB, vehiculoB) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);
        var opA = await InsertarOperacionAsync(conexion, a, loteA, ct);

        await DebeViolarAsync(() => InsertarOperacionAsync(conexion, b, loteA, ct), "fk_derivacion_operaciones_diagnostico", PostgresErrorCodes.ForeignKeyViolation);
        await DebeViolarAsync(() => InsertarItemAsync(conexion, b, opA, vehiculoB, "ENVIADO", ct), "fk_derivaciones_operacion", PostgresErrorCodes.ForeignKeyViolation);
        await DebeViolarAsync(() => InsertarItemAsync(conexion, a, opA, vehiculoB, "ENVIADO", ct), "fk_derivaciones_vehiculo", PostgresErrorCodes.ForeignKeyViolation);
        await DebeViolarAsync(() => InsertarOperacionAsync(conexion, a, loteA, ct, tipo: "OTRA"), "ck_derivacion_operaciones_tipo");
        await DebeViolarAsync(() => InsertarOperacionAsync(conexion, a, loteA, ct, estado: "TERMINADA"), "ck_derivacion_operaciones_terminada");
        await DebeViolarAsync(() => InsertarOperacionAsync(conexion, a, loteA, ct, total: 1, creadas: 2), "ck_derivacion_operaciones_contadores");
        await DebeViolarAsync(() => InsertarOperacionAsync(conexion, a, loteA, ct, total: 0), "ck_derivacion_operaciones_total");
        await DebeViolarAsync(() => InsertarItemAsync(conexion, a, opA, vehiculoA, "RARO", ct), "ck_derivaciones_estado");
        await DebeViolarAsync(() => InsertarItemAsync(conexion, a, opA, vehiculoA, "CREADO", ct), "ck_derivaciones_creado");
        await DebeViolarAsync(() => InsertarItemAsync(conexion, a, opA, vehiculoA, "ENVIADO", ct, tramite: Guid.NewGuid()), "ck_derivaciones_creado");
        await DebeViolarAsync(() => InsertarItemAsync(conexion, a, opA, vehiculoA, "OMITIDO", ct), "ck_derivaciones_motivo");

        await InsertarItemAsync(conexion, a, opA, vehiculoA, "ENVIADO", ct);
        await DebeViolarAsync(
            () => EjecutarAsync(conexion, "DELETE FROM diagnostico.derivacion_operaciones WHERE id = @id", opA, ct),
            "fk_derivaciones_operacion",
            PostgresErrorCodes.ForeignKeyViolation);
        _ = loteB;
    }

    // Revisión 2026-11-11 (P4d): la FK al vehículo tiene índice propio sin filtro (el parcial de vivos no la cubre en un DELETE o
    // UPDATE del vehículo), y las columnas de usuario llevan su clasificación @pii:low.
    [Fact]
    public async Task P4d_indice_de_la_FK_al_vehiculo_y_usuarios_clasificados_pii_low()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);

        var indice = (await ListarAsync(conexion, "SELECT indexdef FROM pg_indexes WHERE schemaname = 'diagnostico' AND indexname = 'ix_derivaciones_vehiculo'", ct))
            .Should().ContainSingle("la FK fk_derivaciones_vehiculo (tenant_id, vehiculo_id) necesita un índice sin filtro").Subject;
        indice.Should().Contain("(tenant_id, vehiculo_id)").And.NotContain("WHERE").And.NotContain("UNIQUE");
        (await ListarAsync(conexion, "SELECT col_description('diagnostico.derivacion_operaciones'::regclass, a.attnum) FROM pg_attribute a WHERE a.attrelid = 'diagnostico.derivacion_operaciones'::regclass AND a.attname = 'usuario_id'", ct))
            .Should().ContainSingle().Which.Should().StartWith("@pii:low");
        (await ListarAsync(conexion, "SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name = 'derivaciones' AND column_name = 'reintentado_por'", ct))
            .Should().Equal(["YES"], "reintentar fallidas guarda quién reintentó el ítem (nulo si nadie)");
        (await ListarAsync(conexion, "SELECT col_description('diagnostico.derivaciones'::regclass, a.attnum) FROM pg_attribute a WHERE a.attrelid = 'diagnostico.derivaciones'::regclass AND a.attname = 'reintentado_por'", ct))
            .Should().ContainSingle().Which.Should().StartWith("@pii:low");
    }

    // Revisión 2026-11-11 (H4): el Down de P7b no puede volver a prohibir PLACA_MOTOCARRO con motocarros guardados; falla con un
    // mensaje que dice qué hacer, no con la violación genérica del CHECK.
    [Fact]
    public async Task Rollback_de_P7b_con_motocarros_falla_con_mensaje_claro()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (_, _, vehiculo) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);
        await EjecutarAsync(conexion, "UPDATE diagnostico.vehiculos SET tipo_identificador = 'PLACA_MOTOCARRO', identificador_norm = '123ABC', placa = '123ABC' WHERE id = @id", vehiculo, ct);

        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        var rollback = () => db.GetService<IMigrator>().MigrateAsync("20261111175412_P7a_DictamenV2RtmSoatPorClase", ct);
        var error = (await rollback.Should().ThrowAsync<PostgresException>()).Which;

        error.SqlState.Should().NotBe(PostgresErrorCodes.CheckViolation, "el rollback se detiene antes de reponer el CHECK");
        error.MessageText.Should().Contain("PLACA_MOTOCARRO").And.Contain("reclasific");
        (await ListarAsync(conexion, "SELECT tipo_identificador FROM diagnostico.vehiculos", ct)).Should().Equal("PLACA_MOTOCARRO");
    }

    private static async Task VerificarAsync(NpgsqlConnection conexion, CancellationToken ct)
    {
        (await ListarAsync(conexion, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'diagnostico'", ct))
            .Should().Contain(["derivacion_operaciones", "derivaciones"]);
        (await ListarAsync(conexion, "SELECT column_name FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name = 'vehiculos'", ct))
            .Should().Contain(["tramite_id", "tramite_numero", "tramite_creado_en"]);
        // Incremento 3 (P4c): el reconciliador re-publica una sola vez cada ítem colgado.
        (await ListarAsync(conexion, "SELECT column_default FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name = 'derivaciones' AND column_name = 'republicaciones' AND is_nullable = 'NO'", ct))
            .Should().ContainSingle().Which.Should().Contain("0");
        var indices = await ListarAsync(conexion, "SELECT indexname FROM pg_indexes WHERE schemaname = 'diagnostico' AND tablename IN ('derivaciones', 'derivacion_operaciones')", ct);
        indices.Should().Contain(["ux_derivaciones_vehiculo_vivo", "ix_derivaciones_pendientes", "ix_derivaciones_en_vuelo", "ix_derivaciones_operacion", "ix_derivacion_operaciones_lote"]);
        (await ListarAsync(conexion, "SELECT indexdef FROM pg_indexes WHERE schemaname = 'diagnostico' AND indexname = 'ux_derivaciones_vehiculo_vivo'", ct))
            .Single().Should().Contain("UNIQUE").And.Contain("PENDIENTE").And.Contain("ENVIADO").And.Contain("CREADO").And.NotContain("OMITIDO");
        (await ListarAsync(conexion, "SELECT obj_description('diagnostico.derivaciones'::regclass, 'pg_class')", ct)).Single().Should().NotBeNullOrWhiteSpace();
        (await ListarAsync(conexion, "SELECT obj_description('diagnostico.derivacion_operaciones'::regclass, 'pg_class')", ct)).Single().Should().NotBeNullOrWhiteSpace();
    }

    private static async Task<Guid> InsertarOperacionAsync(
        NpgsqlConnection conexion, Guid tenant, Guid lote, CancellationToken ct, string tipo = "INDIVIDUAL", string estado = "EN_CURSO", int total = 1, int creadas = 0)
    {
        var id = Guid.CreateVersion7();
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.derivacion_operaciones (id, tenant_id, diagnostico_id, tipo, tipo_tramite_codigo, usuario_id, estado, total, creadas, creada_en) " +
            "VALUES (@id, @t, @d, @tipo, 'TRASPASO_STANDARD', @u, @estado, @total, @creadas, now())",
            conexion);
        insertar.Parameters.AddWithValue("id", id);
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("d", lote);
        insertar.Parameters.AddWithValue("tipo", tipo);
        insertar.Parameters.AddWithValue("u", Guid.NewGuid());
        insertar.Parameters.AddWithValue("estado", estado);
        insertar.Parameters.AddWithValue("total", total);
        insertar.Parameters.AddWithValue("creadas", creadas);
        await insertar.ExecuteNonQueryAsync(ct);
        return id;
    }

    private static async Task InsertarItemAsync(
        NpgsqlConnection conexion, Guid tenant, Guid operacion, Guid vehiculo, string estado, CancellationToken ct, string? motivo = null, Guid? tramite = null)
    {
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.derivaciones (id, tenant_id, operacion_id, vehiculo_id, orden, estado, motivo_codigo, tramite_id, actualizado_en) " +
            "VALUES (@id, @t, @o, @v, 1, @estado, @motivo, @tramite, now())",
            conexion);
        insertar.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("o", operacion);
        insertar.Parameters.AddWithValue("v", vehiculo);
        insertar.Parameters.AddWithValue("estado", estado);
        insertar.Parameters.Add(new NpgsqlParameter("motivo", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)motivo ?? DBNull.Value });
        insertar.Parameters.Add(new NpgsqlParameter("tramite", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)tramite ?? DBNull.Value });
        await insertar.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<string>> ListarAsync(NpgsqlConnection conexion, string sql, CancellationToken ct)
    {
        var valores = new List<string>();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await using var fila = await comando.ExecuteReaderAsync(ct);
        while (await fila.ReadAsync(ct))
            valores.Add(fila.IsDBNull(0) ? string.Empty : fila.GetString(0));
        return valores;
    }

    private static async Task EjecutarAsync(NpgsqlConnection conexion, string sql, Guid id, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(sql, conexion);
        comando.Parameters.AddWithValue("id", id);
        await comando.ExecuteNonQueryAsync(ct);
    }

    private static async Task DebeViolarAsync(Func<Task> accion, string restriccion, string sqlState = PostgresErrorCodes.CheckViolation)
    {
        var error = (await accion.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(sqlState);
        error.ConstraintName.Should().Be(restriccion);
    }

    private static async Task<NpgsqlConnection> AbrirAsync(ApiDePrueba api, CancellationToken ct)
    {
        var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        return conexion;
    }

    private static async Task<(Guid Tenant, Guid Lote, Guid Vehiculo)> SembrarVehiculoAsync(NpgsqlConnection conexion, Guid tenant, CancellationToken ct)
    {
        var lote = Guid.CreateVersion7();
        await using (var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.diagnosticos (id, tenant_id, nombre, modo, alcance, configuracion, estado, usuario_creador_id, creado_en) " +
            "VALUES (@id, @t, 'Lote', 'SIMPLE', '[\"RUNT\"]'::jsonb, '{}'::jsonb, 'CREADO', @usuario, now())",
            conexion))
        {
            insertar.Parameters.AddWithValue("id", lote);
            insertar.Parameters.AddWithValue("t", tenant);
            insertar.Parameters.AddWithValue("usuario", Guid.NewGuid());
            await insertar.ExecuteNonQueryAsync(ct);
        }

        var vehiculo = Guid.CreateVersion7();
        await using (var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.vehiculos (id, tenant_id, diagnostico_id, identificador_original, identificador_norm, tipo_identificador, placa, orden) " +
            "VALUES (@id, @t, @d, 'TST491', 'TST491', 'PLACA_CARRO', 'TST491', 1)",
            conexion))
        {
            insertar.Parameters.AddWithValue("id", vehiculo);
            insertar.Parameters.AddWithValue("t", tenant);
            insertar.Parameters.AddWithValue("d", lote);
            await insertar.ExecuteNonQueryAsync(ct);
        }

        return (tenant, lote, vehiculo);
    }
}
