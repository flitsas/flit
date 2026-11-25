using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Flit.Impuestos.Api;
using Flit.Impuestos.Api.Infraestructura.Archivos;
using Flit.Impuestos.Api.Modulos.Recibos;
using Flit.Impuestos.Api.Modulos.Resultados;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Ejecucion;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using M = Flit.Impuestos.Tests.Liquidaciones.EntornoMasiva;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Recibos;

/// <summary>
/// HU #14694 (M11, plan §1.6 y §2.4; DA-9c; guía de integración §8) — descarga de los recibos PDF de un ítem con resultado:
/// un archivo por <c>fileId</c> distinto (el mismo <c>fileId</c> en varias vigencias es un solo recibo con todas sus vigencias),
/// <c>consolidatedFileId</c> y <c>clearance.fileId</c> como CONSOLIDADO y PAZ_Y_SALVO, una vigencia con <c>fileId</c> null no
/// tiene recibo. <c>404 file_missing</c> o <c>not_found</c> ⇒ <c>SIN_ARCHIVO</c> con su motivo; <c>503 storage_unavailable</c> ⇒
/// reintentos (1, 5 y 30 min, cola <c>impuestos.recibos</c>) y al cuarto intento <c>PENDIENTE_REINTENTO_MANUAL</c>. Lo guardado no se
/// vuelve a pedir. El <c>X-API-Key</c> es el de la compañía dueña y nunca llega al log. Sin file-manager ni bandera de pruebas, el
/// servicio no arranca.
/// </summary>
/// <example>
/// <code>
/// await using var scope = api.App.Services.CreateAsyncScope();
/// var r = await scope.ServiceProvider.GetRequiredService&lt;DescargaRecibos&gt;().DescargarAsync(empresa, item, ct);
/// </code>
/// </example>
public sealed class DescargaRecibosTests
{
    [Fact]
    public async Task Un_fileId_repetido_entre_vigencias_se_descarga_una_vez_y_queda_un_recibo_con_sus_vigencias()
    {
        var ct = TestContext.Current.CancellationToken;
        var archivos = new ArchivosDelProveedor();
        var proveedor = archivos.Proveedor();
        await using var api = await M.ConApiAsync(proveedor);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item) = await ItemConResultadoAsync(api, Consulta([("2024", "f1"), ("2025", "f1")], consolidado: "fc"), ct);

        var r = await DescargarAsync(api, empresa, item, ct);

