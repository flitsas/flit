using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flit.Diagnostico.Api.Modulos.Notificaciones;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Tests.Aislamiento;
using Flit.Diagnostico.Tests.Api;
using Flit.Diagnostico.Tests.Consultas;
using Flit.Diagnostico.Tests.Derivacion;
using Flit.Diagnostico.Tests.Persistencia;
using Flit.Diagnostico.Tests.Reportes;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using TableroAyudas = Flit.Diagnostico.Tests.Tablero.Tablero;

namespace Flit.Diagnostico.Tests.Notificaciones;

/// <summary>
/// HU #14563 B8 (AC34, AC35; ADR-0086 D-3, D8, P2 = a) — los cuatro productores escriben su notificación en la transacción del
/// hecho con <see cref="EscritorNotificaciones"/>: ZIP LISTA y ERROR definitivo (para <c>evidencias.solicitado_por</c>), derivación
/// MASIVA terminada (para quien la lanzó; la individual no notifica) y lote COMPLETADO o INCOMPLETO (para el creador, con la fila de
/// historial como referencia: un lote reabierto que vuelve a terminar notifica otra vez). Idempotente por
/// <c>(tenant, usuario, tipo, referencia)</c>; si la transacción se revierte no queda nada; sin destinatario no inserta.
/// </summary>
/// <example>
/// <code>
/// dotnet test services/core-diagnostico/tests/Flit.Diagnostico.Tests --no-build --filter FullyQualifiedName~ProductoresNotificacionesTests
/// </code>
/// </example>
public sealed class ProductoresNotificacionesTests
{
    // AC34 — idempotencia: el mismo hecho escrito dos veces (dos contextos) deja una sola fila.
    [Fact]
    public async Task Escritor_es_idempotente_por_destinatario_tipo_y_referencia()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos: [new("TST761", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        var usuario = Guid.NewGuid();
        var referencia = Guid.NewGuid();

        for (var i = 0; i < 2; i++)
        {
            await using var db = empresa.Db();
            await EscritorNotificaciones.AgregarAsync(db, empresa.Id, usuario, Notificacion.ZipEvidenciasListo, lote.Id, referencia, DateTimeOffset.UtcNow, ct);
            await db.SaveChangesAsync(ct);
        }

        (await FilasAsync(api, lote.Id, ct)).Should().Equal($"{Notificacion.ZipEvidenciasListo}|{usuario}|{referencia}");
    }

    // AC35 — sin destinatario no inserta; un tipo fuera de los cuatro (DICTAMEN_AJUSTADO, D8) no se acepta.
    [Fact]
    public async Task Escritor_sin_destinatario_no_inserta_y_rechaza_tipos_cerrados()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, vehiculos: [new("TST762", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        await using var db = empresa.Db();

        (await EscritorNotificaciones.AgregarAsync(db, empresa.Id, Guid.Empty, Notificacion.LoteTerminado, lote.Id, Guid.NewGuid(), DateTimeOffset.UtcNow, ct))
            .Should().BeFalse();
        await db.SaveChangesAsync(ct);
        (await FilasAsync(api, lote.Id, ct)).Should().BeEmpty();

        var cerrado = () => EscritorNotificaciones.AgregarAsync(db, empresa.Id, Guid.NewGuid(), "DICTAMEN_AJUSTADO", lote.Id, Guid.NewGuid(), DateTimeOffset.UtcNow, ct);
        await cerrado.Should().ThrowAsync<ArgumentException>();
    }

    // AC34 — ZIP LISTA: una notificación para quien lo pidió, con la evidencia como referencia.
    [Fact]
    public async Task Zip_lista_notifica_a_quien_lo_pidio()
    {
        await using var api = await ZipDePrueba.ArrancarAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await ZipLoteTests.LoteTerminadoAsync(empresa, "TST763", ct);
        var token = PdfDePrueba.Token(api, empresa.Id);
        var (_, pedido) = await ZipDePrueba.PedirAsync(api.Cliente(token), lote.Id, ct);
        var eid = pedido.GetProperty("id").GetGuid();

        (await ZipDePrueba.DrenarAsync(api, ct)).Should().Be(1);

        (await ZipLoteTests.EvidenciaAsync(empresa, eid, ct)).Estado.Should().Be(EstadoEvidencia.Lista);
        (await FilasAsync(api, lote.Id, ct)).Should().Equal($"{Notificacion.ZipEvidenciasListo}|{TableroAyudas.Usuario(token)}|{eid}");
    }

    // AC34 — ZIP en ERROR definitivo (sin reintentos): ZIP_EVIDENCIAS_FALLIDO para quien lo pidió, y ningún LISTO.
    [Fact]
    public async Task Zip_en_error_definitivo_notifica_fallido()
    {
        var fabrica = new FabricaAlmacenQueFallaEnZip();
        await using var api = await ZipDePrueba.ArrancarAsync(b =>
        {
            b.Services.AddSingleton<Flit.Diagnostico.Api.Infraestructura.Archivos.IFabricaAlmacen>(fabrica);
            b.Configuration["Diagnostico:Evidencias:MaxReintentos"] = "0";
        });
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await ZipLoteTests.LoteTerminadoAsync(empresa, "TST765", ct);
        var token = PdfDePrueba.Token(api, empresa.Id);
        var (_, pedido) = await ZipDePrueba.PedirAsync(api.Cliente(token), lote.Id, ct);
        var eid = pedido.GetProperty("id").GetGuid();

        (await ZipDePrueba.ProcesarAsync(api, ct)).Should().BeTrue();

        (await ZipLoteTests.EvidenciaAsync(empresa, eid, ct)).Estado.Should().Be(EstadoEvidencia.Error);
        (await FilasAsync(api, lote.Id, ct)).Should().Equal($"{Notificacion.ZipEvidenciasFallido}|{TableroAyudas.Usuario(token)}|{eid}");
    }

    // AC34 — lote COMPLETADO: LOTE_TERMINADO para el creador con la fila de historial como referencia; dos recálculos de la misma
    // transición en la misma transacción dejan una sola.
    [Fact]
    public async Task Lote_terminado_notifica_al_creador_con_la_fila_de_historial()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var creador = Guid.NewGuid();
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, version: 9, creador: creador, vehiculos: [new("TST766", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);

        await RecalcularAsync(api, empresa.Id, lote.Id, 9, guardar: true, dosVeces: true, ct);

        var historial = await HistorialAsync(api, lote.Id, ct);
        historial.Should().NotBeEmpty();
        (await FilasAsync(api, lote.Id, ct)).Should().Equal($"{Notificacion.LoteTerminado}|{creador}|{historial[0]}");
    }

    // AC34 — si la transacción del hecho se revierte, no queda notificación.
    [Fact]
    public async Task Lote_con_transaccion_revertida_no_deja_notificacion()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, version: 3, creador: Guid.NewGuid(), vehiculos: [new("TST767", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);

        await RecalcularAsync(api, empresa.Id, lote.Id, 3, guardar: false, dosVeces: false, ct);

        (await HistorialAsync(api, lote.Id, ct)).Should().BeEmpty();
        (await FilasAsync(api, lote.Id, ct)).Should().BeEmpty();
    }

    // AC34 — un lote reabierto que vuelve a terminar notifica otra vez (otra fila de historial, otra referencia).
    [Fact]
    public async Task Lote_reabierto_que_vuelve_a_terminar_notifica_otra_vez()
    {
        await using var api = await ApiDePrueba.ConBaseAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        var creador = Guid.NewGuid();
        var lote = await TableroAyudas.SembrarAsync(empresa, ct, version: 5, creador: creador, vehiculos: [new("TST768", [new(Fuente.Runt, EstadoCelda.Completado, 1)])]);
        await RecalcularAsync(api, empresa.Id, lote.Id, 5, guardar: true, dosVeces: false, ct);
        await EjecutarAsync(api, $"UPDATE diagnostico.diagnosticos SET estado = 'EN_PROCESO', cerrado_en = NULL WHERE id = '{lote.Id}'", ct);

        await RecalcularAsync(api, empresa.Id, lote.Id, 6, guardar: true, dosVeces: false, ct);

        var historial = await HistorialAsync(api, lote.Id, ct);
        historial.Should().HaveCount(2);
        (await FilasAsync(api, lote.Id, ct)).Should().BeEquivalentTo(
            [$"{Notificacion.LoteTerminado}|{creador}|{historial[0]}", $"{Notificacion.LoteTerminado}|{creador}|{historial[1]}"]);
    }

    // AC34 + P2 = a — derivación MASIVA: al cerrar su último ítem notifica DERIVACION_TERMINADA a quien la lanzó, una sola vez
    // aunque el mensaje se entregue de nuevo.
    [Fact]
    public async Task Derivacion_masiva_terminada_notifica_una_vez_a_quien_la_lanzo()
    {
        var falso = new DerivacionTramitesFalso();
        falso.Responder = _ => DerivacionTramitesFalso.Creado(Guid.NewGuid(), "TR-000931");
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await DerivacionDePrueba.OrganismoAsync(empresa, ct, "99001");
        var lote = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST769", true, "VIABLE")]);
        var token = DerivacionDePrueba.TokenDerivar(api, empresa);
        var respuesta = await api.Cliente(token).PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(lote.Id), new { vehiculo_ids = lote.Vehiculos.Select(v => v.Id) }, ct);
        respuesta.StatusCode.Should().Be(HttpStatusCode.Accepted, await respuesta.Content.ReadAsStringAsync(ct));
        var operacion = (await respuesta.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("operacion_id").GetGuid();
        (await FilasAsync(api, lote.Id, ct)).Should().BeEmpty("la operación sigue en curso");
        var mensaje = (await DerivacionDePrueba.MensajesAsync(empresa, ct)).Single();

        await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensaje.Data, ct);
        await DerivacionDePrueba.ConsumirAsync(api, empresa.Id, mensaje.Data, ct);

        (await FilasAsync(api, lote.Id, ct)).Should().Equal($"{Notificacion.DerivacionTerminada}|{TableroAyudas.Usuario(token)}|{operacion}");
    }

    // AC34 + P2 = a — masiva con todos no elegibles termina de inmediato y notifica; la individual desde el panel no notifica.
    [Fact]
    public async Task Derivacion_masiva_inmediata_notifica_y_la_individual_no()
    {
        var falso = new DerivacionTramitesFalso();
        falso.Responder = _ => DerivacionTramitesFalso.Creado(Guid.NewGuid(), "TR-000932");
        await using var api = await DerivacionDePrueba.ApiAsync(falso);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var ct = TestContext.Current.CancellationToken;
        var empresa = DosEmpresas.En(api).A;
        await DerivacionDePrueba.OrganismoAsync(empresa, ct, "99001");
        var token = DerivacionDePrueba.TokenDerivar(api, empresa);
        var masivo = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST770", true, "BLOQUEADO")]);
        var individual = await DerivacionDePrueba.SembrarAsync(empresa, ct, vehiculos: [("TST771", true, "VIABLE")]);

        var r1 = await api.Cliente(token).PostAsJsonAsync(DerivacionDePrueba.RutaMasivo(masivo.Id), new { vehiculo_ids = masivo.Vehiculos.Select(v => v.Id) }, ct);
        var operacion = (await r1.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("operacion_id").GetGuid();
        var r2 = await api.Cliente(token).PostAsJsonAsync(DerivacionDePrueba.Ruta(individual.Id, individual.Vehiculos[0].Id), new { tipo_tramite_codigo = "TRASPASO_STANDARD" }, ct);
        r2.StatusCode.Should().Be(HttpStatusCode.Created, await r2.Content.ReadAsStringAsync(ct));

        (await FilasAsync(api, masivo.Id, ct)).Should().Equal($"{Notificacion.DerivacionTerminada}|{TableroAyudas.Usuario(token)}|{operacion}");
        (await FilasAsync(api, individual.Id, ct)).Should().BeEmpty("P2 = a: la derivación individual no notifica");
    }

    private static async Task RecalcularAsync(ApiDePrueba api, Guid empresa, Guid lote, long version, bool guardar, bool dosVeces, CancellationToken ct)
    {
        await using var scope = OutboxDePrueba.Scope(api, empresa);
        var db = scope.ServiceProvider.GetRequiredService<DiagnosticoDb>();
        var outbox = scope.ServiceProvider.GetRequiredService<IPlatformOutbox>();
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaccion = await db.Database.BeginTransactionAsync(ct);
            var ahora = DateTimeOffset.UtcNow;
            (await RecalculadorEstado.RecalcularAsync(db, outbox, lote, version, ahora, ct)).Should().NotBeNull();
            if (dosVeces)
                await RecalculadorEstado.RecalcularAsync(db, outbox, lote, version, ahora, ct);
            await db.SaveChangesAsync(ct);
            if (guardar)
                await transaccion.CommitAsync(ct);
            else
                await transaccion.RollbackAsync(ct);
        });
    }

    private static async Task<List<string>> FilasAsync(ApiDePrueba api, Guid lote, CancellationToken ct)
    {
        await using var conexion = await EsquemaR8Tests.AbrirAsync(api, ct);
        return await EsquemaR8Tests.ListarAsync(conexion,
            $"SELECT tipo || '|' || usuario_id::text || '|' || referencia_id::text FROM diagnostico.notificaciones WHERE diagnostico_id = '{lote}' ORDER BY creada_en, id", ct);
    }

    private static async Task<List<string>> HistorialAsync(ApiDePrueba api, Guid lote, CancellationToken ct)
    {
        await using var conexion = await EsquemaR8Tests.AbrirAsync(api, ct);
        return await EsquemaR8Tests.ListarAsync(conexion,
            $"SELECT id::text FROM diagnostico.historial_estado_diagnostico WHERE diagnostico_id = '{lote}' AND estado_nuevo IN ('COMPLETADO', 'INCOMPLETO') ORDER BY en, id", ct);
    }

    private static async Task EjecutarAsync(ApiDePrueba api, string sql, CancellationToken ct)
    {
        await using var conexion = await EsquemaR8Tests.AbrirAsync(api, ct);
        await using var comando = new NpgsqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync(ct);
    }
}
