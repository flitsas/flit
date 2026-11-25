using System.Net;
using System.Text.Json;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Catalogos;
using Flit.Impuestos.Tests.Configuracion;
using Flit.Impuestos.Tests.Credenciales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
using A = Flit.Impuestos.Tests.Api.ApiDePrueba;

namespace Flit.Impuestos.Tests.Concurrencia;

/// <summary>
/// HU #14694 (revisión previa al PR) — migración <c>R2_Revision</c>: comentarios <c>@pii</c> de las columnas de actor, nombre, motivo,
/// secretaría y placa; FK <c>fk_notificaciones_liquidaciones</c> con <c>ON DELETE RESTRICT</c> y su índice; CHECK del secreto anterior
/// (las dos columnas null o las dos con valor) y de la forma de los jsonb; índices parciales de ítems en vuelo y pendientes; las
/// columnas <c>ultimo_cambio_plataforma_en</c> y <c>ultima_firma_invalida_en</c>. Su <c>Down</c> deja el esquema como en R1.
/// </summary>
/// <example>
/// <code>
/// dotnet test … --filter FullyQualifiedName~EsquemaRevisionR2Tests
/// </code>
/// </example>
public sealed class EsquemaRevisionR2Tests
{
    private const string R1 = "20261125074835_R1_AltaIdempotente";

    [Fact]
    public async Task R2_deja_comentarios_pii_restricciones_e_indices()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await A.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        await using var c = new NpgsqlConnection(api.BaseDeDatos);
        await c.OpenAsync(ct);

        var comentarios = new Dictionary<(string Tabla, string Columna), string>
        {
            [("liquidaciones", "creada_por_nombre")] = "@pii:medium Nombre del usuario interno (en claro por decisión del usuario)",
            [("liquidaciones", "cerrada_por_nombre")] = "@pii:medium Nombre del usuario interno (en claro por decisión del usuario)",
            [("liquidaciones", "motivo_cierre")] = "@pii:low Texto libre del usuario; puede contener datos personales",
        };
        foreach (var ((tabla, columna), esperado) in comentarios)
            (await ComentarioAsync(c, tabla, columna, ct)).Should().Be(esperado, $"{tabla}.{columna}");
        foreach (var (tabla, columna, prefijo) in new[]
                 {
                     ("liquidacion_items", "secretaria_nombre", "@pii:none "),
                     ("liquidaciones", "creada_por", "@pii:low "),
                     ("liquidaciones", "cerrada_por", "@pii:low "),
                     ("credenciales_empresa", "actualizado_por", "@pii:low "),
                     ("configuracion_empresa", "actualizado_por", "@pii:low "),
                     ("historial_aprovisionamiento", "actor_id", "@pii:low "),
                     ("auditoria_lectura_backoffice", "actor_id", "@pii:low "),
                     ("notificaciones", "leida_por", "@pii:low "),
                     ("liquidacion_items", "placa", "@pii:low "),
                 })
        {
            (await ComentarioAsync(c, tabla, columna, ct)).Should().StartWith(prefijo, $"{tabla}.{columna}");
        }

