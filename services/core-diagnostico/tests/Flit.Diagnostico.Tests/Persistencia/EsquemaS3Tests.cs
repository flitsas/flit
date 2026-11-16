using Flit.Diagnostico.Domain.Personas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Flit.Diagnostico.Tests.Persistencia;

/// <summary>
/// HU #14582 AC8 y AC9 (schema.md §1–§2, SP-2) — migración <c>S3_PropietarioDeclarado</c>: tres columnas nuevas en
/// <c>diagnostico.vehiculos</c> con sus CHECK, retiro de <c>locatario_declarado_doc_*</c> y migración de datos por copia de bytes
/// (sin descifrar): el declarado vigente (<c>DECLARADO_CLIENTE</c>) y el respaldo pasan al propietario declarado y el locatario
/// declarado se limpia. El Down reconstruye el estado anterior; subir otra vez deja los mismos bytes (idempotente). Se siembra por
/// SQL en el esquema S2 con el protector real; documentos ficticios.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~EsquemaS3Tests
/// </code>
/// </example>
public sealed class EsquemaS3Tests
{
    private const string MigracionAnterior = "20261115083016_S2_AccesosReporte";
    private const string Declarado = "9999900301";
    private const string Respaldo = "9999900302";
    private const string LocatarioProveedor = "9999900303";

    // AC8 + AC9 — Up mueve los bytes, crea columnas y checks y retira el respaldo; Down los devuelve; Up otra vez es idéntico.
    [Fact]
    public async Task HU14582_AC8_AC9_S3_migra_por_copia_de_bytes_y_es_reversible_e_idempotente()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var protector = api.App.Services.GetRequiredService<IProtectorDocumentoPersona>();
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        await using var db = DosEmpresas.Contexto(api.BaseDeDatos, null);
        var migrador = db.GetService<IMigrator>();
        var s3 = db.Database.GetMigrations().Single(m => m.EndsWith("_S3_PropietarioDeclarado", StringComparison.Ordinal));

        await migrador.MigrateAsync(MigracionAnterior, ct);
        var declaradoCifrado = protector.Proteger(Declarado);
        var respaldoCifrado = protector.Proteger(Respaldo);
        var locatarioCifrado = protector.Proteger(LocatarioProveedor);
        var (tenant, lote) = await LoteAsync(conexion, ct);
        var conDeclarado = await VehiculoAsync(conexion, tenant, lote, "TST301", 1, ct,
            ("locatario_doc_tipo", "CC"), ("locatario_doc_cifrado", declaradoCifrado), ("locatario_doc_origen", "DECLARADO_CLIENTE"));
        var conRespaldo = await VehiculoAsync(conexion, tenant, lote, "TST302", 2, ct,
            ("locatario_doc_tipo", "CC"), ("locatario_doc_cifrado", locatarioCifrado), ("locatario_doc_origen", "PROVEEDOR"),
            ("locatario_declarado_doc_tipo", "CE"), ("locatario_declarado_doc_cifrado", respaldoCifrado));
        var sinNada = await VehiculoAsync(conexion, tenant, lote, "TST303", 3, ct);

        await migrador.MigrateAsync(s3, ct);
        await VerificarS3Async();

        await migrador.MigrateAsync(MigracionAnterior, ct);
        (await ColumnasAsync(conexion, ct)).Should().Contain(["locatario_declarado_doc_tipo", "locatario_declarado_doc_cifrado"])
            .And.NotContain(["propietario_declarado_doc_tipo", "propietario_declarado_doc_cifrado", "cambio_propietario"]);
        var abajo = await FilaS2Async(conexion, conDeclarado, ct);
        abajo.Should().Be(("CC", "DECLARADO_CLIENTE", Hex(declaradoCifrado), (string?)null), "el Down devuelve el declarado vigente al locatario");
        (await FilaS2Async(conexion, conRespaldo, ct)).Should().Be(("CC", "PROVEEDOR", Hex(locatarioCifrado), Hex(respaldoCifrado)),
            "con locatario del proveedor, el declarado vuelve al respaldo");
        (await FilaS2Async(conexion, sinNada, ct)).Should().Be(((string?)null, (string?)null, (string?)null, (string?)null));

