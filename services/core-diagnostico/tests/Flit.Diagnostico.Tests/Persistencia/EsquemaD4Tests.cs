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
/// HU #13846 (F07-H1) — migración <c>D4_Evidencias</c> sobre el schema migrado (base efímera de <see cref="ApiDePrueba"/>): la tabla
/// <c>evidencias</c> (FK compuestas con la empresa, RESTRICT, una sola vigente por lote, vehículo y tipo con
/// <c>NULLS NOT DISTINCT</c>, PDF ⇔ vehículo, LISTA ⇔ archivo, sin borrado lógico) y <c>archivo_local</c> (solo pruebas). Las reglas
/// las garantiza la base: se prueban por SQL directo. Placas y huellas ficticias.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EsquemaD4Tests
/// </code>
/// </example>
public sealed class EsquemaD4Tests
{
    private const string MigracionAnterior = "20261022172031_D3_LlenadoManual";
    private static readonly string Huella = new('c', 64);

    // DoD 1 — idempotente y reversible: el Down quita las tablas; el Up las vuelve a crear.
    [Fact]
    public async Task Migracion_D4_Evidencias_aplica_dos_veces_sin_error()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            await db.Database.MigrateAsync(ct);
            (await db.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
            var idempotente = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            await using var script = new NpgsqlCommand(idempotente, conexion);
            await script.ExecuteNonQueryAsync(ct);
        }