        r.Guardados.Should().Be(2);
        archivos.Pedidos.Should().BeEquivalentTo(["f1", "fc"], "un GET por fileId distinto");
        proveedor.Pedidos.Should().OnlyContain(p => p.Cabeceras["X-API-Key"] == ClienteApiLiquidacionTests.ApiKey, "la api_key de la compañía dueña");
        proveedor.Pedidos.Select(p => p.Uri).Should().Contain(ClienteApiLiquidacionTests.Base + "/api/v1/files/f1/download");
        var recibos = await RecibosAsync(api, empresa, item, ct);
        recibos.Should().HaveCount(2);
        var vigencia = recibos.Single(x => x.FileIdApi == "f1");
        vigencia.Tipo.Should().Be(TipoRecibo.Vigencia);
        vigencia.Periodos.Should().Equal("2024", "2025");
        vigencia.Estado.Should().Be(EstadoRecibo.Guardado);
        vigencia.Motivo.Should().BeNull();
        vigencia.Intentos.Should().Be(1);
        vigencia.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
        vigencia.Tamano.Should().Be(ArchivosDelProveedor.Pdf("f1").Length);
        vigencia.ReferenciaAlmacen.Should().NotBeNullOrEmpty();
        recibos.Single(x => x.FileIdApi == "fc").Tipo.Should().Be(TipoRecibo.Consolidado);
        await using var scope = api.App.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IAlmacenArchivos>().LeerAsync(vigencia.ReferenciaAlmacen!, ct))
            .Should().Equal(ArchivosDelProveedor.Pdf("f1"), "el PDF queda en el almacén de la suite");
    }

    [Fact]
    public async Task Volver_a_descargar_no_pide_otra_vez_lo_guardado()
    {
        var ct = TestContext.Current.CancellationToken;
        var archivos = new ArchivosDelProveedor();
        await using var api = await M.ConApiAsync(archivos.Proveedor());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item) = await ItemConResultadoAsync(api, Consulta([("2025", "f1")]), ct);

        await DescargarAsync(api, empresa, item, ct);
        var segunda = await DescargarAsync(api, empresa, item, ct);

        segunda.Guardados.Should().Be(0);
        archivos.Pedidos.Should().Equal("f1");
        (await RecibosAsync(api, empresa, item, ct)).Should().ContainSingle().Which.Intentos.Should().Be(1);
    }

    [Fact]
    public async Task Paz_y_salvo_se_guarda_como_PAZ_Y_SALVO_y_una_vigencia_sin_fileId_no_tiene_recibo()
    {
        var ct = TestContext.Current.CancellationToken;
        var archivos = new ArchivosDelProveedor();
        await using var api = await M.ConApiAsync(archivos.Proveedor());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item) = await ItemConResultadoAsync(api, Consulta([("2023", null), ("2025", "f1")], pazYSalvo: "fp"), ct);

        await DescargarAsync(api, empresa, item, ct);

        var recibos = await RecibosAsync(api, empresa, item, ct);
        recibos.Select(x => (x.FileIdApi, x.Tipo)).Should().BeEquivalentTo([("f1", TipoRecibo.Vigencia), ("fp", TipoRecibo.PazYSalvo)]);
        recibos.Single(x => x.FileIdApi == "f1").Periodos.Should().Equal("2025");
        archivos.Pedidos.Should().BeEquivalentTo(["f1", "fp"]);
    }

    [Fact]
    public async Task File_missing_y_not_found_quedan_SIN_ARCHIVO_con_su_motivo_y_no_se_reintentan()
    {
        var ct = TestContext.Current.CancellationToken;
        var archivos = new ArchivosDelProveedor();
        archivos.Responder("f1", () => Error(HttpStatusCode.NotFound, "file_missing"));
        archivos.Responder("f2", () => Error(HttpStatusCode.NotFound, "not_found"));
        await using var api = await M.ConApiAsync(archivos.Proveedor());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item) = await ItemConResultadoAsync(api, Consulta([("2024", "f1"), ("2025", "f2")]), ct);

        var r = await DescargarAsync(api, empresa, item, ct);
        await DescargarAsync(api, empresa, item, ct);

        r.SinArchivo.Should().Be(2);
        r.PorReintentar.Should().Be(0);
        var recibos = await RecibosAsync(api, empresa, item, ct);
        recibos.Should().OnlyContain(x => x.Estado == EstadoRecibo.SinArchivo && x.ReferenciaAlmacen == null);
        recibos.Single(x => x.FileIdApi == "f1").Motivo.Should().Be("FILE_MISSING");
        recibos.Single(x => x.FileIdApi == "f2").Motivo.Should().Be("NOT_FOUND");
        archivos.Pedidos.Should().HaveCount(2, "un SIN_ARCHIVO no se vuelve a pedir");
    }

    [Fact]
    public async Task Un_503_se_reintenta_tres_veces_y_al_cuarto_intento_queda_PENDIENTE_REINTENTO_MANUAL()
    {
        var ct = TestContext.Current.CancellationToken;
        var archivos = new ArchivosDelProveedor { PorDefecto = () => Error(HttpStatusCode.ServiceUnavailable, "storage_unavailable") };
        await using var api = await M.ConApiAsync(archivos.Proveedor());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item) = await ItemConResultadoAsync(api, Consulta([("2025", "f1")]), ct);

        for (var intento = 1; intento <= 3; intento++)
        {
            var r = await DescargarAsync(api, empresa, item, ct);
            r.PorReintentar.Should().Be(1, $"intento {intento}");
            var fila = (await RecibosAsync(api, empresa, item, ct)).Single();
            fila.Estado.Should().Be(EstadoRecibo.Pendiente);
            fila.Motivo.Should().Be("STORAGE_UNAVAILABLE");
            fila.Intentos.Should().Be(intento);
        }

        var ultima = await DescargarAsync(api, empresa, item, ct);
        await DescargarAsync(api, empresa, item, ct);

        ultima.PorReintentar.Should().Be(0);
        ultima.Manuales.Should().Be(1);
        var final = (await RecibosAsync(api, empresa, item, ct)).Single();
        final.Estado.Should().Be(EstadoRecibo.PendienteReintentoManual);
        final.Motivo.Should().Be("STORAGE_UNAVAILABLE");
        final.Intentos.Should().Be(DescargaRecibos.IntentosMaximos);
        archivos.Pedidos.Should().HaveCount(DescargaRecibos.IntentosMaximos, "el manual no se pide solo");
        DescargaRecibos.Esperas.Should().Equal(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task Un_503_que_despues_responde_200_queda_GUARDADO_sin_motivo()
    {
        var ct = TestContext.Current.CancellationToken;
        var archivos = new ArchivosDelProveedor();
        archivos.Responder("f1", () => Error(HttpStatusCode.ServiceUnavailable, "storage_unavailable"));
        await using var api = await M.ConApiAsync(archivos.Proveedor());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item) = await ItemConResultadoAsync(api, Consulta([("2025", "f1")]), ct);

        (await DescargarAsync(api, empresa, item, ct)).PorReintentar.Should().Be(1);
        (await DescargarAsync(api, empresa, item, ct)).Guardados.Should().Be(1);

        var fila = (await RecibosAsync(api, empresa, item, ct)).Single();
        fila.Estado.Should().Be(EstadoRecibo.Guardado);
        fila.Motivo.Should().BeNull();
        fila.Intentos.Should().Be(2);
    }

    [Fact]
    public async Task Sin_credencial_no_llama_al_proveedor_y_lo_informa()
    {
        var ct = TestContext.Current.CancellationToken;
        var archivos = new ArchivosDelProveedor();
        await using var api = await M.ConApiAsync(archivos.Proveedor());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item) = await ItemConResultadoAsync(api, Consulta([("2025", "f1")]), ct);
        await using (var db = api.Contexto(empresa))
            await db.Set<CredencialEmpresa>().ExecuteDeleteAsync(ct);

        var r = await DescargarAsync(api, empresa, item, ct);

        r.SinCredencial.Should().BeTrue();
        archivos.Pedidos.Should().BeEmpty();
        (await RecibosAsync(api, empresa, item, ct)).Should().ContainSingle().Which.Estado.Should().Be(EstadoRecibo.Pendiente);
    }

    [Fact]
    public async Task El_resultado_aplicado_deja_el_evento_y_el_consumidor_guarda_los_recibos()
    {
        var ct = TestContext.Current.CancellationToken;
        var archivos = new ArchivosDelProveedor();
        await using var api = await M.ConApiAsync(archivos.Proveedor());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item) = await ItemConResultadoAsync(api, Consulta([("2025", "f1")]), ct);
        var sobre = await EventoAsync(api, item, ct);

        sobre.Type.Should().Be(RecibosDescargar.Tipo);
        sobre.DataAs<RecibosDescargar>().ItemId.Should().Be(item);
        (await EntregarAsync(api, sobre, ct)).Should().BeTrue();
        (await EntregarAsync(api, sobre, ct)).Should().BeFalse("la bandeja descarta la re-entrega del mismo eventId");

        (await RecibosAsync(api, empresa, item, ct)).Should().ContainSingle().Which.Estado.Should().Be(EstadoRecibo.Guardado);
        archivos.Pedidos.Should().Equal("f1");
    }

    [Fact]
    public async Task El_consumidor_lanza_para_que_el_SDK_reintente_mientras_hay_503_y_confirma_al_pasar_a_manual()
    {
        var ct = TestContext.Current.CancellationToken;
        var archivos = new ArchivosDelProveedor { PorDefecto = () => Error(HttpStatusCode.ServiceUnavailable, "storage_unavailable") };
        await using var api = await M.ConApiAsync(archivos.Proveedor());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item) = await ItemConResultadoAsync(api, Consulta([("2025", "f1")]), ct);
        var sobre = await EventoAsync(api, item, ct);

        for (var intento = 1; intento < DescargaRecibos.IntentosMaximos; intento++)
        {
            var entrega = () => EntregarAsync(api, sobre, ct);
            await entrega.Should().ThrowAsync<RecibosPorReintentarException>($"intento {intento}: el SDK lo pasa a impuestos.recibos.retry.{intento}");
        }

        (await EntregarAsync(api, sobre, ct)).Should().BeTrue("al cuarto intento queda manual y el mensaje se confirma");
        (await RecibosAsync(api, empresa, item, ct)).Single().Estado.Should().Be(EstadoRecibo.PendienteReintentoManual);
    }

    [Fact]
    public async Task El_consumidor_de_otra_empresa_no_ve_el_item()
    {
        var ct = TestContext.Current.CancellationToken;
        var archivos = new ArchivosDelProveedor();
        await using var api = await M.ConApiAsync(archivos.Proveedor());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item) = await ItemConResultadoAsync(api, Consulta([("2025", "f1")]), ct);
        var otra = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, otra, ct);
        var sobre = await EventoAsync(api, item, ct) with { TenantId = otra };

        (await EntregarAsync(api, sobre, ct)).Should().BeTrue();

        archivos.Pedidos.Should().BeEmpty();
        (await RecibosAsync(api, empresa, item, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task La_cola_impuestos_recibos_queda_enlazada_con_esperas_1_5_30_min_y_solo_del_servicio()
    {
        var o = new PlatformConsumerOptions();

        ConsumidorRecibos.ConfigurarCola(o);

        RecibosDescargar.Tipo.Should().Be(AplicadorResultado.TipoRecibos, "el evento que deja M10");
        RecibosDescargar.Cola.Should().Be("impuestos.recibos");
        RecibosDescargar.Productor.Should().Be("impuestos");
        o.RetryDelays.Should().Equal(DescargaRecibos.Esperas);
        o.ProductoresPermitidos.Should().Equal("impuestos");
        var ct = TestContext.Current.CancellationToken;
        await using var api = await ApiDePrueba.SinBaseAsync();
        api.App.Services.GetServices<IHostedService>()
            .Should().Contain(s => s.GetType().GetGenericArguments().Contains(typeof(ConsumidorRecibos)), "el consumidor de la cola arranca con el host");
        await Task.CompletedTask.WaitAsync(ct);
    }

    [Fact]
    public async Task Nunca_registra_la_api_key_en_el_log()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new RegistroDeLogs();
        var archivos = new ArchivosDelProveedor();
        archivos.Responder("f2", () => Error(HttpStatusCode.ServiceUnavailable, "storage_unavailable"));
        await using var api = await R.ConApiAsync(archivos.Proveedor(), logs: logs);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var (empresa, item) = await ItemConResultadoAsync(api, Consulta([("2024", "f1"), ("2025", "f2")]), ct);

        await DescargarAsync(api, empresa, item, ct);

        logs.Todo.Should().NotBeEmpty().And.NotContain(ClienteApiLiquidacionTests.ApiKey);
    }

    [Fact]
    public void Sin_file_manager_ni_bandera_de_pruebas_no_arranca_y_nombra_la_variable()
    {
        var config = ApiDePrueba.Configuracion(ApiDePrueba.BaseInalcanzable);
        config.Remove(OpcionesAlmacen.GuardarEnMemoria);

        var act = () => Program.Build([], b =>
        {
            b.WebHost.UseTestServer();
            b.Configuration.AddInMemoryCollection(config);
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*FILE_MANAGER_BASE_URL*");
        new FileManagerOptions().Category.Should().Be("impuestos", "categoría propia del file-manager (ADR-0057, gate G-11)");
    }

    // ── apoyo ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Una consulta <c>succeeded</c> con las vigencias dadas (<c>fileId</c> null = sin recibo) y los archivos del lote.</summary>
    internal static string Consulta(IReadOnlyList<(string Periodo, string? FileId)> periodos, string? consolidado = null, string? pazYSalvo = null)
    {
        var lista = string.Join(",", periodos.Select(p =>
            $$$"""{"period":{{{p.Periodo}}},"tax":100,"totalToPay":100,"otherCharges":[],"dueDate":"2026-11-30","paymentReference":null,"fileId":{{{Texto(p.FileId)}}}}"""));
        var estado = periodos.Count == 0 ? "paz_y_salvo" : "con_deuda";
        var paz = pazYSalvo is null ? "null" : $$$"""{"fileId":"{{{pazYSalvo}}}"}""";
        return $$$"""{"id":"c-1","status":"succeeded","attempt":1,"error_code":null,"error_message":null,"file_errors":[],"callback_url":null,"result":{"status":"{{{estado}}}","plateNumber":"LHQ479","department":{"code":"25","name":"CUNDINAMARCA"},"pendingPeriods":[{{{lista}}}],"totals":{"totalToPay":100},"lastPayment":null,"clearance":{{{paz}}},"consolidatedFileId":{{{Texto(consolidado)}}},"validations":[],"warnings":[],"error":null}}""";
    }

    private static string Texto(string? valor) => valor is null ? "null" : $"\"{valor}\"";

    /// <summary>Compañía lista, un ítem ENVIADO (consulta <c>c-1</c>) y el resultado aplicado como lo hace M10.</summary>
    internal static async Task<(Guid Empresa, Guid Item)> ItemConResultadoAsync(ApiDePrueba api, string consulta, CancellationToken ct)
    {
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["LHQ479"], ct, EstadoItem.Enviado);
        await using (var db = api.Contexto(empresa))
        {
            await db.Set<Liquidacion>().Where(l => l.Id == lote).ExecuteUpdateAsync(s => s.SetProperty(l => l.Estado, EstadoLote.EnProceso), ct);
            await db.Set<LiquidacionItem>().Where(i => i.Id == items[0]).ExecuteUpdateAsync(s => s.SetProperty(i => i.ConsultaApiId, "c-1"), ct);
        }

        (await R.AplicarAsync(api, empresa, items[0], consulta, ct)).Should().Be(Aplicacion.Aplicado);
        return (empresa, items[0]);
    }

    internal static async Task<ResultadoDescarga> DescargarAsync(ApiDePrueba api, Guid empresa, Guid item, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DescargaRecibos>().DescargarAsync(empresa, item, ct);
    }

    internal static async Task<List<Recibo>> RecibosAsync(ApiDePrueba api, Guid empresa, Guid item, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<Recibo>().AsNoTracking().Where(r => r.ItemId == item).OrderBy(r => r.FileIdApi).ToListAsync(ct);
    }

    /// <summary>El sobre de <c>impuestos.recibos.descargar</c> que dejó la aplicación del resultado en la outbox.</summary>
    private static async Task<EventEnvelope> EventoAsync(ApiDePrueba api, Guid item, CancellationToken ct)
    {
        await using var db = api.Contexto(null);
        var mensajes = await db.Set<OutboxMessage>().AsNoTracking().Where(m => m.RoutingKey == AplicadorResultado.TipoRecibos).ToListAsync(ct);
        return mensajes.Select(m => EventEnvelope.FromJson(Encoding.UTF8.GetBytes(m.Payload))).Single(s => s.DataAs<RecibosDescargar>().ItemId == item);
    }

    /// <summary>Entrega como el SDK: transacción, bandeja de la cola, efecto y fila de la bandeja; si el efecto lanza, se revierte.</summary>
    private static async Task<bool> EntregarAsync(ApiDePrueba api, EventEnvelope sobre, CancellationToken ct)
    {
        await using var scope = api.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ImpuestosDb>();
        var consumidor = scope.ServiceProvider.GetRequiredService<ConsumidorRecibos>();
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (await db.Set<InboxMessage>().AnyAsync(m => m.EventId == sobre.EventId && m.Consumer == RecibosDescargar.Cola, ct))
                return false;
            await consumidor.HandleAsync(sobre, sobre.DataAs<RecibosDescargar>(), ct);
            db.Set<InboxMessage>().Add(new InboxMessage { EventId = sobre.EventId, Consumer = RecibosDescargar.Cola, Type = sobre.Type, ProcessedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        });
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string codigo) =>
        ApiLiquidacionDePrueba.Json(status, $$$"""{"error":{"code":"{{{codigo}}}","message":"x"}}""");
}

/// <summary>
/// Doble de <c>GET /api/v1/files/{fileId}/download</c>: por defecto un PDF propio de cada <c>fileId</c>; una respuesta encolada
/// para un <c>fileId</c> se usa una vez. Registra los <c>fileId</c> pedidos en orden.
/// </summary>
internal sealed class ArchivosDelProveedor
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<HttpResponseMessage>>> _respuestas = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _pedidos = new();

    public Func<HttpResponseMessage>? PorDefecto { get; init; }

    public IReadOnlyList<string> Pedidos => [.. _pedidos];

    public static byte[] Pdf(string fileId) => Encoding.ASCII.GetBytes($"%PDF-1.4 recibo de prueba {fileId}\n%%EOF");

    public void Responder(string fileId, Func<HttpResponseMessage> respuesta) =>
        _respuestas.GetOrAdd(fileId, _ => new ConcurrentQueue<Func<HttpResponseMessage>>()).Enqueue(respuesta);

    public ApiLiquidacionDePrueba Proveedor() => new() { Responder = Responder };

    private HttpResponseMessage Responder(HttpRequestMessage pedido)
    {
        var partes = pedido.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (partes is not ["api", "v1", "files", var fileId, "download"])
            return ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, ClienteApiLiquidacionTests.Fuentes200);
        fileId = Uri.UnescapeDataString(fileId);
        _pedidos.Enqueue(fileId);
        if (_respuestas.TryGetValue(fileId, out var cola) && cola.TryDequeue(out var encolada))
            return encolada();
        if (PorDefecto is not null)
            return PorDefecto();
        var contenido = new ByteArrayContent(Pdf(fileId));
        contenido.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        contenido.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = $"recibo-LHQ479-{fileId}.pdf" };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = contenido };
    }
}