        await db.Database.MigrateAsync(ct);
        (await db.Database.GetPendingMigrationsAsync(ct)).Should().BeEmpty();
        await VerificarS3Async();
        await db.Database.MigrateAsync(ct);
        await VerificarS3Async();

        async Task VerificarS3Async()
        {
            var columnas = await ColumnasAsync(conexion, ct);
            columnas.Should().Contain(["propietario_declarado_doc_tipo", "propietario_declarado_doc_cifrado", "cambio_propietario"]);
            columnas.Should().NotContain(["locatario_declarado_doc_tipo", "locatario_declarado_doc_cifrado"], "SP-8: PII sin uso se retira");
            var checks = await ListaAsync(conexion, "SELECT conname FROM pg_constraint WHERE contype = 'c' AND conrelid = 'diagnostico.vehiculos'::regclass", ct);
            checks.Should().Contain(["ck_vehiculos_propietario_declarado", "ck_vehiculos_cambio_propietario", "ck_locatario_origen"])
                .And.NotContain(["ck_vehiculos_locatario_declarado", "ck_vehiculos_locatario_declarado_con_proveedor"]);

            var vigente = await FilaS3Async(conexion, conDeclarado, ct);
            vigente.Should().Be(("CC", Hex(declaradoCifrado), (string?)null, (string?)null, (string?)null), "SP-2: mismos bytes y el locatario declarado se limpia");
            protector.Desproteger(Convert.FromHexString(vigente.Cifrado!)).Should().Be(Declarado);
            var respaldo = await FilaS3Async(conexion, conRespaldo, ct);
            respaldo.Should().Be(("CE", Hex(respaldoCifrado), "CC", "PROVEEDOR", Hex(locatarioCifrado)), "el respaldo pasa al propietario declarado; el locatario del RUNT queda");
            (await FilaS3Async(conexion, sinNada, ct)).Should().Be(((string?)null, (string?)null, (string?)null, (string?)null, (string?)null));
            (await ListaAsync(conexion, $"SELECT coalesce(cambio_propietario::text, 'null') FROM diagnostico.vehiculos WHERE diagnostico_id = '{lote}'", ct))
                .Should().OnlyContain(c => c == "null", "la SQL no puede descifrar: queda para el cálculo de la aplicación");
        }
    }

    // AC8 — los CHECK nuevos: tipo y cifrado del declarado van juntos; cambio_propietario solo con declarado.
    [Fact]
    public async Task HU14582_AC8_checks_del_propietario_declarado()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        await using var conexion = new NpgsqlConnection(api.BaseDeDatos);
        await conexion.OpenAsync(ct);
        var (tenant, lote) = await LoteAsync(conexion, ct);
        var vehiculo = await VehiculoAsync(conexion, tenant, lote, "TST311", 1, ct);

        (await Violacion(() => EjecutarAsync(conexion, $"UPDATE diagnostico.vehiculos SET propietario_declarado_doc_tipo = 'CC' WHERE id = '{vehiculo}'", ct)))
            .Should().Be(("23514", "ck_vehiculos_propietario_declarado"));
        (await Violacion(() => EjecutarAsync(conexion, $"UPDATE diagnostico.vehiculos SET cambio_propietario = true WHERE id = '{vehiculo}'", ct)))
            .Should().Be(("23514", "ck_vehiculos_cambio_propietario"));
        await EjecutarAsync(conexion,
            $"UPDATE diagnostico.vehiculos SET propietario_declarado_doc_tipo = 'CC', propietario_declarado_doc_cifrado = decode('0102', 'hex'), cambio_propietario = false WHERE id = '{vehiculo}'", ct);
        (await ListaAsync(conexion, $"SELECT cambio_propietario::text FROM diagnostico.vehiculos WHERE id = '{vehiculo}'", ct)).Should().Equal("false");
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    private static async Task<(Guid Tenant, Guid Lote)> LoteAsync(NpgsqlConnection conexion, CancellationToken ct)
    {
        var tenant = Guid.NewGuid();
        var lote = Guid.CreateVersion7();
        await EjecutarAsync(conexion,
            "INSERT INTO diagnostico.diagnosticos (id, tenant_id, nombre, modo, alcance, configuracion, estado, usuario_creador_id, creado_en) " +
            $"VALUES ('{lote}', '{tenant}', 'Lote', 'SIMPLE', '[\"RUNT\"]'::jsonb, '{{}}'::jsonb, 'CREADO', '{Guid.NewGuid()}', now())", ct);
        return (tenant, lote);
    }

    private static async Task<Guid> VehiculoAsync(
        NpgsqlConnection conexion, Guid tenant, Guid lote, string placa, int orden, CancellationToken ct, params (string Columna, object Valor)[] extra)
    {
        var id = Guid.CreateVersion7();
        var columnas = string.Concat(extra.Select(e => ", " + e.Columna));
        var valores = string.Concat(extra.Select((_, i) => $", @p{i}"));
        await using var comando = new NpgsqlCommand(
            $"INSERT INTO diagnostico.vehiculos (id, tenant_id, diagnostico_id, identificador_original, identificador_norm, tipo_identificador, placa, orden{columnas}) " +
            $"VALUES ('{id}', '{tenant}', '{lote}', '{placa}', '{placa}', 'PLACA_CARRO', '{placa}', {orden}{valores})", conexion);
        for (var i = 0; i < extra.Length; i++)
            comando.Parameters.AddWithValue($"p{i}", extra[i].Valor);
        await comando.ExecuteNonQueryAsync(ct);
        return id;
    }

    private static async Task<(string? Tipo, string? Origen, string? Cifrado, string? Respaldo)> FilaS2Async(NpgsqlConnection conexion, Guid id, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(
            "SELECT locatario_doc_tipo, locatario_doc_origen, encode(locatario_doc_cifrado, 'hex'), encode(locatario_declarado_doc_cifrado, 'hex') " +
            "FROM diagnostico.vehiculos WHERE id = @id", conexion);
        comando.Parameters.AddWithValue("id", id);
        await using var fila = await comando.ExecuteReaderAsync(ct);
        (await fila.ReadAsync(ct)).Should().BeTrue();
        return (Texto(fila, 0), Texto(fila, 1), Texto(fila, 2)?.ToUpperInvariant(), Texto(fila, 3)?.ToUpperInvariant());
    }

    private static async Task<(string? Tipo, string? Cifrado, string? LocatarioTipo, string? LocatarioOrigen, string? LocatarioCifrado)> FilaS3Async(
        NpgsqlConnection conexion, Guid id, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(
            "SELECT propietario_declarado_doc_tipo, encode(propietario_declarado_doc_cifrado, 'hex'), locatario_doc_tipo, locatario_doc_origen, " +
            "encode(locatario_doc_cifrado, 'hex') FROM diagnostico.vehiculos WHERE id = @id", conexion);
        comando.Parameters.AddWithValue("id", id);
        await using var fila = await comando.ExecuteReaderAsync(ct);
        (await fila.ReadAsync(ct)).Should().BeTrue();
        return (Texto(fila, 0), Texto(fila, 1)?.ToUpperInvariant(), Texto(fila, 2), Texto(fila, 3), Texto(fila, 4)?.ToUpperInvariant());
    }

    private static string? Texto(NpgsqlDataReader fila, int i) => fila.IsDBNull(i) ? null : fila.GetString(i);

    private static Task<List<string>> ColumnasAsync(NpgsqlConnection conexion, CancellationToken ct) =>
        ListaAsync(conexion, "SELECT column_name FROM information_schema.columns WHERE table_schema = 'diagnostico' AND table_name = 'vehiculos'", ct);

    private static async Task<(string SqlState, string? Constraint)> Violacion(Func<Task> accion)
    {
        var error = (await FluentActions.Awaiting(accion).Should().ThrowAsync<PostgresException>()).Which;
        return (error.SqlState, error.ConstraintName);
    }

    private static async Task EjecutarAsync(NpgsqlConnection conexion, string sql, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync(ct);
    }

    private static async Task<List<string>> ListaAsync(NpgsqlConnection conexion, string sql, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(sql, conexion);
        var lista = new List<string>();
        await using var fila = await comando.ExecuteReaderAsync(ct);
        while (await fila.ReadAsync(ct))
            lista.Add(fila.GetString(0));
        return lista;
    }
}