        await VerificarSchemaAsync(conexion, ct);

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior, ct);
            (await ListarAsync(conexion, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'diagnostico'", ct))
                .Should().NotContain(["evidencias", "archivo_local"]).And.Contain("consultas");
            await db.Database.MigrateAsync(ct);
        }

        await VerificarSchemaAsync(conexion, ct);
    }

    // AC5 (F07-C11) — una evidencia de B no se liga a un lote ni a un vehículo de A; RESTRICT impide borrar el lote con evidencias.
    [Fact]
    public async Task FK_compuesta_rechaza_evidencia_de_otra_empresa()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (a, loteA, vehiculoA) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);
        var (b, loteB, _) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);

        await DebeViolarAsync(
            () => InsertarAsync(conexion, b, loteA, null, "XLSX", ct),
            "fk_evidencias_diagnosticos",
            PostgresErrorCodes.ForeignKeyViolation);
        await DebeViolarAsync(
            () => InsertarAsync(conexion, b, loteB, vehiculoA, "PDF", ct),
            "fk_evidencias_vehiculos",
            PostgresErrorCodes.ForeignKeyViolation);

        // La de la propia empresa sí se liga, y RESTRICT impide borrar el vehículo con su PDF o el lote (sin vehículos) con su Excel.
        await InsertarAsync(conexion, a, loteA, vehiculoA, "PDF", ct);
        await DebeViolarAsync(
            () => EjecutarAsync(conexion, "DELETE FROM diagnostico.vehiculos WHERE id = @id", vehiculoA, ct),
            "fk_evidencias_vehiculos",
            PostgresErrorCodes.ForeignKeyViolation);
        var loteSinVehiculos = await InsertarLoteAsync(conexion, a, ct);
        await InsertarAsync(conexion, a, loteSinVehiculos, null, "XLSX", ct);
        await DebeViolarAsync(
            () => EjecutarAsync(conexion, "DELETE FROM diagnostico.diagnosticos WHERE id = @id", loteSinVehiculos, ct),
            "fk_evidencias_diagnosticos",
            PostgresErrorCodes.ForeignKeyViolation);
    }

    // AC4 (F07-C8) — dos vigentes del mismo lote, vehículo y tipo: la segunda se rechaza, también sin vehículo (NULLS NOT DISTINCT);
    // las no vigentes se acumulan (R-18) y otro tipo u otro vehículo sí caben.
    [Fact]
    public async Task Una_sola_vigente_por_lote_vehiculo_y_tipo_tambien_sin_vehiculo()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote, vehiculo) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);
        var otroVehiculo = await InsertarVehiculoAsync(conexion, tenant, lote, "TST902", 2, ct);

        await InsertarAsync(conexion, tenant, lote, vehiculo, "PDF", ct);
        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, vehiculo, "PDF", ct), "ux_evidencias_vigente", PostgresErrorCodes.UniqueViolation);
        await InsertarAsync(conexion, tenant, lote, otroVehiculo, "PDF", ct);

        await InsertarAsync(conexion, tenant, lote, null, "XLSX", ct);
        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, null, "XLSX", ct), "ux_evidencias_vigente", PostgresErrorCodes.UniqueViolation);
        await InsertarAsync(conexion, tenant, lote, null, "ZIP", ct, estado: "PENDIENTE", archivo: null);
        await DebeViolarAsync(
            () => InsertarAsync(conexion, tenant, lote, null, "ZIP", ct, estado: "PENDIENTE", archivo: null),
            "ux_evidencias_vigente",
            PostgresErrorCodes.UniqueViolation);

        // Las reemplazadas se conservan: varias no vigentes con la misma clave.
        await InsertarAsync(conexion, tenant, lote, vehiculo, "PDF", ct, vigente: false);
        await InsertarAsync(conexion, tenant, lote, vehiculo, "PDF", ct, vigente: false);
        await InsertarAsync(conexion, tenant, lote, null, "XLSX", ct, vigente: false);
    }

    // CHECK de la tabla — PDF si y solo si hay vehículo; LISTA si y solo si hay archivo (con tamaño y fecha); huella SHA-256 hex.
    [Fact]
    public async Task Pdf_exige_vehiculo_y_lista_exige_archivo()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote, vehiculo) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);

        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, null, "PDF", ct), "ck_evidencias_pdf_vehiculo");
        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, vehiculo, "XLSX", ct), "ck_evidencias_pdf_vehiculo");
        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, vehiculo, "PDF", ct, archivo: null), "ck_evidencias_lista_archivo");
        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, null, "ZIP", ct, estado: "PENDIENTE"), "ck_evidencias_lista_archivo");
        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, vehiculo, "PDF", ct, tamano: null), "ck_evidencias_lista_archivo");
        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, null, "CSV", ct), "ck_evidencias_tipo");
        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, null, "ZIP", ct, estado: "BORRADA", archivo: null), "ck_evidencias_estado");
        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, vehiculo, "PDF", ct, huella: new string('G', 64)), "ck_evidencias_huella");
        await DebeViolarAsync(
            () => InsertarAsync(conexion, tenant, lote, null, "ZIP", ct, estado: "PENDIENTE", archivo: null, tipoError: "GENERACION_FALLIDA"),
            "ck_evidencias_tipo_error");

        await InsertarAsync(conexion, tenant, lote, vehiculo, "PDF", ct);
        await InsertarAsync(conexion, tenant, lote, null, "ZIP", ct, estado: "ERROR", archivo: null, tipoError: "GENERACION_FALLIDA", vigente: false);
        await InsertarAsync(conexion, tenant, lote, null, "ZIP", ct, estado: "GENERANDO", archivo: null);
    }

    private static async Task VerificarSchemaAsync(NpgsqlConnection conexion, CancellationToken ct)
    {
        var columnas = await ListarAsync(
            conexion,
            "SELECT table_name || '.' || column_name || ' ' || data_type || ' ' || is_nullable FROM information_schema.columns " +
            "WHERE table_schema = 'diagnostico' AND table_name IN ('evidencias', 'archivo_local')",
            ct);
        columnas.Should().Contain(
        [
            "evidencias.id uuid NO", "evidencias.tenant_id uuid NO", "evidencias.diagnostico_id uuid NO", "evidencias.vehiculo_id uuid YES",
            "evidencias.tipo character varying NO", "evidencias.huella character NO", "evidencias.estado character varying NO",
            "evidencias.archivo_ref character varying YES", "evidencias.tamano_bytes bigint YES", "evidencias.solicitado_por uuid NO",
            "evidencias.empresa_nombre character varying YES", "evidencias.vigente boolean NO", "evidencias.intentos integer NO",
            "evidencias.lease_hasta timestamp with time zone YES", "evidencias.tipo_error character varying YES",
            "evidencias.creado_en timestamp with time zone NO", "evidencias.generado_en timestamp with time zone YES",
            "archivo_local.id uuid NO", "archivo_local.tenant_id uuid NO", "archivo_local.contenido bytea NO",
        ]);
        columnas.Where(c => c.StartsWith("evidencias.", StringComparison.Ordinal))
            .Should().HaveCount(17, "sin borrado lógico ni origen de las celdas (R-18, R-20)")
            .And.NotContain(c => c.Contains("deleted", StringComparison.Ordinal) || c.Contains("origen", StringComparison.Ordinal));

        var restricciones = await ListarAsync(
            conexion,
            "SELECT conrelid::regclass::text || ' ' || conname FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = 'diagnostico'",
            ct);
        restricciones.Should().Contain(
        [
            "diagnostico.evidencias pk_evidencias", "diagnostico.evidencias uq_evidencias_tenant_id",
            "diagnostico.evidencias fk_evidencias_diagnosticos", "diagnostico.evidencias fk_evidencias_vehiculos",
            "diagnostico.archivo_local pk_archivo_local",
        ]);
        (await ListarAsync(
                conexion,
                "SELECT conname || ' ' || confdeltype::text FROM pg_constraint WHERE contype = 'f' AND conname IN ('fk_evidencias_diagnosticos', 'fk_evidencias_vehiculos')",
                ct))
            .Should().HaveCount(2).And.OnlyContain(f => f.EndsWith(" r", StringComparison.Ordinal), "RESTRICT (ADR-0072)");

        var indices = await ListarAsync(conexion, "SELECT indexname || ' ' || indexdef FROM pg_indexes WHERE schemaname = 'diagnostico'", ct);
        indices.Should().Contain(i => i.StartsWith("ux_evidencias_vigente ", StringComparison.Ordinal)
            && i.Contains("UNIQUE", StringComparison.Ordinal) && i.Contains("(tenant_id, diagnostico_id, vehiculo_id, tipo) NULLS NOT DISTINCT", StringComparison.Ordinal)
            && i.Contains("WHERE vigente", StringComparison.Ordinal));
        indices.Should().Contain(i => i.StartsWith("ix_evidencias_cola ", StringComparison.Ordinal)
            && i.Contains("(estado, lease_hasta)", StringComparison.Ordinal) && i.Contains("'ZIP'", StringComparison.Ordinal));
        indices.Should().Contain(i => i.StartsWith("ix_evidencias_diagnostico ", StringComparison.Ordinal) && i.Contains("(tenant_id, diagnostico_id", StringComparison.Ordinal));
        indices.Should().Contain(i => i.StartsWith("ix_evidencias_vehiculo ", StringComparison.Ordinal) && i.Contains("(tenant_id, vehiculo_id)", StringComparison.Ordinal));
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
        var lote = await InsertarLoteAsync(conexion, tenant, ct);
        return (tenant, lote, await InsertarVehiculoAsync(conexion, tenant, lote, "TST901", 1, ct));
    }

    private static async Task<Guid> InsertarLoteAsync(NpgsqlConnection conexion, Guid tenant, CancellationToken ct)
    {
        var lote = Guid.CreateVersion7();
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.diagnosticos (id, tenant_id, nombre, modo, alcance, configuracion, estado, usuario_creador_id, creado_en) " +
            "VALUES (@id, @t, 'Lote', 'SIMPLE', '[\"RUNT\"]'::jsonb, '{}'::jsonb, 'CREADO', @usuario, now())",
            conexion);
        insertar.Parameters.AddWithValue("id", lote);
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("usuario", Guid.NewGuid());
        await insertar.ExecuteNonQueryAsync(ct);
        return lote;
    }

    private static async Task<Guid> InsertarVehiculoAsync(NpgsqlConnection conexion, Guid tenant, Guid lote, string placa, int orden, CancellationToken ct)
    {
        var vehiculo = Guid.CreateVersion7();
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.vehiculos (id, tenant_id, diagnostico_id, identificador_original, identificador_norm, tipo_identificador, placa, orden) " +
            "VALUES (@id, @t, @d, @p, @p, 'PLACA_CARRO', @p, @o)",
            conexion);
        insertar.Parameters.AddWithValue("id", vehiculo);
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("d", lote);
        insertar.Parameters.AddWithValue("p", placa);
        insertar.Parameters.AddWithValue("o", orden);
        await insertar.ExecuteNonQueryAsync(ct);
        return vehiculo;
    }

    private static async Task InsertarAsync(
        NpgsqlConnection conexion, Guid tenant, Guid lote, Guid? vehiculo, string tipo, CancellationToken ct,
        string estado = "LISTA", string? archivo = "mem:prueba", long? tamano = 10, bool vigente = true, string? huella = null, string? tipoError = null)
    {
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.evidencias (id, tenant_id, diagnostico_id, vehiculo_id, tipo, huella, estado, archivo_ref, tamano_bytes, " +
            "solicitado_por, vigente, intentos, tipo_error, creado_en, generado_en) " +
            "VALUES (@id, @t, @d, @v, @tipo, @huella, @estado, @archivo, @tamano, @usuario, @vigente, 0, @tipoError, now(), " +
            "CASE WHEN @estado = 'LISTA' THEN now() END)",
            conexion);
        insertar.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("d", lote);
        insertar.Parameters.Add(new NpgsqlParameter("v", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)vehiculo ?? DBNull.Value });
        insertar.Parameters.AddWithValue("tipo", tipo);
        insertar.Parameters.AddWithValue("huella", huella ?? Huella);
        insertar.Parameters.Add(new NpgsqlParameter("estado", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = estado });
        insertar.Parameters.Add(new NpgsqlParameter("archivo", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)archivo ?? DBNull.Value });
        insertar.Parameters.Add(new NpgsqlParameter("tamano", NpgsqlTypes.NpgsqlDbType.Bigint) { Value = (object?)tamano ?? DBNull.Value });
        insertar.Parameters.AddWithValue("usuario", Guid.NewGuid());
        insertar.Parameters.AddWithValue("vigente", vigente);
        insertar.Parameters.Add(new NpgsqlParameter("tipoError", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)tipoError ?? DBNull.Value });
        await insertar.ExecuteNonQueryAsync(ct);
    }
}
