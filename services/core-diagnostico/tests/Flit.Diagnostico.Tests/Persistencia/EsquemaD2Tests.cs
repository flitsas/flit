using System.Reflection;
using System.Text.RegularExpressions;
using Flit.Diagnostico.Api.Modulos.Catalogos;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Flit.Diagnostico.Domain.Vehiculos;
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
/// HU #13723 (F03-H1) — migración D2 del motor de consultas sobre el schema migrado (base efímera de <see cref="ApiDePrueba"/>):
/// <c>consultas</c> (celda y unidad de la cola), <c>intentos_consulta</c> (solo inserción), los catálogos globales
/// <c>organismos</c> y <c>departamentos_impuesto</c> y las columnas de reutilización del mismo día (D-10). Las reglas las
/// garantiza la base: se prueban por SQL directo. Placas y documentos son ficticios.
/// </summary>
public sealed partial class EsquemaD2Tests
{
    private const string MigracionAnterior = "20261021085830_D1_Vehiculos";

    private static readonly string[] TablasD2 = ["consultas", "intentos_consulta", "organismos", "departamentos_impuesto"];

    // AC4 (F03-C13, F04-C7) — idempotente y reversible; existen las columnas de D-10 y el historial del lote.
    [Fact]
    public async Task Migracion_aplica_dos_veces_sin_error()
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

