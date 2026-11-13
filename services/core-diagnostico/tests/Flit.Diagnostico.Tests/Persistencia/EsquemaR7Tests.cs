using Flit.Diagnostico.Api.Modulos.Dictamen;
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
/// HU #14563 (B7, ADR-0086) — migración <c>R7_AjusteDictamen</c> sobre el schema migrado (base efímera de <see cref="ApiDePrueba"/>):
/// columnas <c>ajuste_motivo</c> y <c>ajustado_desde_version</c> en <c>dictamenes</c>, <c>MANUAL</c> en <c>redactado_por</c> y los
/// CHECK nuevos de la cadena de versiones. Las reglas las garantiza la base: se prueban por SQL directo. Placas ficticias.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EsquemaR7Tests
/// </code>
/// </example>
public sealed class EsquemaR7Tests
{
    private const string MigracionAnterior = "20261111214216_P4d_DerivacionAjustesRevision";
    private const string MotivoValido = "Soporte verificó el SOAT con la aseguradora";

    // Reversible: el Down quita las columnas y el Up las vuelve a crear.
    [Fact]
    public async Task Migracion_R7_crea_columnas_y_es_reversible()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);

        (await ColumnasAsync(conexion, ct)).Should().Contain(["ajuste_motivo:character varying:500", "ajustado_desde_version:integer:"]);

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, null))
        {
            await db.GetService<IMigrator>().MigrateAsync(MigracionAnterior, ct);
            (await ColumnasAsync(conexion, ct)).Should().BeEmpty();
            await db.Database.MigrateAsync(ct);
            (await db.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
        }

        (await ColumnasAsync(conexion, ct)).Should().HaveCount(2);
    }

    // Override coherente: la versión anterior es siempre version - 1 y autor e instante van juntos.
    [Fact]
    public async Task Checks_de_override_y_cadena_rechazan_filas_incoherentes()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, vehiculo) = await SembrarVehiculoAsync(conexion, ct);

        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 0 }, ct), "ck_dictamenes_version");
        await DebeViolarAsync(
            () => InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 2, Override = true, ConOverrideEn = false, Desde = 1 }, ct),
            "ck_dictamenes_override");
        await DebeViolarAsync(
            () => InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 2, Override = true }, ct),
            "ck_dictamenes_ajuste");
        await DebeViolarAsync(
            () => InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 3, Override = true, Desde = 1 }, ct),
            "ck_dictamenes_ajuste");
        await DebeViolarAsync(
            () => InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 2, Desde = 1 }, ct),
            "ck_dictamenes_ajuste");

        await InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 1 }, ct);
        await InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 2, Override = true, Desde = 1 }, ct);
    }

    // D9 y redacción MANUAL: el motivo exige override y 10..500 caracteres; MANUAL exige ajuste con motivo y nace COMPLETADA.
    [Fact]
    public async Task Checks_de_motivo_y_redaccion_manual()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, vehiculo) = await SembrarVehiculoAsync(conexion, ct);

        await DebeViolarAsync(
            () => InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 1, Motivo = MotivoValido }, ct),
            "ck_dictamenes_ajuste_motivo");
        await DebeViolarAsync(
            () => InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 2, Override = true, Desde = 1, Motivo = "  corto  " }, ct),
            "ck_dictamenes_ajuste_motivo");
        await DebeViolarAsync(
            () => InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 2, Override = true, Desde = 1, RedactadoPor = "MANUAL", Estado = "COMPLETADA" }, ct),
            "ck_dictamenes_manual");
        await DebeViolarAsync(
            () => InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 2, Override = true, Desde = 1, Motivo = MotivoValido, RedactadoPor = "MANUAL" }, ct),
            "ck_redaccion");
        await DebeViolarAsync(
            () => InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 1, RedactadoPor = "OTRO" }, ct),
            "ck_dictamenes_redactado_por");

        await InsertarAsync(
            conexion, tenant, vehiculo,
            new Fila { Version = 2, Override = true, Desde = 1, Motivo = MotivoValido, RedactadoPor = "MANUAL", Estado = "COMPLETADA" }, ct);
        // Override de solo veredicto (cuerpo legado): motivo nulo admitido por la base (schema.md §2.2).
        await InsertarAsync(conexion, tenant, vehiculo, new Fila { Version = 3, Override = true, Desde = 2 }, ct);
    }

    // La fila del override deja la cadena explícita: ajustada desde la vigente, sin motivo en el cuerpo legado.
    [Fact]
    public void FilaDeOverride_registra_la_version_ajustada()
    {
        var ahora = new DateTimeOffset(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);
        var vigente = new DictamenVehiculo
        {
            TenantId = Guid.CreateVersion7(),
            VehiculoId = Guid.CreateVersion7(),
            Version = 4,
            Vigente = true,
            Veredicto = "VIABLE",
            Alertamientos = "[]",
            CatalogoVersion = 1,
            RedactadoPor = "PLANTILLA",
            EstadoRedaccion = "PENDIENTE",
        };

        var nueva = EmisorDictamen.FilaDeOverride(vigente, "BLOQUEADO", true, Guid.CreateVersion7(), "TRASPASO_STANDARD", false, true, ahora);

        nueva.Version.Should().Be(5);
        nueva.AjustadoDesdeVersion.Should().Be(4);
        nueva.AjusteMotivo.Should().BeNull();
    }

    // HU #14563 AC21/AC29 (schema.md §2.4) — la cadena real: automático, override de veredicto, ajuste FLIT con alertamientos y
    // recálculo automático. Versiones 1..4 sin huecos, una sola vigente, ajustado_desde_version = version - 1 solo en override y
    // ajuste, y los CHECK de R7 la aceptan.
    [Fact]
    public async Task Cadena_1_a_max_con_override_ajuste_y_recalculo()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, vehiculo) = await SembrarVehiculoAsync(conexion, ct);
        var ahora = new DateTimeOffset(2026, 11, 13, 18, 0, 0, TimeSpan.Zero);
        var usuario = Guid.CreateVersion7();
        var evaluado = new Flit.Diagnostico.Domain.Dictamen.DictamenEvaluado("VIABLE", []);

        await using (var db = DosEmpresas.Contexto(api.BaseDeDatos, tenant))
        {
            db.Add(EmisorDictamen.FilaAlCerrar(tenant, vehiculo, 1, "TRASPASO_STANDARD", evaluado, false, true, ahora));
            await db.SaveChangesAsync(ct);
        }

        await AgregarAsync(api, tenant, vehiculo, v =>
        {
            var fila = EmisorDictamen.FilaDeOverride(v, "BLOQUEADO", true, usuario, "TRASPASO_STANDARD", false, true, ahora);
            fila.AjusteMotivo = MotivoValido;
            return fila;
        }, ct);
        await AgregarAsync(api, tenant, vehiculo, v => EmisorDictamen.FilaDeAjuste(
            v, "BLOQUEADO",
            [new AlertamientoAjuste(CodigosDictamen.AjusteFlit, "Soporte confirmo el embargo", "BLOQUEA", "RUNT")],
            MotivoValido, usuario, ahora), ct);
        await AgregarAsync(api, tenant, vehiculo, v => EmisorDictamen.FilaAlCerrar(tenant, vehiculo, v.Version + 1, "TRASPASO_STANDARD", evaluado, false, true, ahora), ct);

        await using var lectura = DosEmpresas.Contexto(api.BaseDeDatos, tenant);
        var cadena = await lectura.Set<DictamenVehiculo>().AsNoTracking().Where(d => d.VehiculoId == vehiculo).OrderBy(d => d.Version).ToListAsync(ct);
        cadena.Select(d => d.Version).Should().Equal(1, 2, 3, 4);
        cadena.Should().ContainSingle(d => d.Vigente).Which.Version.Should().Be(4);
        cadena.Select(d => d.AjustadoDesdeVersion).Should().Equal(null, 1, 2, null);
        cadena.Select(d => d.RedactadoPor).Should().Equal("PLANTILLA", "PLANTILLA", "MANUAL", "PLANTILLA");
        cadena[3].AnalisisOverride.Should().BeFalse("D7: el recálculo nace sin marca de ajuste");
        cadena[3].AjusteMotivo.Should().BeNull();
    }

    private static async Task AgregarAsync(ApiDePrueba api, Guid tenant, Guid vehiculo, Func<DictamenVehiculo, DictamenVehiculo> siguiente, CancellationToken ct)
    {
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, tenant);
        var vigente = await db.Set<DictamenVehiculo>().SingleAsync(d => d.VehiculoId == vehiculo && d.Vigente, ct);
        var nueva = siguiente(vigente);
        vigente.Vigente = false;
        nueva.TenantId = tenant;
        db.Add(nueva);
        await db.SaveChangesAsync(ct);
    }

    private sealed record Fila
    {
        public int Version { get; init; }
        public bool Override { get; init; }
        public bool ConOverrideEn { get; init; } = true;
        public int? Desde { get; init; }
        public string? Motivo { get; init; }
        public string RedactadoPor { get; init; } = "PLANTILLA";
        public string Estado { get; init; } = "PENDIENTE";
    }

    private static async Task InsertarAsync(NpgsqlConnection conexion, Guid tenant, Guid vehiculo, Fila fila, CancellationToken ct)
    {
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.dictamenes (id, tenant_id, vehiculo_id, version, vigente, veredicto, alertamientos, catalogo_version, " +
            "redactado_por, estado_redaccion, redaccion_intentos, analisis_override, usuario_override_id, override_en, creado_en, " +
            "ajuste_motivo, ajustado_desde_version) " +
            "VALUES (@id, @t, @v, @version, false, 'VIABLE', '[]'::jsonb, 1, @redactado, @estado, 0, @override, @usuario, @en, now(), @motivo, @desde)",
            conexion);
        insertar.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("v", vehiculo);
        insertar.Parameters.AddWithValue("version", fila.Version);
        insertar.Parameters.AddWithValue("redactado", fila.RedactadoPor);
        insertar.Parameters.AddWithValue("estado", fila.Estado);
        insertar.Parameters.AddWithValue("override", fila.Override);
        insertar.Parameters.Add(new NpgsqlParameter("usuario", NpgsqlDbType.Uuid) { Value = fila.Override ? Guid.NewGuid() : DBNull.Value });
        insertar.Parameters.Add(new NpgsqlParameter("en", NpgsqlDbType.TimestampTz)
        {
            Value = fila.Override && fila.ConOverrideEn ? DateTimeOffset.UtcNow : DBNull.Value,
        });
        insertar.Parameters.Add(new NpgsqlParameter("motivo", NpgsqlDbType.Varchar) { Value = (object?)fila.Motivo ?? DBNull.Value });
        insertar.Parameters.Add(new NpgsqlParameter("desde", NpgsqlDbType.Integer) { Value = (object?)fila.Desde ?? DBNull.Value });
        await insertar.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<string>> ColumnasAsync(NpgsqlConnection conexion, CancellationToken ct)
    {
        var valores = new List<string>();
        await using var comando = new NpgsqlCommand(
            "SELECT column_name || ':' || data_type || ':' || coalesce(character_maximum_length::text, '') FROM information_schema.columns " +
            "WHERE table_schema = 'diagnostico' AND table_name = 'dictamenes' AND column_name IN ('ajuste_motivo', 'ajustado_desde_version')",
            conexion);
        await using var fila = await comando.ExecuteReaderAsync(ct);
        while (await fila.ReadAsync(ct))
            valores.Add(fila.GetString(0));
        return valores;
    }

    private static async Task DebeViolarAsync(Func<Task> accion, string restriccion)
    {
        var error = (await accion.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        error.ConstraintName.Should().Be(restriccion);
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
            "VALUES (@id, @t, @d, 'TST907', 'TST907', 'PLACA_CARRO', 'TST907', 1)",
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
