using Flit.Comparendos.Api.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// GDC punto 12 — la migración <c>GDC_NumeroComparendoSoloDigitos</c> sobre una base propia: se migra hasta la anterior,
/// se siembran choques y se aplica. Lo que choca no cambia y queda reportado; el documento OCR sigue la misma regla que
/// el comparendo. Sin Postgres se omite en local y falla en CI.
/// </summary>
public sealed class MigracionNumeroSinPrefijoTests : IAsyncLifetime
{
    private const string Anterior = "20261022193744_GDC_ReprocesoOcr";
    private const string Objetivo = "20261106094303_GDC_NumeroComparendoSoloDigitos";
    private static readonly Guid TenantA = Guid.Parse("0199a000-0000-7000-8000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("0199a000-0000-7000-8000-00000000000b");

    private string _database = string.Empty;
    private string? _omitir;

    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("ConnectionStrings__Core") ?? $"Host=127.0.0.1;Port=5432;Username={Environment.UserName}";
        _database = new NpgsqlConnectionStringBuilder(server) { Database = $"flit_cmp_{Guid.NewGuid():N}"[..30] }.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{new NpgsqlConnectionStringBuilder(_database).Database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException ex) when (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            _omitir = $"Postgres no alcanzable: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_omitir is not null)
            return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_database) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{new NpgsqlConnectionStringBuilder(_database).Database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Limpia_LoQueNoChoca_YReporta_LoQueChoca_EnComparendosYDocumentosOcr()
    {
        Assert.SkipWhen(_omitir is not null, _omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = new NpgsqlConnection(_database);
        var avisos = new List<string>();
        conexion.Notice += (_, e) => avisos.Add(e.Notice.MessageText);
        await conexion.OpenAsync(ct);
        var opciones = new DbContextOptionsBuilder<ComparendosDb>()
            .UseNpgsql(conexion, n => n.MigrationsHistoryTable("__EFMigrationsHistory", ComparendosDb.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new ComparendosDb(opciones);
        var migrador = db.GetService<IMigrator>();
        await migrador.MigrateAsync(Anterior, cancellationToken: ct);

        // Siembra y lectura con SQL explícito, solo con columnas que ya existen en la migración anterior: el modelo de EF
        // de hoy puede traer columnas que esa versión de la base aún no tiene.
        var paquete = Guid.NewGuid();
        await EjecutarAsync(conexion, """
            INSERT INTO comparendos.paquete_ocr (id, tenant_id, nombre, estado, cantidad_documentos, cantidad_pendientes, cantidad_completos,
                cantidad_incompletos, cantidad_fallidos, cantidad_duplicados, cantidad_registrados, cantidad_descartados, prompt_version_id, creado_por, creado_en)
            VALUES (@paquete, @a, 'Lote', 'Pendiente', 0, 0, 0, 0, 0, 0, 0, 0, @vacio, @vacio, now())
            """, ct, ("paquete", paquete));
        // Tenant A: dos con prefijo distinto que quedarían iguales (choque), uno con prefijo cuyo número ya existe sin
        // prefijo (choque) y uno que no choca. Tenant B: el mismo número del choque de A, que en B no choca.
        foreach (var (tenant, numero) in new[]
                 {
                     (TenantA, "D1234567890"), (TenantA, "A1234567890"), (TenantA, "D9999999999"), (TenantA, "9999999999"),
                     (TenantA, "D5555555555"), (TenantA, "AV3"), (TenantA, "7777777777"), (TenantB, "D1234567890"),
                 })
        {
            await EjecutarAsync(conexion, """
                INSERT INTO comparendos.comparendo (id, tenant_id, numero, numero_normalizado, tipo_ingreso, en_cobro_coactivo, activo, estado_gestion,
                    estado_notificacion_conductor, estado_cambio_contraventor, estado_consulta_contraventor, intentos_consulta_contraventor,
                    notificado_fisicamente, pagado, tiene_dp, gestionado, ultimo_evento_en, creado_en)
                VALUES (gen_random_uuid(), @tenant, @numero, @numero, 0, false, true, 'SinGestion', 'SinNotificar', 'SinSolicitar', 'NoAplica', 0,
                    false, false, false, false, now(), now())
                """, ct, ("tenant", tenant), ("numero", numero));
        }

        foreach (var numero in new[] { "D1234567890", "D5555555555", "D7777777777" })
        {
            await EjecutarAsync(conexion, """
                INSERT INTO comparendos.documento_ocr (id, tenant_id, paquete_id, archivo_id, nombre_archivo, tipo_archivo, estado, intentos,
                    campos_faltantes, numero_comparendo_extraido, numero_normalizado)
                VALUES (gen_random_uuid(), @a, @paquete, gen_random_uuid(), @numero || '.pdf', 'PDF', 'Completo', 0, '{}', @numero, @numero)
                """, ct, ("paquete", paquete), ("numero", numero));
        }

        await migrador.MigrateAsync(Objetivo, cancellationToken: ct);

        var comparendos = await LeerAsync(conexion, "SELECT tenant_id, numero, numero_normalizado FROM comparendos.comparendo", ct);
        string NumeroDe(Guid tenant, string numero) => comparendos.Single(c => c.Tenant == tenant && c.Numero == numero).Normalizado;
        NumeroDe(TenantA, "D1234567890").Should().Be("D1234567890");
        NumeroDe(TenantA, "A1234567890").Should().Be("A1234567890");
        NumeroDe(TenantA, "D9999999999").Should().Be("D9999999999");
        NumeroDe(TenantA, "D5555555555").Should().Be("5555555555");
        NumeroDe(TenantA, "AV3").Should().Be("AV3");
        NumeroDe(TenantB, "D1234567890").Should().Be("1234567890");

        var documentos = await LeerAsync(conexion, "SELECT tenant_id, nombre_archivo, numero_normalizado FROM comparendos.documento_ocr", ct);
        documentos.Single(d => d.Numero == "D1234567890.pdf").Normalizado.Should().Be("D1234567890", "su comparendo conserva el prefijo");
        documentos.Single(d => d.Numero == "D5555555555.pdf").Normalizado.Should().Be("5555555555");
        documentos.Single(d => d.Numero == "D7777777777.pdf").Normalizado.Should().Be("D7777777777", "ya hay un comparendo sin prefijo con ese número");

        var reporte = avisos.Where(a => a.StartsWith("GDC_NumeroComparendoSoloDigitos", StringComparison.Ordinal)).ToList();
        reporte.Should().Contain(a => a.Contains("comparendo sin cambiar") && a.Contains(TenantA.ToString()) && a.Contains("D1234567890"));
        reporte.Should().Contain(a => a.Contains("comparendo sin cambiar") && a.Contains(TenantA.ToString()) && a.Contains("A1234567890"));
        reporte.Should().Contain(a => a.Contains("comparendo sin cambiar") && a.Contains("D9999999999"));
        reporte.Should().Contain(a => a.Contains("documento OCR sin cambiar") && a.Contains(TenantA.ToString()) && a.Contains("D1234567890"));
        reporte.Should().Contain(a => a.Contains("documento OCR sin cambiar") && a.Contains("D7777777777"));
        reporte.Should().NotContain(a => a.Contains(TenantB.ToString()));
        reporte.Should().Contain(a => a.Contains("2 comparendos y 1 documentos OCR sin prefijo; 3 comparendos y 2 documentos sin cambiar por choque"));
    }

    private static async Task EjecutarAsync(NpgsqlConnection conexion, string sql, CancellationToken ct, params (string Nombre, object Valor)[] parametros)
    {
        await using var cmd = new NpgsqlCommand(sql, conexion);
        cmd.Parameters.AddWithValue("a", TenantA);
        cmd.Parameters.AddWithValue("vacio", Guid.Empty);
        foreach (var (nombre, valor) in parametros)
            cmd.Parameters.AddWithValue(nombre, valor);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Filas (tenant, texto, número normalizado) de una consulta de tres columnas.</summary>
    private static async Task<List<(Guid Tenant, string Numero, string Normalizado)>> LeerAsync(NpgsqlConnection conexion, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conexion);
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        var filas = new List<(Guid, string, string)>();
        while (await lector.ReadAsync(ct))
            filas.Add((lector.GetGuid(0), lector.GetString(1), lector.GetString(2)));
        return filas;
    }
}
