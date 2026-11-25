using Flit.Comparendos.Api.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Flit.Comparendos.Tests.Api;

/// <summary>
/// GDC-EVID §10 — la migración <c>GDC_AsociacionLote</c> sobre una base propia: con un documento PorAsociar en un lote, el
/// Down lo devuelve a Duplicado (el código anterior no conoce ese estado), repone el contador de duplicados del paquete y
/// quita las tablas; el Up vuelve a aplicar. Sin Postgres se omite en local y falla en CI.
/// </summary>
public sealed class MigracionAsociacionLoteTests : IAsyncLifetime
{
    private const string Anterior = "20261124193846_GDC_OcrDocumentoAsociado";
    private const string Objetivo = "20261125155855_GDC_AsociacionLote";
    private static readonly Guid Tenant = Guid.Parse("0199a000-0000-7000-8000-0000000000c1");

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
    public async Task Down_DevuelveLosPorAsociarADuplicado_ReponeElContador_YElUpVuelveAAplicar()
    {
        Assert.SkipWhen(_omitir is not null, _omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = new NpgsqlConnection(_database);
        await conexion.OpenAsync(ct);
        var opciones = new DbContextOptionsBuilder<ComparendosDb>()
            .UseNpgsql(conexion, n => n.MigrationsHistoryTable("__EFMigrationsHistory", ComparendosDb.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var db = new ComparendosDb(opciones);
        var migrador = db.GetService<IMigrator>();
        await migrador.MigrateAsync(Objetivo, cancellationToken: ct);

        // Un paquete con un documento esperando al trabajador (PorAsociar, contado en cantidad_por_asociar) y otro Duplicado normal.
        var (paquete, enLote, otro, lote) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await EjecutarAsync(conexion, """
            INSERT INTO comparendos.paquete_ocr (id, tenant_id, nombre, estado, cantidad_documentos, cantidad_pendientes, cantidad_completos,
                cantidad_incompletos, cantidad_fallidos, cantidad_duplicados, cantidad_registrados, cantidad_descartados, cantidad_por_asociar,
                prompt_version_id, creado_por, creado_en)
            VALUES (@paquete, @t, 'Lote', 'Cerrado', 2, 0, 0, 0, 0, 1, 0, 0, 1, @vacio, @vacio, now())
            """, ct, ("paquete", paquete));
        foreach (var (id, estado) in new[] { (enLote, "PorAsociar"), (otro, "Duplicado") })
        {
            await EjecutarAsync(conexion, """
                INSERT INTO comparendos.documento_ocr (id, tenant_id, paquete_id, archivo_id, nombre_archivo, tipo_archivo, estado, intentos,
                    campos_faltantes, numero_comparendo_extraido, numero_normalizado, proximo_intento_en)
                VALUES (@id, @t, @paquete, gen_random_uuid(), 'x.pdf', 'PDF', @estado, 0, '{}', '1100100000', '1100100000', now())
                """, ct, ("id", id), ("paquete", paquete), ("estado", estado));
        }

        await EjecutarAsync(conexion, """
            INSERT INTO comparendos.asociacion_lote (id, tenant_id, paquete_id, solicitado_por, solicitado_por_nombre, solicitado_en)
            VALUES (@lote, @t, @paquete, @vacio, 'Gestora', now());
            INSERT INTO comparendos.asociacion_lote_documento (lote_id, documento_id, resultado, intentos) VALUES (@lote, @doc, 'PorAsociar', 0);
            """, ct, ("lote", lote), ("paquete", paquete), ("doc", enLote));

        await migrador.MigrateAsync(Anterior, cancellationToken: ct);

        (await EscalarAsync<string>(conexion, "SELECT estado FROM comparendos.documento_ocr WHERE id = @id", ct, ("id", enLote))).Should().Be("Duplicado");
        (await EscalarAsync<object>(conexion, "SELECT proximo_intento_en FROM comparendos.documento_ocr WHERE id = @id", ct, ("id", enLote))).Should().Be(DBNull.Value);
        (await EscalarAsync<string>(conexion, "SELECT estado FROM comparendos.documento_ocr WHERE id = @id", ct, ("id", otro))).Should().Be("Duplicado", "el resto no cambia");
        (await EscalarAsync<int>(conexion, "SELECT cantidad_duplicados FROM comparendos.paquete_ocr WHERE id = @paquete", ct, ("paquete", paquete))).Should().Be(2, "el que estaba por asociar vuelve a contar como duplicado");
        (await EscalarAsync<long>(conexion, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'comparendos' AND table_name = 'paquete_ocr' AND column_name = 'cantidad_por_asociar'", ct)).Should().Be(0);
        (await EscalarAsync<long>(conexion, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'comparendos' AND table_name IN ('asociacion_lote', 'asociacion_lote_documento')", ct)).Should().Be(0);

        await migrador.MigrateAsync(Objetivo, cancellationToken: ct);
        (await EscalarAsync<long>(conexion, "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'comparendos' AND table_name IN ('asociacion_lote', 'asociacion_lote_documento')", ct)).Should().Be(2);
    }

    private static async Task EjecutarAsync(NpgsqlConnection conexion, string sql, CancellationToken ct, params (string Nombre, object Valor)[] parametros)
    {
        await using var cmd = Comando(conexion, sql, parametros);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> EscalarAsync<T>(NpgsqlConnection conexion, string sql, CancellationToken ct, params (string Nombre, object Valor)[] parametros)
    {
        await using var cmd = Comando(conexion, sql, parametros);
        var valor = await cmd.ExecuteScalarAsync(ct);
        return (T)(typeof(T) == typeof(int) ? Convert.ToInt32(valor, System.Globalization.CultureInfo.InvariantCulture) : valor!);
    }

    private static NpgsqlCommand Comando(NpgsqlConnection conexion, string sql, (string Nombre, object Valor)[] parametros)
    {
        var cmd = new NpgsqlCommand(sql, conexion);
        cmd.Parameters.AddWithValue("t", Tenant);
        cmd.Parameters.AddWithValue("vacio", Guid.Empty);
        foreach (var (nombre, valor) in parametros)
            cmd.Parameters.AddWithValue(nombre, valor);
        return cmd;
    }
}
