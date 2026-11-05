using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
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
/// HU #13738 (F01-H1) — <c>diagnostico.diagnosticos</c> (cabecera) y <c>diagnostico.historial_estado_diagnostico</c> sobre el
/// schema migrado (base efímera de <see cref="ApiDePrueba"/>). Las reglas las garantiza la base: se prueban por SQL directo.
/// </summary>
public sealed class CabeceraDiagnosticoSchemaTests
{
    private const string MigracionAnterior = "20261021083622_D1_ConfiguracionEmpresa";

    // Escenario 1 — migración idempotente y reversible (F01-C8).
    [Fact]
    public async Task Migraciones_aplican_dos_veces_y_crean_tablas_checks_e_indices()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        await using var conexion = await AbrirAsync(api, ct);

        // La API ya migró al arrancar: la segunda corrida y el script idempotente no fallan ni cambian nada.
        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            await db.Database.MigrateAsync(ct);
            (await db.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
            var idempotente = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
            await using var script = new NpgsqlCommand(idempotente, conexion);
            await script.ExecuteNonQueryAsync(ct);
        }

        await VerificarSchemaAsync(conexion, ct);

        // Reversible: el Down deja la base como estaba en la migración anterior y el Up la vuelve a crear.
        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior, ct);
            (await TablasAsync(conexion, ct)).Should().NotContain(["diagnosticos", "historial_estado_diagnostico"]);
            await db.Database.MigrateAsync(ct);
        }

        await VerificarSchemaAsync(conexion, ct);
    }

    // Escenario 2 — Con Análisis IA sin tipo de trámite rechazado por la base (F01-C5).
    [Fact]
    public async Task Check_rechaza_con_analisis_sin_tipo_de_tramite()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var tenant = Guid.NewGuid();

        var sinTipo = () => InsertarCabeceraAsync(conexion, tenant, ct, modo: "CON_ANALISIS", tipo: null);
        var error = (await sinTipo.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        error.ConstraintName.Should().Be("ck_tipo_con_analisis");

        // Con tipo sí cabe, y el modo SIMPLE no lo exige.
        await InsertarCabeceraAsync(conexion, tenant, ct, modo: "CON_ANALISIS", tipo: "TRASPASO");
        await InsertarCabeceraAsync(conexion, tenant, ct, modo: "SIMPLE", tipo: null);
    }

    // DoD — los CHECK de estado, modo y disparador rechazan un valor fuera del enum.
    [Fact]
    public async Task Check_rechaza_valores_fuera_del_enum()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var tenant = Guid.NewGuid();

        await DebeViolarAsync(() => InsertarCabeceraAsync(conexion, tenant, ct, estado: "BORRADOR"), "ck_diagnosticos_estado");
        await DebeViolarAsync(() => InsertarCabeceraAsync(conexion, tenant, ct, modo: "PREMIUM"), "ck_diagnosticos_modo");

        var diagnostico = await InsertarCabeceraAsync(conexion, tenant, ct);
        await DebeViolarAsync(() => InsertarHistorialAsync(conexion, tenant, diagnostico, ct, estadoNuevo: "BORRADOR"), "ck_historial_estado_diagnostico_estado_nuevo");
        await DebeViolarAsync(() => InsertarHistorialAsync(conexion, tenant, diagnostico, ct, estadoAnterior: "BORRADOR"), "ck_historial_estado_diagnostico_estado_anterior");
        await DebeViolarAsync(() => InsertarHistorialAsync(conexion, tenant, diagnostico, ct, disparador: "OTRO"), "ck_historial_estado_diagnostico_disparador");
        await InsertarHistorialAsync(conexion, tenant, diagnostico, ct);
    }

    // El alcance es un arreglo de fuentes conocidas que siempre incluye RUNT; configuración y estimado son objetos JSON.
    [Fact]
    public async Task Check_rechaza_alcance_sin_runt_o_con_fuente_desconocida_y_json_que_no_es_objeto()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var tenant = Guid.NewGuid();

        await DebeViolarAsync(() => InsertarCabeceraAsync(conexion, tenant, ct, alcance: "[\"SIMIT_VEHICULO\"]"), "ck_diagnosticos_alcance_valido");
        await DebeViolarAsync(() => InsertarCabeceraAsync(conexion, tenant, ct, alcance: "[\"RUNT\", \"FASECOLDA\"]"), "ck_diagnosticos_alcance_valido");
        await DebeViolarAsync(() => InsertarCabeceraAsync(conexion, tenant, ct, configuracion: "[]"), "ck_diagnosticos_configuracion_objeto");
        await DebeViolarAsync(() => InsertarCabeceraAsync(conexion, tenant, ct, estimado: "5"), "ck_diagnosticos_estimado_consultas_objeto");
        await InsertarCabeceraAsync(conexion, tenant, ct, alcance: "[\"RUNT\", \"SIMIT_VEHICULO\"]", estimado: "{\"RUNT\": 3}");
    }

    // Escenario 3 — aislamiento por compañía (F01-C8): la cabecera y su historial de A no se ven con la empresa de B.
    [Fact]
    public async Task Cabecera_de_A_no_se_devuelve_con_la_empresa_B()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var creador = Guid.NewGuid();
        var cabecera = new CabeceraDiagnostico
        {
            Nombre = "Lote de prueba",
            Modo = ModoDiagnostico.ConAnalisis,
            TipoTramiteCodigo = "TRASPASO",
            Alcance = [Fuente.Runt, Fuente.SimitVehiculo],
            Configuracion = "{\"limite_vehiculos_lote\": 500}",
            UsuarioCreadorId = creador,
            CreadoEn = DateTimeOffset.UtcNow,
        };

        await using (var db = empresas.A.Db())
        {
            db.Add(cabecera);
            db.Add(new HistorialEstadoDiagnostico
            {
                DiagnosticoId = cabecera.Id,
                EstadoNuevo = EstadoDiagnostico.Creado,
                Disparador = DisparadorEstado.Manual,
                UsuarioId = creador,
                En = cabecera.CreadoEn,
            });
            await db.SaveChangesAsync(ct);
        }

        await using (var deA = empresas.A.Db())
        {
            var leida = await deA.Set<CabeceraDiagnostico>().AsNoTracking().SingleAsync(ct);
            leida.TenantId.Should().Be(empresas.A.Id);
            leida.Modo.Should().Be(ModoDiagnostico.ConAnalisis);
            leida.Alcance.Should().Equal(Fuente.Runt, Fuente.SimitVehiculo);
            leida.Estado.Should().Be(EstadoDiagnostico.Creado);
            leida.VersionTablero.Should().Be(0);
            var historial = await deA.Set<HistorialEstadoDiagnostico>().AsNoTracking().SingleAsync(ct);
            historial.EstadoAnterior.Should().BeNull();
            historial.Disparador.Should().Be(DisparadorEstado.Manual);
        }

        await using (var deB = empresas.B.Db())
        {
            (await deB.Set<CabeceraDiagnostico>().AnyAsync(d => d.Id == cabecera.Id, ct)).Should().BeFalse("B no ve la cabecera de A");
            (await deB.Set<CabeceraDiagnostico>().CountAsync(ct)).Should().Be(0);
            (await deB.Set<HistorialEstadoDiagnostico>().CountAsync(ct)).Should().Be(0, "B no ve el historial de A");
        }

        // Ni por SQL directo: un historial de B no puede colgar de la cabecera de A (FK compuesta por empresa).
        await using var conexion = await AbrirAsync(api, ct);
        await DebeViolarAsync(
            () => InsertarHistorialAsync(conexion, empresas.B.Id, cabecera.Id, ct),
            "fk_historial_estado_diagnostico_diagnosticos",
            PostgresErrorCodes.ForeignKeyViolation);
    }

    // La configuración y el alcance se guardan inmutables (descripción de la HU): EF rechaza cambiarlos tras guardar.
    [Fact]
    public async Task Alcance_configuracion_y_modo_no_cambian_despues_de_guardar()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var cabecera = new CabeceraDiagnostico { Nombre = "Lote", Configuracion = "{}", UsuarioCreadorId = Guid.NewGuid(), CreadoEn = DateTimeOffset.UtcNow };
        await using (var db = empresas.A.Db())
        {
            db.Add(cabecera);
            await db.SaveChangesAsync(ct);
        }

        foreach (var cambiar in new Action<CabeceraDiagnostico>[]
                 {
                     c => c.Alcance = [Fuente.Runt, Fuente.Avaluo],
                     c => c.Configuracion = "{\"otra\": true}",
                     c => c.Modo = ModoDiagnostico.ConAnalisis,
                 })
        {
            await using var db = empresas.A.Db();
            var leida = await db.Set<CabeceraDiagnostico>().SingleAsync(ct);
            cambiar(leida);
            var guardar = () => db.SaveChangesAsync(ct);
            await guardar.Should().ThrowAsync<InvalidOperationException>();
        }

        // El estado y el cierre sí cambian.
        await using (var db = empresas.A.Db())
        {
            var leida = await db.Set<CabeceraDiagnostico>().SingleAsync(ct);
            leida.Estado = EstadoDiagnostico.EnProceso;
            leida.VersionTablero = 1;
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task VerificarSchemaAsync(NpgsqlConnection conexion, CancellationToken ct)
    {
        (await TablasAsync(conexion, ct)).Should().Contain(["diagnosticos", "historial_estado_diagnostico"]);

        var restricciones = await ListarAsync(
            conexion,
            "SELECT conname FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace " +
            "WHERE n.nspname = 'diagnostico' AND conrelid::regclass::text IN ('diagnostico.diagnosticos', 'diagnostico.historial_estado_diagnostico')",
            ct);
        restricciones.Should().Contain(
        [
            "pk_diagnosticos", "uq_diagnosticos_tenant_id", "ck_tipo_con_analisis", "ck_diagnosticos_modo", "ck_diagnosticos_estado",
            "ck_diagnosticos_alcance_valido", "ck_diagnosticos_configuracion_objeto", "ck_diagnosticos_estimado_consultas_objeto",
            "pk_historial_estado_diagnostico", "fk_historial_estado_diagnostico_diagnosticos",
            "ck_historial_estado_diagnostico_estado_nuevo", "ck_historial_estado_diagnostico_estado_anterior",
            "ck_historial_estado_diagnostico_disparador",
        ]);

        var indices = await ListarAsync(conexion, "SELECT indexname || ' ' || indexdef FROM pg_indexes WHERE schemaname = 'diagnostico'", ct);
        indices.Should().Contain(i => i.StartsWith("ix_diag_tenant_creado ", StringComparison.Ordinal) && i.Contains("(tenant_id, creado_en DESC)", StringComparison.Ordinal));
        indices.Should().Contain(i => i.StartsWith("ix_historial_diagnostico ", StringComparison.Ordinal) && i.Contains("(tenant_id, diagnostico_id, en)", StringComparison.Ordinal));

        var columnas = await ListarAsync(
            conexion,
            "SELECT column_name || ' ' || data_type || ' ' || is_nullable FROM information_schema.columns " +
            "WHERE table_schema = 'diagnostico' AND table_name = 'diagnosticos'",
            ct);
        columnas.Should().Contain(
        [
            "estimado_consultas jsonb YES", "alcance jsonb NO", "configuracion jsonb NO",
            "creado_en timestamp with time zone NO", "cerrado_en timestamp with time zone YES",
        ]);
    }

    private static async Task<List<string>> TablasAsync(NpgsqlConnection conexion, CancellationToken ct) =>
        await ListarAsync(conexion, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'diagnostico'", ct);

    private static async Task<List<string>> ListarAsync(NpgsqlConnection conexion, string sql, CancellationToken ct)
    {
        var valores = new List<string>();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await using var fila = await comando.ExecuteReaderAsync(ct);
        while (await fila.ReadAsync(ct))
            valores.Add(fila.GetString(0));
        return valores;
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

    private static async Task<Guid> InsertarCabeceraAsync(
        NpgsqlConnection conexion,
        Guid tenant,
        CancellationToken ct,
        string modo = "SIMPLE",
        string? tipo = null,
        string estado = "CREADO",
        string alcance = "[\"RUNT\"]",
        string configuracion = "{}",
        string? estimado = null)
    {
        var id = Guid.CreateVersion7();
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.diagnosticos (id, tenant_id, nombre, modo, tipo_tramite_codigo, alcance, configuracion, " +
            "estimado_consultas, estado, usuario_creador_id, creado_en) " +
            "VALUES (@id, @t, 'Lote', @modo, @tipo, @alcance::jsonb, @configuracion::jsonb, @estimado::jsonb, @estado, @usuario, now())",
            conexion);
        insertar.Parameters.AddWithValue("id", id);
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("modo", modo);
        insertar.Parameters.AddWithValue("tipo", (object?)tipo ?? DBNull.Value);
        insertar.Parameters.AddWithValue("alcance", alcance);
        insertar.Parameters.AddWithValue("configuracion", configuracion);
        insertar.Parameters.Add(new NpgsqlParameter("estimado", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)estimado ?? DBNull.Value });
        insertar.Parameters.AddWithValue("estado", estado);
        insertar.Parameters.AddWithValue("usuario", Guid.NewGuid());
        await insertar.ExecuteNonQueryAsync(ct);
        return id;
    }

    private static async Task InsertarHistorialAsync(
        NpgsqlConnection conexion,
        Guid tenant,
        Guid diagnostico,
        CancellationToken ct,
        string? estadoAnterior = null,
        string estadoNuevo = "CREADO",
        string disparador = "AUTO")
    {
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.historial_estado_diagnostico (id, tenant_id, diagnostico_id, estado_anterior, estado_nuevo, disparador, en) " +
            "VALUES (@id, @t, @d, @anterior, @nuevo, @disparador, now())",
            conexion);
        insertar.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("d", diagnostico);
        insertar.Parameters.Add(new NpgsqlParameter("anterior", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)estadoAnterior ?? DBNull.Value });
        insertar.Parameters.AddWithValue("nuevo", estadoNuevo);
        insertar.Parameters.AddWithValue("disparador", disparador);
        await insertar.ExecuteNonQueryAsync(ct);
    }
}
