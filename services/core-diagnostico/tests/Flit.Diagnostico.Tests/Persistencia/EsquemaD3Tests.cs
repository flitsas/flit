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
/// HU #13790 (F05-H1) — migración <c>D3_PersonasVehiculo</c> sobre el schema migrado (base efímera de <see cref="ApiDePrueba"/>):
/// la tabla hija <c>personas_vehiculo</c> (FK compuesta con la empresa, RESTRICT, único por vehículo, rol y orden, documento
/// cifrado y sin nombres) y <c>intentos_consulta.persona_id</c>. Las reglas las garantiza la base: se prueban por SQL directo.
/// Placas y bytes de documento son ficticios.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EsquemaD3Tests
/// </code>
/// </example>
public sealed class EsquemaD3Tests
{
    private const string MigracionAnterior = "20261021214816_D2_ReprocesoUsuarioCelda";

    // DoD 1 — idempotente y reversible: el Down quita la tabla y la columna; el Up las vuelve a crear.
    [Fact]
    public async Task Migracion_D3_PersonasVehiculo_aplica_dos_veces_sin_error()
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
                .Should().NotContain("personas_vehiculo").And.Contain("intentos_consulta");
            (await ListarAsync(conexion, "SELECT column_name FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name = 'intentos_consulta'", ct))
                .Should().NotContain("persona_id");
            await db.Database.MigrateAsync(ct);
        }

        await VerificarSchemaAsync(conexion, ct);
    }

    // AC4 (F05-C14) — una persona de B no cuelga de un vehículo de A, ni un intento de B apunta a una persona de A.
    [Fact]
    public async Task FK_compuesta_rechaza_persona_de_otra_empresa()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (a, loteA, vehiculoA) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);
        var (b, loteB, vehiculoB) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);

        await DebeViolarAsync(
            () => InsertarPersonaAsync(conexion, b, vehiculoA, "PROPIETARIO", 1, ct),
            "fk_personas_vehiculo_vehiculos",
            PostgresErrorCodes.ForeignKeyViolation);

        var personaA = await InsertarPersonaAsync(conexion, a, vehiculoA, "PROPIETARIO", 1, ct);
        var celdaB = await InsertarCeldaAsync(conexion, b, loteB, vehiculoB, ct);
        await DebeViolarAsync(
            () => InsertarIntentoAsync(conexion, b, loteB, vehiculoB, celdaB, personaA, ct),
            "fk_intentos_consulta_personas_vehiculo",
            PostgresErrorCodes.ForeignKeyViolation);

        // La de la propia empresa sí se liga, y RESTRICT impide borrar la persona con intentos ni el vehículo con personas.
        var celdaA = await InsertarCeldaAsync(conexion, a, loteA, vehiculoA, ct);
        await InsertarIntentoAsync(conexion, a, loteA, vehiculoA, celdaA, personaA, ct);
        await DebeViolarAsync(
            () => EjecutarAsync(conexion, "DELETE FROM diagnostico.personas_vehiculo WHERE id = @id", personaA, ct),
            "fk_intentos_consulta_personas_vehiculo",
            PostgresErrorCodes.ForeignKeyViolation);
        await EjecutarAsync(conexion, "DELETE FROM diagnostico.intentos_consulta WHERE persona_id = @id", personaA, ct);
        await EjecutarAsync(conexion, "DELETE FROM diagnostico.consultas WHERE vehiculo_id = @id", vehiculoA, ct);
        await DebeViolarAsync(
            () => EjecutarAsync(conexion, "DELETE FROM diagnostico.vehiculos WHERE id = @id", vehiculoA, ct),
            "fk_personas_vehiculo_vehiculos",
            PostgresErrorCodes.ForeignKeyViolation);
    }

    // AC5 (F05-C3) — dos filas con el mismo vehículo, rol y orden: la segunda se rechaza; otro rol u otro orden sí caben.
    [Fact]
    public async Task Unico_vehiculo_rol_orden_rechaza_duplicado()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, _, vehiculo) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);

        await InsertarPersonaAsync(conexion, tenant, vehiculo, "PROPIETARIO", 1, ct);
        await DebeViolarAsync(
            () => InsertarPersonaAsync(conexion, tenant, vehiculo, "PROPIETARIO", 1, ct),
            "ux_personas_vehiculo_rol_orden",
            PostgresErrorCodes.UniqueViolation);

        await InsertarPersonaAsync(conexion, tenant, vehiculo, "PROPIETARIO", 2, ct);
        await InsertarPersonaAsync(conexion, tenant, vehiculo, "LOCATARIO", 1, ct);
        await DebeViolarAsync(() => InsertarPersonaAsync(conexion, tenant, vehiculo, "CONDUCTOR", 1, ct), "ck_personas_vehiculo_rol");
        await DebeViolarAsync(() => InsertarPersonaAsync(conexion, tenant, vehiculo, "LOCATARIO", 0, ct), "ck_personas_vehiculo_orden");
        await DebeViolarAsync(
            () => InsertarPersonaAsync(conexion, tenant, vehiculo, "PROPIETARIO", 3, ct, origen: "DECLARADO_CLIENTE"),
            "ck_personas_vehiculo_declarado_solo_locatario");
    }

    private static async Task VerificarSchemaAsync(NpgsqlConnection conexion, CancellationToken ct)
    {
        var columnas = await ListarAsync(
            conexion,
            "SELECT table_name || '.' || column_name || ' ' || data_type || ' ' || is_nullable FROM information_schema.columns " +
            "WHERE table_schema = 'diagnostico' AND table_name IN ('personas_vehiculo', 'intentos_consulta')",
            ct);
        columnas.Should().Contain(
        [
            "personas_vehiculo.id uuid NO", "personas_vehiculo.tenant_id uuid NO", "personas_vehiculo.vehiculo_id uuid NO",
            "personas_vehiculo.rol character varying NO", "personas_vehiculo.orden integer NO", "personas_vehiculo.doc_tipo character varying NO",
            "personas_vehiculo.doc_cifrado bytea NO", "personas_vehiculo.origen character varying NO",
            "personas_vehiculo.creado_en timestamp with time zone NO", "intentos_consulta.persona_id uuid YES",
        ]);
        columnas.Where(c => c.StartsWith("personas_vehiculo.", StringComparison.Ordinal))
            .Should().HaveCount(9, "sin nombres ni borrado lógico").And.NotContain(c => c.Contains("nombre", StringComparison.Ordinal));

        (await ListarAsync(
                conexion,
                "SELECT col_description('diagnostico.personas_vehiculo'::regclass, ordinal_position) FROM information_schema.columns " +
                "WHERE table_schema = 'diagnostico' AND table_name = 'personas_vehiculo' AND column_name = 'doc_cifrado'",
                ct))
            .Should().ContainSingle().Which.Should().StartWith("@pii:high");

        var restricciones = await ListarAsync(
            conexion,
            "SELECT conrelid::regclass::text || ' ' || conname FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = 'diagnostico'",
            ct);
        restricciones.Should().Contain(
        [
            "diagnostico.personas_vehiculo pk_personas_vehiculo", "diagnostico.personas_vehiculo uq_personas_vehiculo_tenant_id",
            "diagnostico.personas_vehiculo fk_personas_vehiculo_vehiculos", "diagnostico.intentos_consulta fk_intentos_consulta_personas_vehiculo",
        ]);
        (await ListarAsync(
                conexion,
                "SELECT conname || ' ' || confdeltype::text FROM pg_constraint WHERE contype = 'f' AND conname IN ('fk_personas_vehiculo_vehiculos', 'fk_intentos_consulta_personas_vehiculo')",
                ct))
            .Should().HaveCount(2).And.OnlyContain(f => f.EndsWith(" r", StringComparison.Ordinal), "RESTRICT (ADR-0072)");

        var indices = await ListarAsync(conexion, "SELECT indexname || ' ' || indexdef FROM pg_indexes WHERE schemaname = 'diagnostico'", ct);
        indices.Should().Contain(i => i.StartsWith("ux_personas_vehiculo_rol_orden ", StringComparison.Ordinal)
            && i.Contains("UNIQUE", StringComparison.Ordinal) && i.Contains("(tenant_id, vehiculo_id, rol, orden)", StringComparison.Ordinal));
        indices.Should().Contain(i => i.StartsWith("ix_intentos_consulta_persona ", StringComparison.Ordinal)
            && i.Contains("(tenant_id, persona_id)", StringComparison.Ordinal));
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
                         "VALUES (@id, @t, @d, 'TST901', 'TST901', 'PLACA_CARRO', 'TST901', 1)",
                         conexion))
        {
            insertar.Parameters.AddWithValue("id", vehiculo);
            insertar.Parameters.AddWithValue("t", tenant);
            insertar.Parameters.AddWithValue("d", lote);
            await insertar.ExecuteNonQueryAsync(ct);
        }

        return (tenant, lote, vehiculo);
    }

    private static async Task<Guid> InsertarPersonaAsync(
        NpgsqlConnection conexion, Guid tenant, Guid vehiculo, string rol, int orden, CancellationToken ct, string origen = "PROVEEDOR")
    {
        var id = Guid.CreateVersion7();
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.personas_vehiculo (id, tenant_id, vehiculo_id, rol, orden, doc_tipo, doc_cifrado, origen, creado_en) " +
            "VALUES (@id, @t, @v, @rol, @orden, 'CC', @doc, @origen, now())",
            conexion);
        insertar.Parameters.AddWithValue("id", id);
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("v", vehiculo);
        insertar.Parameters.AddWithValue("rol", rol);
        insertar.Parameters.AddWithValue("orden", orden);
        insertar.Parameters.AddWithValue("doc", new byte[] { 0x01, 0x02, 0x03 });
        insertar.Parameters.AddWithValue("origen", origen);
        await insertar.ExecuteNonQueryAsync(ct);
        return id;
    }

    private static async Task<Guid> InsertarCeldaAsync(NpgsqlConnection conexion, Guid tenant, Guid lote, Guid vehiculo, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.consultas (id, tenant_id, diagnostico_id, vehiculo_id, fuente, estado, actualizado_en, version_tablero) " +
            "VALUES (@id, @t, @d, @v, 'RUNT_PROPIETARIO', 'PENDIENTE', now(), 0)",
            conexion);
        insertar.Parameters.AddWithValue("id", id);
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("d", lote);
        insertar.Parameters.AddWithValue("v", vehiculo);
        await insertar.ExecuteNonQueryAsync(ct);
        return id;
    }

    private static async Task InsertarIntentoAsync(NpgsqlConnection conexion, Guid tenant, Guid lote, Guid vehiculo, Guid celda, Guid persona, CancellationToken ct)
    {
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.intentos_consulta (id, tenant_id, diagnostico_id, vehiculo_id, consulta_id, persona_id, fuente, disparador, inicio, fin, estado) " +
            "VALUES (@id, @t, @d, @v, @c, @p, 'RUNT_PROPIETARIO', 'AUTO', now(), now(), 'SUCCESS')",
            conexion);
        insertar.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("d", lote);
        insertar.Parameters.AddWithValue("v", vehiculo);
        insertar.Parameters.AddWithValue("c", celda);
        insertar.Parameters.AddWithValue("p", persona);
        await insertar.ExecuteNonQueryAsync(ct);
    }
}
