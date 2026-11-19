using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Flit.Diagnostico.Tests.Persistencia;

/// <summary>
/// HU #14563 AC23 (cierre BE; ADR-0084, ADR-0086) — migración <c>R9_AjusteConTramite</c> sobre el schema migrado: la columna
/// <c>dictamenes.ajuste_con_tramite</c> (boolean, NOT NULL, default false) registra en la versión de auditoría que el vehículo ya
/// tenía trámite cuando soporte confirmó el ajuste con <c>confirmar_con_tramite</c>. Solo un ajuste puede llevarla
/// (<c>ck_dictamenes_ajuste_con_tramite</c>). El Down la retira y se bloquea si hay versiones marcadas (bajarla borraría auditoría).
/// Las reglas las garantiza la base: se prueban por SQL directo. Placas ficticias.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EsquemaR9Tests
/// </code>
/// </example>
public sealed class EsquemaR9Tests
{
    private const string MigracionAnterior = "20261113054446_R1_VersionFilaTablero";

    // Columna con su tipo, NOT NULL y default false; el Down la quita y el Up la vuelve a crear.
    [Fact]
    public async Task Migracion_R9_crea_la_columna_y_es_reversible()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);

        (await ColumnaAsync(conexion, ct)).Should().Be("boolean:NO:false");

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior, ct);
            (await ColumnaAsync(conexion, ct)).Should().BeNull("el Down retira la columna");
            await db.Database.MigrateAsync(ct);
            (await db.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
        }

        (await ColumnaAsync(conexion, ct)).Should().Be("boolean:NO:false");
    }

    // Solo un ajuste (analisis_override) puede registrar el trámite; la automática nace en false sin enviarla.
    [Fact]
    public async Task Ck_ajuste_con_tramite_solo_en_un_ajuste()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, vehiculo) = await SembrarVehiculoAsync(conexion, ct);

        var error = (await FluentActions.Awaiting(() => InsertarAsync(conexion, tenant, vehiculo, 1, ajuste: false, conTramite: true, ct))
            .Should().ThrowAsync<PostgresException>()).Which;
        (error.SqlState, error.ConstraintName).Should().Be((PostgresErrorCodes.CheckViolation, "ck_dictamenes_ajuste_con_tramite"));

        await InsertarAsync(conexion, tenant, vehiculo, 1, ajuste: false, conTramite: null, ct);
        await InsertarAsync(conexion, tenant, vehiculo, 2, ajuste: true, conTramite: true, ct);
        (await LeerAsync(conexion, vehiculo, ct)).Should().Equal(false, true);
    }

    // El Down se bloquea con un mensaje claro si alguna versión registra el trámite: bajar la columna borraría auditoría.
    [Fact]
    public async Task Down_de_R9_se_bloquea_si_hay_ajustes_con_tramite()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, vehiculo) = await SembrarVehiculoAsync(conexion, ct);
        await InsertarAsync(conexion, tenant, vehiculo, 1, ajuste: false, conTramite: null, ct);
        await InsertarAsync(conexion, tenant, vehiculo, 2, ajuste: true, conTramite: true, ct);

        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        var error = (await FluentActions.Awaiting(() => db.GetService<IMigrator>().MigrateAsync(MigracionAnterior, ct))
            .Should().ThrowAsync<PostgresException>()).Which;
        error.MessageText.Should().Contain("R9_AjusteConTramite");
        (await ColumnaAsync(conexion, ct)).Should().Be("boolean:NO:false", "el rollback fallido no tocó la columna");
        (await db.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
    }

    private static async Task InsertarAsync(
        NpgsqlConnection conexion, Guid tenant, Guid vehiculo, int version, bool ajuste, bool? conTramite, CancellationToken ct)
    {
        var columnas = "id, tenant_id, vehiculo_id, version, vigente, veredicto, alertamientos, catalogo_version, redactado_por, " +
            "estado_redaccion, redaccion_intentos, analisis_override, usuario_override_id, override_en, creado_en, ajustado_desde_version";
        var valores = "@id, @t, @v, @version, false, 'VIABLE', '[]'::jsonb, 1, 'PLANTILLA', 'PENDIENTE', 0, @ajuste, @usuario, @en, now(), @desde";
        if (conTramite is not null)
        {
            columnas += ", ajuste_con_tramite";
            valores += ", @tramite";
        }

        await using var insertar = new NpgsqlCommand($"INSERT INTO diagnostico.dictamenes ({columnas}) VALUES ({valores})", conexion);
        insertar.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("v", vehiculo);
        insertar.Parameters.AddWithValue("version", version);
        insertar.Parameters.AddWithValue("ajuste", ajuste);
        insertar.Parameters.Add(new NpgsqlParameter("usuario", NpgsqlDbType.Uuid) { Value = ajuste ? Guid.NewGuid() : DBNull.Value });
        insertar.Parameters.Add(new NpgsqlParameter("en", NpgsqlDbType.TimestampTz) { Value = ajuste ? DateTimeOffset.UtcNow : DBNull.Value });
        insertar.Parameters.Add(new NpgsqlParameter("desde", NpgsqlDbType.Integer) { Value = ajuste ? version - 1 : DBNull.Value });
        if (conTramite is { } marca)
            insertar.Parameters.AddWithValue("tramite", marca);
        await insertar.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<bool>> LeerAsync(NpgsqlConnection conexion, Guid vehiculo, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(
            "SELECT ajuste_con_tramite FROM diagnostico.dictamenes WHERE vehiculo_id = @v ORDER BY version", conexion);
        comando.Parameters.AddWithValue("v", vehiculo);
        var valores = new List<bool>();
        await using var fila = await comando.ExecuteReaderAsync(ct);
        while (await fila.ReadAsync(ct))
            valores.Add(fila.GetBoolean(0));
        return valores;
    }

    /// <summary><c>tipo:nulable:default</c> de la columna, o null si no existe.</summary>
    private static async Task<string?> ColumnaAsync(NpgsqlConnection conexion, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(
            "SELECT data_type || ':' || is_nullable || ':' || coalesce(column_default, '') FROM information_schema.columns " +
            "WHERE table_schema = 'diagnostico' AND table_name = 'dictamenes' AND column_name = 'ajuste_con_tramite'",
            conexion);
        return await comando.ExecuteScalarAsync(ct) as string;
    }

    private static async Task<NpgsqlConnection> AbrirAsync(ApiDePrueba api, CancellationToken ct)
    {
        var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        return conexion;
    }

    private static async Task<(Guid Tenant, Guid Vehiculo)> SembrarVehiculoAsync(NpgsqlConnection conexion, CancellationToken ct)
    {
        var tenant = Guid.NewGuid();
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
            "VALUES (@id, @t, @d, 'TST923', 'TST923', 'PLACA_CARRO', 'TST923', 1)",
            conexion))
        {
            insertar.Parameters.AddWithValue("id", vehiculo);
            insertar.Parameters.AddWithValue("t", tenant);
            insertar.Parameters.AddWithValue("d", lote);
            await insertar.ExecuteNonQueryAsync(ct);
        }

        return (tenant, vehiculo);
    }
}
