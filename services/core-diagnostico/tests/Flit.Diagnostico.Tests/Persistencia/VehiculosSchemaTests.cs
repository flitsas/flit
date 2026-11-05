using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Domain.Vehiculos;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Persistencia;

/// <summary>
/// HU #13735 (F02-H1) — <c>diagnostico.vehiculos</c> y <c>diagnostico.data_protection_keys</c> sobre el schema migrado (base
/// efímera de <see cref="ApiDePrueba"/>). Las reglas las garantiza la base: se prueban por SQL directo. Placas, VIN y
/// documentos son ficticios.
/// </summary>
public sealed class VehiculosSchemaTests
{
    private const string MigracionAnterior = "20261021084448_D1_CabeceraDiagnostico";

    // DoD — migración idempotente y reversible.
    [Fact]
    public async Task Migraciones_aplican_dos_veces_y_crean_vehiculos_y_llaves()
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

        // Reversible: el Down deja la base como en la migración anterior y el Up la vuelve a crear.
        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior, ct);
            (await TablasAsync(conexion, ct)).Should().NotContain(["vehiculos", "data_protection_keys"]);
            await db.Database.MigrateAsync(ct);
        }

        await VerificarSchemaAsync(conexion, ct);
    }

    // Escenario 1 — sin duplicados en el lote (F02-C8).
    [Fact]
    public async Task Indice_unico_rechaza_el_mismo_identificador_normalizado_en_el_lote()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var tenant = Guid.NewGuid();
        var lote = await InsertarCabeceraAsync(conexion, tenant, ct);

        await InsertarVehiculoAsync(conexion, tenant, lote, ct, original: "ABC123", norm: "ABC123");
        await DebeViolarAsync(
            () => InsertarVehiculoAsync(conexion, tenant, lote, ct, original: "abc 123", norm: "ABC123", orden: 2),
            "ux_vehiculo_por_lote",
            PostgresErrorCodes.UniqueViolation);

        // En otro lote el mismo identificador sí cabe.
        var otroLote = await InsertarCabeceraAsync(conexion, tenant, ct);
        await InsertarVehiculoAsync(conexion, tenant, otroLote, ct, original: "ABC123", norm: "ABC123");
    }

    // Escenario 2 — documento con origen (F02-C13).
    [Fact]
    public async Task Check_rechaza_documento_del_locatario_sin_origen()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var tenant = Guid.NewGuid();
        var lote = await InsertarCabeceraAsync(conexion, tenant, ct);
        byte[] cifrado = [0x01, 0x02, 0x03];

        await DebeViolarAsync(() => InsertarVehiculoAsync(conexion, tenant, lote, ct, norm: "AAA111", docLocatario: cifrado, origen: null), "ck_locatario_origen");
        await DebeViolarAsync(() => InsertarVehiculoAsync(conexion, tenant, lote, ct, norm: "AAA112", docLocatario: null, origen: "PROVEEDOR"), "ck_locatario_origen");
        await DebeViolarAsync(
            () => InsertarVehiculoAsync(conexion, tenant, lote, ct, norm: "AAA113", docLocatario: cifrado, origen: "OTRO"),
            "ck_vehiculos_locatario_doc_origen");

        // El autollenado del proveedor y el declarado por el cliente sí caben, y sin documento tampoco hay origen.
        await InsertarVehiculoAsync(conexion, tenant, lote, ct, norm: "AAA114", docLocatario: cifrado, origen: "PROVEEDOR");
        await InsertarVehiculoAsync(conexion, tenant, lote, ct, norm: "AAA115", docLocatario: cifrado, origen: "DECLARADO_CLIENTE");
        await InsertarVehiculoAsync(conexion, tenant, lote, ct, norm: "AAA116");
    }

    // Escenario 3 — placa de maquinaria clasificable (F02-C2).
    [Fact]
    public async Task Acepta_placa_de_maquinaria_y_rechaza_un_tipo_desconocido()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var tenant = Guid.NewGuid();
        var lote = await InsertarCabeceraAsync(conexion, tenant, ct);

        await InsertarVehiculoAsync(conexion, tenant, lote, ct, original: "MC029554", norm: "MC029554", tipo: "PLACA_MAQUINARIA");
        await DebeViolarAsync(() => InsertarVehiculoAsync(conexion, tenant, lote, ct, norm: "BBB222", tipo: "PLACA_TRACTOR"), "ck_vehiculos_tipo_identificador");
    }

    // El identificador normalizado va en mayúsculas y sin espacios; los datos consolidados son un objeto JSON.
    [Fact]
    public async Task Check_rechaza_identificador_sin_normalizar_y_datos_que_no_son_objeto()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var tenant = Guid.NewGuid();
        var lote = await InsertarCabeceraAsync(conexion, tenant, ct);

        await DebeViolarAsync(() => InsertarVehiculoAsync(conexion, tenant, lote, ct, norm: "abc123"), "ck_vehiculos_identificador_norm");
        await DebeViolarAsync(() => InsertarVehiculoAsync(conexion, tenant, lote, ct, norm: "ABC 123"), "ck_vehiculos_identificador_norm");
        await DebeViolarAsync(() => InsertarVehiculoAsync(conexion, tenant, lote, ct, norm: "CCC333", datos: "[]"), "ck_vehiculos_datos_consolidados_objeto");
    }

    // DoD — fuga entre dos compañías: el vehículo de A no se ve con la empresa de B, ni puede colgar de un lote ajeno.
    [Fact]
    public async Task Vehiculo_de_A_no_se_devuelve_con_la_empresa_B()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresas = DosEmpresas.En(api);
        var cabecera = new CabeceraDiagnostico { Nombre = "Lote", Configuracion = "{}", UsuarioCreadorId = Guid.NewGuid(), CreadoEn = DateTimeOffset.UtcNow };
        var vehiculo = new VehiculoDiagnostico
        {
            DiagnosticoId = cabecera.Id,
            IdentificadorOriginal = "abc 123",
            IdentificadorNorm = "ABC123",
            TipoIdentificador = TipoIdentificador.PlacaCarro,
            Placa = "ABC123",
            LocatarioDocTipo = "CC",
            LocatarioDocCifrado = [0x0A, 0x0B],
            LocatarioDocOrigen = OrigenDocumento.Proveedor,
            Orden = 1,
        };

        await using (var db = empresas.A.Db())
        {
            db.Add(cabecera);
            db.Add(vehiculo);
            await db.SaveChangesAsync(ct);
        }

        await using (var deA = empresas.A.Db())
        {
            var leido = await deA.Set<VehiculoDiagnostico>().AsNoTracking().SingleAsync(ct);
            leido.TenantId.Should().Be(empresas.A.Id);
            leido.TipoIdentificador.Should().Be(TipoIdentificador.PlacaCarro);
            leido.LocatarioDocOrigen.Should().Be(OrigenDocumento.Proveedor);
            leido.LocatarioDocCifrado.Should().Equal(0x0A, 0x0B);
            leido.DatosConsolidados.Should().Be("{}");
            leido.EstadoConsultas.Should().Be("PENDIENTE");
        }

        await using (var deB = empresas.B.Db())
        {
            (await deB.Set<VehiculoDiagnostico>().AnyAsync(v => v.Id == vehiculo.Id, ct)).Should().BeFalse("B no ve el vehículo de A");
            (await deB.Set<VehiculoDiagnostico>().CountAsync(ct)).Should().Be(0);
        }

        // Ni por SQL directo: un vehículo de B no puede colgar del lote de A (FK compuesta por empresa).
        await using var conexion = await AbrirAsync(api, ct);
        await DebeViolarAsync(
            () => InsertarVehiculoAsync(conexion, empresas.B.Id, cabecera.Id, ct, norm: "DDD444"),
            "fk_vehiculos_diagnosticos",
            PostgresErrorCodes.ForeignKeyViolation);
    }

    // Las llaves de Data Protection del servicio se guardan en su schema: un reinicio no deja ilegibles los documentos.
    [Fact]
    public async Task Llaves_de_data_protection_se_guardan_en_el_schema_del_servicio()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            db.Should().BeAssignableTo<IDataProtectionKeyContext>("el servicio persiste sus llaves con PersistKeysToDbContext");
            ((IDataProtectionKeyContext)(object)db).DataProtectionKeys.Add(new DataProtectionKey { FriendlyName = "llave-de-prueba", Xml = "<key />" });
            await db.SaveChangesAsync(ct);
        }

        await using var conexion = await AbrirAsync(api, ct);
        // Desde HU #13751 el servicio conecta Data Protection: al arrancar ya dejó su propia llave en la misma tabla.
        (await ListarAsync(conexion, "SELECT friendly_name FROM diagnostico.data_protection_keys", ct)).Should().Contain("llave-de-prueba");
    }

    private static async Task VerificarSchemaAsync(NpgsqlConnection conexion, CancellationToken ct)
    {
        (await TablasAsync(conexion, ct)).Should().Contain(["vehiculos", "data_protection_keys"]);

        var restricciones = await ListarAsync(
            conexion,
            "SELECT conname FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace " +
            "WHERE n.nspname = 'diagnostico' AND conrelid::regclass::text = 'diagnostico.vehiculos'",
            ct);
        restricciones.Should().Contain(
        [
            "pk_vehiculos", "fk_vehiculos_diagnosticos", "ck_locatario_origen", "ck_vehiculos_tipo_identificador",
            "ck_vehiculos_locatario_doc_origen", "ck_vehiculos_identificador_norm", "ck_vehiculos_datos_consolidados_objeto",
        ]);

        var indices = await ListarAsync(conexion, "SELECT indexname || ' ' || indexdef FROM pg_indexes WHERE schemaname = 'diagnostico'", ct);
        indices.Should().Contain(i => i.StartsWith("ux_vehiculo_por_lote ", StringComparison.Ordinal)
            && i.Contains("UNIQUE", StringComparison.Ordinal) && i.Contains("(tenant_id, diagnostico_id, identificador_norm)", StringComparison.Ordinal));

        var columnas = await ListarAsync(
            conexion,
            "SELECT column_name || ' ' || data_type || ' ' || is_nullable FROM information_schema.columns " +
            "WHERE table_schema = 'diagnostico' AND table_name = 'vehiculos'",
            ct);
        columnas.Should().Contain(
        [
            "tenant_id uuid NO", "propietario_doc_cifrado bytea YES", "locatario_doc_cifrado bytea YES", "datos_consolidados jsonb NO",
            "runt_consultado_en timestamp with time zone YES", "orden integer NO",
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

    private static async Task<Guid> InsertarCabeceraAsync(NpgsqlConnection conexion, Guid tenant, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.diagnosticos (id, tenant_id, nombre, modo, alcance, configuracion, estado, usuario_creador_id, creado_en) " +
            "VALUES (@id, @t, 'Lote', 'SIMPLE', '[\"RUNT\"]'::jsonb, '{}'::jsonb, 'CREADO', @usuario, now())",
            conexion);
        insertar.Parameters.AddWithValue("id", id);
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("usuario", Guid.NewGuid());
        await insertar.ExecuteNonQueryAsync(ct);
        return id;
    }

    private static async Task InsertarVehiculoAsync(
        NpgsqlConnection conexion,
        Guid tenant,
        Guid diagnostico,
        CancellationToken ct,
        string? original = null,
        string norm = "ABC123",
        string tipo = "PLACA_CARRO",
        byte[]? docLocatario = null,
        string? origen = null,
        string datos = "{}",
        int orden = 1)
    {
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.vehiculos (id, tenant_id, diagnostico_id, identificador_original, identificador_norm, tipo_identificador, " +
            "locatario_doc_cifrado, locatario_doc_origen, datos_consolidados, orden) " +
            "VALUES (@id, @t, @d, @original, @norm, @tipo, @doc, @origen, @datos::jsonb, @orden)",
            conexion);
        insertar.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("d", diagnostico);
        insertar.Parameters.AddWithValue("original", original ?? norm);
        insertar.Parameters.AddWithValue("norm", norm);
        insertar.Parameters.AddWithValue("tipo", tipo);
        insertar.Parameters.Add(new NpgsqlParameter("doc", NpgsqlTypes.NpgsqlDbType.Bytea) { Value = (object?)docLocatario ?? DBNull.Value });
        insertar.Parameters.Add(new NpgsqlParameter("origen", NpgsqlTypes.NpgsqlDbType.Varchar) { Value = (object?)origen ?? DBNull.Value });
        insertar.Parameters.AddWithValue("datos", datos);
        insertar.Parameters.AddWithValue("orden", orden);
        await insertar.ExecuteNonQueryAsync(ct);
    }
}