        (await EscalarAsync<string>(c, "SELECT confdeltype::text FROM pg_constraint WHERE conname = 'fk_notificaciones_liquidaciones'", ct))
            .Should().Be("r", "ON DELETE RESTRICT");
        (await EscalarAsync<long>(c,
            "SELECT count(*) FROM pg_indexes WHERE schemaname = 'impuestos' AND tablename = 'notificaciones' AND indexdef LIKE '%(liquidacion_id)%'", ct))
            .Should().Be(1, "la FK lleva su índice");
        foreach (var check in new[] { "ck_credenciales_empresa_secreto_anterior", "ck_liquidaciones_config_copia", "ck_liquidacion_periodos_otros_cargos", "ck_fuentes_api_params_schema" })
            (await EscalarAsync<long>(c, $"SELECT count(*) FROM pg_constraint WHERE conname = '{check}' AND contype = 'c'", ct)).Should().Be(1, check);
        (await DefinicionAsync(c, "ix_liquidacion_items_en_vuelo", ct)).Should().Contain("(tenant_id)").And.Contain("CONSULTANDO_RUNT").And.Contain("ENVIANDO");
        (await DefinicionAsync(c, "ix_liquidacion_items_pendientes", ct)).Should().Contain("(tenant_id, liquidacion_id)").And.Contain("'PENDIENTE'");
        foreach (var columna in new[] { "ultimo_cambio_plataforma_en", "ultima_firma_invalida_en" })
            (await EscalarAsync<long>(c, $"SELECT count(*) FROM information_schema.columns WHERE table_schema = 'impuestos' AND table_name = 'aprovisionamientos' AND column_name = '{columna}'", ct))
                .Should().Be(1, columna);
    }

    [Fact]
    public async Task El_CHECK_del_secreto_anterior_rechaza_una_sola_columna_con_valor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await A.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await Resultados.EntornoResultados.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, Resultados.EntornoResultados.Secreto, ct);
        await using var db = api.Contexto(empresa);

        var soloHasta = () => db.Database.ExecuteSqlAsync(
            $"UPDATE impuestos.credenciales_empresa SET webhook_secret_anterior_hasta = now() WHERE tenant_id = {empresa}", ct);

        (await soloHasta.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Un_params_schema_que_no_es_objeto_se_guarda_como_objeto_vacio()
    {
        var ct = TestContext.Current.CancellationToken;
        var doble = new ApiLiquidacionDePrueba
        {
            Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.OK,
                "{\"items\":[{\"id\":\"f-05\",\"name\":\"Antioquia\",\"department_code\":\"05\",\"department_name\":\"ANTIOQUIA\",\"active\":true,\"params_schema\":[\"x\"]}]}"),
        };
        await using var api = await CargaManualCredencialesTests.ConApiAsync(doble);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await ConfiguracionTests.SembrarListaAsync(api, empresa, ct);

        using var r = await api.Cliente(ConfiguracionTests.TokenAdmin(api, empresa)).GetAsync(SincronizacionFuentesTests.Ruta, ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync(ct));
        await using var db = api.Contexto(null);
        var fuente = await db.Set<FuenteApi>().AsNoTracking().SingleAsync(ct);
        JsonDocument.Parse(fuente.ParamsSchema).RootElement.ValueKind.Should().Be(JsonValueKind.Object, "el CHECK de R2 exige un objeto");
    }

    [Fact]
    public async Task El_Down_de_R2_vuelve_al_esquema_de_R1_y_se_puede_volver_a_subir()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await A.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var opciones = new DbContextOptionsBuilder<ImpuestosDb>();
        ImpuestosDb.Configure(opciones, api.BaseDeDatos);
        await using var db = new ImpuestosDb(opciones.Options, new Api.EmpresaFija(null));
        var migrador = db.GetService<IMigrator>();
        await using var c = new NpgsqlConnection(api.BaseDeDatos);
        await c.OpenAsync(ct);
        (await db.Database.GetAppliedMigrationsAsync(ct)).Should().Contain(m => m.EndsWith("_R2_Revision", StringComparison.Ordinal));

        await migrador.MigrateAsync(R1, ct);

        (await EscalarAsync<long>(c, "SELECT count(*) FROM pg_constraint WHERE conname = 'fk_notificaciones_liquidaciones'", ct)).Should().Be(0);
        (await EscalarAsync<long>(c, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'impuestos' AND column_name IN ('ultimo_cambio_plataforma_en', 'ultima_firma_invalida_en')", ct)).Should().Be(0);
        (await ComentarioAsync(c, "liquidaciones", "creada_por_nombre", ct)).Should().Be("Nombre del actor según su token al crear (1.2.0).");

        await migrador.MigrateAsync(null, ct);
        (await EscalarAsync<long>(c, "SELECT count(*) FROM pg_constraint WHERE conname = 'fk_notificaciones_liquidaciones'", ct)).Should().Be(1);
    }

    private static Task<string?> ComentarioAsync(NpgsqlConnection c, string tabla, string columna, CancellationToken ct) =>
        EscalarAsync<string?>(c,
            $"SELECT col_description('impuestos.{tabla}'::regclass, (SELECT attnum FROM pg_attribute WHERE attrelid = 'impuestos.{tabla}'::regclass AND attname = '{columna}'))", ct);

    private static Task<string?> DefinicionAsync(NpgsqlConnection c, string indice, CancellationToken ct) =>
        EscalarAsync<string?>(c, $"SELECT indexdef FROM pg_indexes WHERE schemaname = 'impuestos' AND indexname = '{indice}'", ct);

    private static async Task<T> EscalarAsync<T>(NpgsqlConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        var valor = await cmd.ExecuteScalarAsync(ct);
        return valor is null or DBNull ? default! : (T)valor;
    }
}
