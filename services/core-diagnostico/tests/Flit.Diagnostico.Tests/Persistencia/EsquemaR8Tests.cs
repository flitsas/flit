using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Persistencia;

/// <summary>
/// HU #14563 B8 (AC35, AC39; schema §3.2) — <c>diagnostico.notificaciones</c> sobre el schema migrado: el CHECK de <c>tipo</c>
/// (cuatro tipos, sin <c>DICTAMEN_AJUSTADO</c>), el CHECK de <c>datos</c> (sin texto libre, plano, hasta 512 bytes), la
/// idempotencia por <c>(tenant_id, usuario_id, tipo, referencia_id)</c>, los índices y el CASCADE hacia la cabecera.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EsquemaR8Tests
/// </code>
/// </example>
public sealed class EsquemaR8Tests
{
    [Theory]
    [InlineData("DICTAMEN_AJUSTADO")]
    [InlineData("OTRO")]
    public async Task Ck_tipo_rechaza_tipos_fuera_de_los_cuatro(string tipo)
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote) = await SembrarLoteAsync(conexion, ct);

        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, tipo, "{}", ct), "ck_notificaciones_tipo");
    }

    [Fact]
    public async Task Ck_datos_acepta_contadores_y_codigos()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote) = await SembrarLoteAsync(conexion, ct);

        await InsertarAsync(conexion, tenant, lote, "DERIVACION_TERMINADA", "{\"creadas\":3,\"fallidas\":1,\"parcial\":true,\"motivo\":\"OT_NO_HABILITADO\"}", ct);
        (await ListarAsync(conexion, $"SELECT tipo FROM diagnostico.notificaciones WHERE diagnostico_id = '{lote}'", ct)).Should().Equal("DERIVACION_TERMINADA");
    }

    [Theory]
    [InlineData("{\"nombre\":\"juan perez\"}")]
    [InlineData("{\"placa\":\"abc123\"}")]
    [InlineData("{\"x\":{\"y\":1}}")]
    [InlineData("{\"x\":[1,2]}")]
    [InlineData("[1]")]
    public async Task Ck_datos_rechaza_texto_libre_y_anidados(string datos)
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote) = await SembrarLoteAsync(conexion, ct);

        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, "LOTE_TERMINADO", datos, ct), "ck_notificaciones_datos");
    }

    [Fact]
    public async Task Ck_datos_rechaza_mas_de_512_bytes()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote) = await SembrarLoteAsync(conexion, ct);
        var grande = "{" + string.Join(",", Enumerable.Range(0, 60).Select(i => $"\"C{i:D2}\":1234567")) + "}";

        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, "LOTE_TERMINADO", grande, ct), "ck_notificaciones_datos");
    }

    [Fact]
    public async Task Idempotencia_leida_indices_cascade_y_sin_rls()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = await AbrirAsync(api, ct);
        var (tenant, lote) = await SembrarLoteAsync(conexion, ct);
        var usuario = Guid.NewGuid();
        var referencia = Guid.NewGuid();

        await InsertarAsync(conexion, tenant, lote, "ZIP_EVIDENCIAS_LISTO", "{}", ct, usuario, referencia);
        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, "ZIP_EVIDENCIAS_LISTO", "{}", ct, usuario, referencia),
            "ux_notificaciones_idempotencia", PostgresErrorCodes.UniqueViolation);
        await DebeViolarAsync(() => InsertarAsync(conexion, tenant, lote, "ZIP_EVIDENCIAS_FALLIDO", "{}", ct, leidaAntes: true),
            "ck_notificaciones_leida");

        var indices = await ListarAsync(conexion, "SELECT indexname::text FROM pg_indexes WHERE schemaname = 'diagnostico' AND tablename = 'notificaciones'", ct);
        indices.Should().Contain(["pk_notificaciones", "ux_notificaciones_idempotencia", "ix_notificaciones_bandeja",
            "ix_notificaciones_no_leidas", "ix_notificaciones_diagnostico", "ix_notificaciones_retencion"]);
        (await ListarAsync(conexion, "SELECT confdeltype::text FROM pg_constraint WHERE conname = 'fk_notificaciones_diagnosticos'", ct))
            .Should().Equal("c");
        (await ListarAsync(conexion, "SELECT policyname::text FROM pg_policies WHERE schemaname = 'diagnostico' AND tablename = 'notificaciones'", ct))
            .Should().BeEmpty("el schema diagnostico no usa RLS (ADR-0072)");
    }

    internal static async Task InsertarAsync(
        NpgsqlConnection conexion, Guid tenant, Guid lote, string tipo, string datos, CancellationToken ct,
        Guid? usuario = null, Guid? referencia = null, bool leidaAntes = false, DateTimeOffset? creadaEn = null)
    {
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.notificaciones (id, tenant_id, usuario_id, tipo, diagnostico_id, referencia_id, datos, creada_en, leida_en) " +
            "VALUES (@id, @t, @u, @tipo, @d, @r, @datos::jsonb, @c, @l)", conexion);
        var creada = creadaEn ?? DateTimeOffset.UtcNow;
        insertar.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insertar.Parameters.AddWithValue("t", tenant);
        insertar.Parameters.AddWithValue("u", usuario ?? Guid.NewGuid());
        insertar.Parameters.AddWithValue("tipo", tipo);
        insertar.Parameters.AddWithValue("d", lote);
        insertar.Parameters.AddWithValue("r", referencia ?? Guid.NewGuid());
        insertar.Parameters.AddWithValue("datos", datos);
        insertar.Parameters.AddWithValue("c", creada);
        insertar.Parameters.AddWithValue("l", leidaAntes ? creada.AddMinutes(-1) : DBNull.Value);
        await insertar.ExecuteNonQueryAsync(ct);
    }

    internal static async Task<(Guid Tenant, Guid Lote)> SembrarLoteAsync(NpgsqlConnection conexion, CancellationToken ct, Guid? tenant = null)
    {
        var t = tenant ?? Guid.NewGuid();
        var lote = Guid.CreateVersion7();
        await using var insertar = new NpgsqlCommand(
            "INSERT INTO diagnostico.diagnosticos (id, tenant_id, nombre, modo, alcance, configuracion, estado, usuario_creador_id, creado_en) " +
            "VALUES (@id, @t, 'Lote', 'SIMPLE', '[\"RUNT\"]'::jsonb, '{}'::jsonb, 'CREADO', @usuario, now())", conexion);
        insertar.Parameters.AddWithValue("id", lote);
        insertar.Parameters.AddWithValue("t", t);
        insertar.Parameters.AddWithValue("usuario", Guid.NewGuid());
        await insertar.ExecuteNonQueryAsync(ct);
        return (t, lote);
    }

    internal static async Task<List<string>> ListarAsync(NpgsqlConnection conexion, string sql, CancellationToken ct)
    {
        var filas = new List<string>();
        await using var comando = new NpgsqlCommand(sql, conexion);
        await using var lector = await comando.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
            filas.Add(lector.GetString(0));
        return filas;
    }

    internal static async Task<NpgsqlConnection> AbrirAsync(ApiDePrueba api, CancellationToken ct)
    {
        var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        return conexion;
    }

    private static async Task DebeViolarAsync(Func<Task> accion, string restriccion, string sqlState = PostgresErrorCodes.CheckViolation)
    {
        var error = (await accion.Should().ThrowAsync<PostgresException>()).Which;
        error.SqlState.Should().Be(sqlState);
        error.ConstraintName.Should().Be(restriccion);
    }
}