        // Reversible: el Down deja la base como en D1 (con el historial del lote) y el Up la vuelve a crear.
        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior, ct);
            var tablas = await ListarAsync(conexion, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'diagnostico'", ct);
            tablas.Should().NotContain(TablasD2).And.Contain(["vehiculos", "historial_estado_diagnostico"]);
            (await ListarAsync(conexion, "SELECT indexname FROM pg_indexes WHERE schemaname = 'diagnostico'", ct))
                .Should().NotContain("ix_vehiculos_tenant_placa");
            await db.Database.MigrateAsync(ct);
        }

        await VerificarSchemaAsync(conexion, ct);
    }

    // AC2 (F03-C3) — una sola celda por vehículo y fuente (idempotencia de la cola, 12659 #5).
    [Fact]
    public async Task Unico_vehiculo_fuente_rechaza_segunda_celda()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote, vehiculo) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);

        await InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "RUNT");
        await DebeViolarAsync(
            () => InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "RUNT"),
            "ux_consulta_vehiculo_fuente",
            PostgresErrorCodes.UniqueViolation);

        // Otra fuente del mismo vehículo, y la misma fuente de otro vehículo, sí caben.
        await InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "SIMIT_VEHICULO");
        var otro = await InsertarVehiculoAsync(conexion, tenant, lote, ct, norm: "XYZ987", orden: 2);
        await InsertarCeldaAsync(conexion, tenant, lote, otro, ct, fuente: "RUNT");
    }

    // AC3 (F03-C6, F03-C9) — N/A siempre con motivo y motivo solo en N/A.
    [Fact]
    public async Task Check_motivo_na_rechaza_NA_sin_motivo_y_motivo_sin_NA()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote, vehiculo) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);

        await DebeViolarAsync(() => InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "RUNT_LOCATARIO", estado: "NA"), "ck_motivo_na");
        await DebeViolarAsync(
            () => InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "RUNT", estado: "COMPLETADO", motivo: "SERVICIO_PUBLICO"),
            "ck_motivo_na");
        await DebeViolarAsync(
            () => InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "AVALUO", estado: "NA", motivo: "SIN_RAZON"),
            "ck_consultas_motivo_na");
        await DebeViolarAsync(() => InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "AVALUO", estado: "TOMADA"), "ck_consultas_estado");
        await DebeViolarAsync(() => InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "AVALUO", payload: "[]"), "ck_consultas_payload_objeto");

        // Cada motivo cabe con N/A, y una celda sin N/A no lleva motivo.
        await InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "RUNT_LOCATARIO", estado: "NA", motivo: "SIN_DOCUMENTO_LOCATARIO");
        await InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "IMPUESTO_DEPARTAMENTAL", estado: "NA", motivo: "DEPARTAMENTO_NO_SOPORTADO");
        await InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "SIMIT_LOCATARIO", estado: "NA", motivo: "SERVICIO_PUBLICO");
        await InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "RUNT", estado: "COMPLETADO", payload: "{}");
    }

    // AC1 (F03-C10) — la base acepta las ocho fuentes del enum, rechaza FASECOLDA, y cada CHECK lista los mismos códigos que su enum.
    [Fact]
    public async Task Check_fuente_coincide_con_enum_de_dominio()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote, vehiculo) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);

        await DebeViolarAsync(() => InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "FASECOLDA"), "ck_consultas_fuente");
        foreach (var fuente in Enum.GetValues<Fuente>())
            await InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: CodigosDeEnum.Codigo(fuente));

        (await CodigosDelCheckAsync(conexion, "consultas", "ck_consultas_fuente", ct)).Should().BeEquivalentTo(Codigos<Fuente>());
        (await CodigosDelCheckAsync(conexion, "consultas", "ck_consultas_estado", ct)).Should().BeEquivalentTo(Codigos<EstadoCelda>());
        (await CodigosDelCheckAsync(conexion, "consultas", "ck_consultas_motivo_na", ct)).Should().BeEquivalentTo(Codigos<MotivoNa>());
        (await CodigosDelCheckAsync(conexion, "consultas", "ck_consultas_origen", ct)).Should().BeEquivalentTo(Codigos<OrigenCelda>());
        (await CodigosDelCheckAsync(conexion, "intentos_consulta", "ck_intentos_consulta_fuente", ct)).Should().BeEquivalentTo(Codigos<Fuente>());
        (await CodigosDelCheckAsync(conexion, "intentos_consulta", "ck_intentos_consulta_disparador", ct)).Should().BeEquivalentTo(Codigos<DisparadorIntento>());
        (await CodigosDelCheckAsync(conexion, "intentos_consulta", "ck_intentos_consulta_servicio", ct)).Should().BeEquivalentTo(Codigos<ServicioConsulta>());
        (await CodigosDelCheckAsync(conexion, "intentos_consulta", "ck_intentos_consulta_estado", ct)).Should().BeEquivalentTo(Codigos<ResultadoIntento>());
    }

    // DoD 6 — dos compañías en el mismo Postgres: B no ve las celdas, intentos ni historial de A, ni puede colgar filas de las de A.
    [Fact]
    public async Task Filtro_de_tenant_aplica_a_consultas_intentos_e_historial()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var ahora = DateTimeOffset.UtcNow;
        var cabecera = new CabeceraDiagnostico { Nombre = "Lote", Configuracion = "{}", UsuarioCreadorId = Guid.NewGuid(), CreadoEn = ahora };
        var vehiculo = new VehiculoDiagnostico
        {
            DiagnosticoId = cabecera.Id,
            IdentificadorOriginal = "ABC123",
            IdentificadorNorm = "ABC123",
            TipoIdentificador = TipoIdentificador.PlacaCarro,
            Placa = "ABC123",
            Orden = 1,
        };
        var celda = new CeldaConsulta
        {
            DiagnosticoId = cabecera.Id,
            VehiculoId = vehiculo.Id,
            Fuente = Fuente.Runt,
            Estado = EstadoCelda.Completado,
            Origen = OrigenCelda.Api,
            Payload = "{}",
            DatoObtenidoEn = ahora,
            ActualizadoEn = ahora,
            VersionTablero = 1,
        };
        var na = new CeldaConsulta
        {
            DiagnosticoId = cabecera.Id,
            VehiculoId = vehiculo.Id,
            Fuente = Fuente.RuntLocatario,
            Estado = EstadoCelda.Na,
            MotivoNa = MotivoNa.SinDocumentoLocatario,
            ActualizadoEn = ahora,
            VersionTablero = 1,
        };
        var intento = new IntentoConsulta
        {
            DiagnosticoId = cabecera.Id,
            VehiculoId = vehiculo.Id,
            ConsultaId = celda.Id,
            Fuente = Fuente.Runt,
            Servicio = ServicioConsulta.Intempo,
            Disparador = DisparadorIntento.Auto,
            Inicio = ahora,
            Fin = ahora,
            Estado = ResultadoIntento.Success,
        };
        var historial = new HistorialEstadoDiagnostico
        {
            DiagnosticoId = cabecera.Id,
            EstadoAnterior = EstadoDiagnostico.Creado,
            EstadoNuevo = EstadoDiagnostico.EnProceso,
            Disparador = DisparadorEstado.Auto,
            En = ahora,
        };

        await using (var db = empresas.A.Db())
        {
            db.AddRange(cabecera, vehiculo, celda, na, intento, historial);
            await db.SaveChangesAsync(ct);
        }

        await using (var deA = empresas.A.Db())
        {
            var celdas = await deA.Set<CeldaConsulta>().AsNoTracking().OrderBy(c => c.Fuente).ToListAsync(ct);
            celdas.Should().HaveCount(2).And.OnlyContain(c => c.TenantId == empresas.A.Id);
            celdas[0].Estado.Should().Be(EstadoCelda.Completado);
            celdas[0].IntentosAuto.Should().Be(0);
            celdas[0].LeaseHasta.Should().BeNull();
            celdas[1].MotivoNa.Should().Be(MotivoNa.SinDocumentoLocatario);
            var leido = await deA.Set<IntentoConsulta>().AsNoTracking().SingleAsync(ct);
            leido.Servicio.Should().Be(ServicioConsulta.Intempo);
            leido.DesdeCache.Should().BeFalse();
            (await deA.Set<HistorialEstadoDiagnostico>().CountAsync(ct)).Should().Be(1);
        }

        await using (var deB = empresas.B.Db())
        {
            (await deB.Set<CeldaConsulta>().CountAsync(ct)).Should().Be(0, "B no ve las celdas de A");
            (await deB.Set<IntentoConsulta>().CountAsync(ct)).Should().Be(0, "B no ve los intentos de A");
            (await deB.Set<HistorialEstadoDiagnostico>().CountAsync(ct)).Should().Be(0, "B no ve el historial de A");
        }

        // Ni por SQL directo: una fila de B no cuelga del lote, del vehículo ni de la celda de A, ni reutiliza su dato (FK compuestas).
        await using var conexion = await AbrirAsync(api, ct);
        var (_, loteB, vehiculoB) = await SembrarVehiculoAsync(conexion, empresas.B.Id, ct);
        await DebeViolarAsync(
            () => InsertarCeldaAsync(conexion, empresas.B.Id, loteB, vehiculo.Id, ct, fuente: "AVALUO"),
            "fk_consultas_vehiculos",
            PostgresErrorCodes.ForeignKeyViolation);
        await DebeViolarAsync(
            () => InsertarCeldaAsync(conexion, empresas.B.Id, cabecera.Id, vehiculoB, ct, fuente: "AVALUO"),
            "fk_consultas_diagnosticos",
            PostgresErrorCodes.ForeignKeyViolation);
        await DebeViolarAsync(
            () => InsertarCeldaAsync(conexion, empresas.B.Id, loteB, vehiculoB, ct, fuente: "RUNT", reutilizadaDe: celda.Id),
            "fk_consultas_consultas_reutilizada_de",
            PostgresErrorCodes.ForeignKeyViolation);
        var celdaB = await InsertarCeldaAsync(conexion, empresas.B.Id, loteB, vehiculoB, ct, fuente: "SIMIT_VEHICULO");
        await DebeViolarAsync(
            () => InsertarIntentoAsync(conexion, empresas.B.Id, loteB, vehiculoB, celda.Id, ct),
            "fk_intentos_consulta_consultas",
            PostgresErrorCodes.ForeignKeyViolation);
        await DebeViolarAsync(
            () => InsertarIntentoAsync(conexion, empresas.B.Id, loteB, vehiculo.Id, celdaB, ct),
            "fk_intentos_consulta_vehiculos",
            PostgresErrorCodes.ForeignKeyViolation);
        await DebeViolarAsync(
            () => InsertarIntentoAsync(conexion, empresas.B.Id, cabecera.Id, vehiculoB, celdaB, ct),
            "fk_intentos_consulta_diagnosticos",
            PostgresErrorCodes.ForeignKeyViolation);
    }

    // ADR-0072 (c) — RESTRICT: una celda con intentos, un vehículo con celdas y una celda reutilizada no se borran.
    [Fact]
    public async Task Fk_restringen_el_borrado_de_celdas_con_historia()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote, vehiculo) = await SembrarVehiculoAsync(conexion, Guid.NewGuid(), ct);
        var celda = await InsertarCeldaAsync(conexion, tenant, lote, vehiculo, ct, fuente: "RUNT", estado: "COMPLETADO", payload: "{}");
        await InsertarIntentoAsync(conexion, tenant, lote, vehiculo, celda, ct);
        await DebeViolarAsync(() => EjecutarAsync(conexion, "DELETE FROM diagnostico.consultas WHERE id = @id", celda, ct), "fk_intentos_consulta_consultas", PostgresErrorCodes.ForeignKeyViolation);

        await EjecutarAsync(conexion, "DELETE FROM diagnostico.intentos_consulta WHERE consulta_id = @id", celda, ct);
        var otro = await InsertarVehiculoAsync(conexion, tenant, lote, ct, norm: "QWE456", orden: 2);
        await InsertarCeldaAsync(conexion, tenant, lote, otro, ct, fuente: "RUNT", estado: "COMPLETADO", payload: "{}", reutilizadaDe: celda);
        await DebeViolarAsync(() => EjecutarAsync(conexion, "DELETE FROM diagnostico.consultas WHERE id = @id", celda, ct), "fk_consultas_consultas_reutilizada_de", PostgresErrorCodes.ForeignKeyViolation);
        await DebeViolarAsync(() => EjecutarAsync(conexion, "DELETE FROM diagnostico.vehiculos WHERE id = @id", vehiculo, ct), "fk_consultas_vehiculos", PostgresErrorCodes.ForeignKeyViolation);
    }

    // ADR-0072 (b) y (d) 4 — el intento es de solo inserción: sin token de concurrencia y solo con init (salvo TenantId).
    [Fact]
    public void Intento_es_de_solo_insercion_y_la_celda_lleva_xmin()
    {
        using var db = DosEmpresas.Contexto("Host=127.0.0.1;Database=solo_modelo", Guid.NewGuid());
        var intento = db.Model.FindEntityType(typeof(IntentoConsulta));
        intento.Should().NotBeNull("diagnostico.intentos_consulta está mapeada");
        intento!.GetProperties().Should().NotContain(p => p.IsConcurrencyToken, "un historial no lleva token de concurrencia");
        typeof(IntentoConsulta).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true } && p.Name != nameof(IntentoConsulta.TenantId) && !EsInit(p))
            .Select(p => p.Name).Should().BeEmpty("solo TenantId tiene setter público");

        var celda = db.Model.FindEntityType(typeof(CeldaConsulta));
        celda.Should().NotBeNull("diagnostico.consultas está mapeada");
        celda!.FindProperty(nameof(CeldaConsulta.Version))!.GetColumnName().Should().Be("xmin");
        db.Model.FindEntityType(typeof(Organismo))!.GetTableName().Should().Be("organismos");
        db.Model.FindEntityType(typeof(DepartamentoImpuesto))!.GetTableName().Should().Be("departamentos_impuesto");
    }

    // Catálogos globales (excepción de ADR-0072): sin tenant_id, se leen con o sin empresa en el token.
    [Fact]
    public async Task Catalogos_de_organismos_y_departamentos_son_globales()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var codigo = $"T{Random.Shared.Next(100000, 999999)}";
        await using (var insertar = new NpgsqlCommand(
                         "INSERT INTO diagnostico.organismos (codigo, nombre, nombre_norm, departamento_codigo, departamento_nombre) " +
                         "VALUES (@c, 'Organismo de prueba', 'ORGANISMO DE PRUEBA', '99', 'Departamento de prueba'); " +
                         "INSERT INTO diagnostico.departamentos_impuesto (departamento_codigo, soportado, actualizado_en) VALUES ('99', true, now())",
                         conexion))
        {
            insertar.Parameters.AddWithValue("c", codigo);
            await insertar.ExecuteNonQueryAsync(ct);
        }

        (await ListarAsync(conexion, "SELECT column_name FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name IN ('organismos', 'departamentos_impuesto')", ct))
            .Should().NotContain("tenant_id");
        foreach (var empresa in new Guid?[] { null, Guid.NewGuid() })
        {
            await using var db = DosEmpresas.Contexto(api.BaseDeDatos, empresa);
            (await db.Set<Organismo>().AsNoTracking().SingleAsync(o => o.Codigo == codigo, ct)).DepartamentoCodigo.Should().Be("99");
            (await db.Set<DepartamentoImpuesto>().AsNoTracking().SingleAsync(d => d.DepartamentoCodigo == "99", ct)).Soportado.Should().BeTrue();
        }
    }

    private static bool EsInit(PropertyInfo propiedad) =>
        propiedad.SetMethod!.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static IEnumerable<string> Codigos<T>()
        where T : struct, Enum => Enum.GetValues<T>().Select(CodigosDeEnum.Codigo);

    private static async Task VerificarSchemaAsync(NpgsqlConnection conexion, CancellationToken ct)
    {
        (await ListarAsync(conexion, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'diagnostico'", ct))
            .Should().Contain([.. TablasD2, "historial_estado_diagnostico"]);

        var columnas = await ListarAsync(
            conexion,
            "SELECT table_name || '.' || column_name || ' ' || data_type || ' ' || is_nullable FROM information_schema.columns WHERE table_schema = 'diagnostico'",
            ct);
        columnas.Should().Contain(
        [
            "consultas.tenant_id uuid NO", "consultas.dato_obtenido_en timestamp with time zone YES", "consultas.reutilizada_de_id uuid YES",
            "consultas.disponible_desde timestamp with time zone NO", "consultas.lease_hasta timestamp with time zone YES",
            "consultas.intentos_auto integer NO", "consultas.version_tablero bigint NO", "consultas.payload jsonb YES",
            "intentos_consulta.tenant_id uuid NO", "intentos_consulta.desde_cache boolean NO", "intentos_consulta.usuario_id uuid YES",
            "diagnosticos.version_tablero bigint NO", "vehiculos.estado_consultas character varying NO",
        ]);

        var restricciones = await ListarAsync(
            conexion,
            "SELECT conrelid::regclass::text || ' ' || conname FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace WHERE n.nspname = 'diagnostico'",
            ct);
        restricciones.Should().Contain(
        [
            "diagnostico.vehiculos uq_vehiculos_tenant_id", "diagnostico.consultas uq_consultas_tenant_id", "diagnostico.consultas pk_consultas",
            "diagnostico.consultas fk_consultas_vehiculos", "diagnostico.consultas fk_consultas_diagnosticos", "diagnostico.consultas fk_consultas_consultas_reutilizada_de",
            "diagnostico.consultas ck_motivo_na", "diagnostico.intentos_consulta fk_intentos_consulta_consultas",
            "diagnostico.intentos_consulta fk_intentos_consulta_vehiculos", "diagnostico.intentos_consulta fk_intentos_consulta_diagnosticos",
            "diagnostico.organismos pk_organismos", "diagnostico.departamentos_impuesto pk_departamentos_impuesto",
        ]);

        // Toda FK nueva es RESTRICT (confdeltype 'r'). Siete desde D3: intentos_consulta.persona_id (HU #13790, EsquemaD3Tests).
        (await ListarAsync(
                conexion,
                "SELECT conname || ' ' || confdeltype::text FROM pg_constraint WHERE contype = 'f' AND conrelid::regclass::text IN ('diagnostico.consultas', 'diagnostico.intentos_consulta')",
                ct))
            .Should().HaveCount(7).And.OnlyContain(f => f.EndsWith(" r", StringComparison.Ordinal));

        var indices = await ListarAsync(conexion, "SELECT indexname || ' ' || indexdef FROM pg_indexes WHERE schemaname = 'diagnostico'", ct);
        indices.Should().Contain(i => i.StartsWith("ix_cola ", StringComparison.Ordinal) && i.Contains("(disponible_desde)", StringComparison.Ordinal)
            && i.Contains("PENDIENTE", StringComparison.Ordinal) && i.Contains("EN_CURSO", StringComparison.Ordinal));
        indices.Should().Contain(i => i.StartsWith("ux_consulta_vehiculo_fuente ", StringComparison.Ordinal) && i.Contains("UNIQUE", StringComparison.Ordinal));
        indices.Should().Contain(i => i.StartsWith("ix_consultas_reutilizacion ", StringComparison.Ordinal)
            && i.Contains("(tenant_id, fuente, dato_obtenido_en)", StringComparison.Ordinal) && i.Contains("COMPLETADO", StringComparison.Ordinal));
        indices.Should().Contain(i => i.StartsWith("ix_vehiculos_tenant_placa ", StringComparison.Ordinal) && i.Contains("(tenant_id, placa)", StringComparison.Ordinal));
        indices.Should().Contain(i => i.StartsWith("ix_consultas_tablero ", StringComparison.Ordinal) && i.Contains("(tenant_id, diagnostico_id, version_tablero)", StringComparison.Ordinal));
        indices.Should().Contain(i => i.StartsWith("ix_intentos_metricas ", StringComparison.Ordinal) && i.Contains("(tenant_id, fuente, servicio, inicio)", StringComparison.Ordinal));
    }

    private static async Task<List<string>> CodigosDelCheckAsync(NpgsqlConnection conexion, string tabla, string restriccion, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(
            "SELECT pg_get_constraintdef(c.oid) FROM pg_constraint c WHERE c.conname = @n AND c.conrelid = ('diagnostico.' || @t)::regclass",
            conexion);
        comando.Parameters.AddWithValue("n", restriccion);
        comando.Parameters.AddWithValue("t", tabla);
        var definicion = (string?)await comando.ExecuteScalarAsync(ct);
        definicion.Should().NotBeNull($"existe {restriccion}");
        return Literal().Matches(definicion!).Select(m => m.Groups[1].Value).ToList();
    }

    [GeneratedRegex("'([A-Z_]+)'")]
    private static partial Regex Literal();

    private static async Task<List<string>> ListarAsync(NpgsqlConnection conexion, string sql, CancellationToken ct)
    {
        var valores = new List<string>();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await using var fila = await comando.ExecuteReaderAsync(ct);
        while (await fila.ReadAsync(ct))
            valores.Add(fila.GetString(0));
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

        return (tenant, lote, await InsertarVehiculoAsync(conexion, tenant, lote, ct));
    }

    private static async Task<Guid> InsertarVehiculoAsync(NpgsqlConnection conexion, Guid tenant, Guid lote, CancellationToken ct, string norm = "ABC123", int orden = 1)
    {
        var id = Guid.CreateVersion7();
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.vehiculos (id, tenant_id, diagnostico_id, identificador_original, identificador_norm, tipo_identificador, placa, orden) " +
            "VALUES (@id, @t, @d, @norm, @norm, 'PLACA_CARRO', @norm, @orden)",
            conexion);
        insertar.Parameters.AddWithValue("id", id);
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("d", lote);
        insertar.Parameters.AddWithValue("norm", norm);
        insertar.Parameters.AddWithValue("orden", orden);
        await insertar.ExecuteNonQueryAsync(ct);
        return id;
    }

    private static async Task<Guid> InsertarCeldaAsync(
        NpgsqlConnection conexion,
        Guid tenant,
        Guid lote,
        Guid vehiculo,
        CancellationToken ct,
        string fuente = "RUNT",
        string estado = "PENDIENTE",
        string? motivo = null,
        string? payload = null,
        Guid? reutilizadaDe = null)
    {
        var id = Guid.CreateVersion7();
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.consultas (id, tenant_id, diagnostico_id, vehiculo_id, fuente, estado, motivo_na, payload, reutilizada_de_id, actualizado_en, version_tablero) " +
            "VALUES (@id, @t, @d, @v, @fuente, @estado, @motivo, @payload::jsonb, @reutilizada, now(), 0)",
            conexion);
        insertar.Parameters.AddWithValue("id", id);
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("d", lote);
        insertar.Parameters.AddWithValue("v", vehiculo);
        insertar.Parameters.AddWithValue("fuente", fuente);
        insertar.Parameters.AddWithValue("estado", estado);
        insertar.Parameters.Add(new NpgsqlParameter("motivo", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)motivo ?? DBNull.Value });
        insertar.Parameters.Add(new NpgsqlParameter("payload", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)payload ?? DBNull.Value });
        insertar.Parameters.Add(new NpgsqlParameter("reutilizada", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)reutilizadaDe ?? DBNull.Value });
        await insertar.ExecuteNonQueryAsync(ct);
        return id;
    }

    private static async Task InsertarIntentoAsync(NpgsqlConnection conexion, Guid tenant, Guid lote, Guid vehiculo, Guid celda, CancellationToken ct)
    {
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.intentos_consulta (id, tenant_id, diagnostico_id, vehiculo_id, consulta_id, fuente, servicio, disparador, inicio, fin, estado) " +
            "VALUES (@id, @t, @d, @v, @c, 'RUNT', 'INTEMPO', 'AUTO', now(), now(), 'SUCCESS')",
            conexion);
        insertar.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("d", lote);
        insertar.Parameters.AddWithValue("v", vehiculo);
        insertar.Parameters.AddWithValue("c", celda);
        await insertar.ExecuteNonQueryAsync(ct);
    }
}
