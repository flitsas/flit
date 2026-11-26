using System.Net;
using System.Net.Http;
using System.Text.Json;
using Flit.Impuestos.Api.Modulos.Ejecucion;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Api;
using Flit.Impuestos.Tests.ApiLiquidacion;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flit.Impuestos.Tests.Ejecucion;

/// <summary>
/// HU #14694 (M09; F2a-CF2, F2a-CF7, F1a-CF12, F3-CF4, F3-CF11, DA-5) — el consumidor de <c>impuestos.liquidacion.solicitada</c>:
/// RUNT por <c>svc-impuestos</c> con la empresa del sobre; el ítem queda <c>ENVIANDO</c> con el hash de su token guardado ANTES del
/// POST; 202 ⇒ <c>ENVIADO</c> y libera el crédito; timeout o 5xx ⇒ <c>INCIERTO</c> sin reenvío; no salió ⇒ reintento (token nuevo,
/// sin contar como intento); 401 ⇒ <c>ERROR</c> y aprovisionamiento <c>REQUIERE_CREDENCIALES</c>; RUNT transitorio y
/// <c>rate_limited</c> se reintentan con 30 s y 2 min hasta 3 intentos; datos incompletos o departamento no habilitado no llaman al
/// API. Logs sin placa, documento, nombre ni llave.
/// </summary>
/// <example>
/// <code>
/// await EntornoEjecucion.DespacharAsync(api, empresa, ct);
/// await EntornoEjecucion.ProcesarColaAsync(api, ct);
/// </code>
/// </example>
public sealed class ConsumidorLiquidacionTests
{
    private const string Placa = "LHQ479";

    [Fact]
    public async Task Registra_ENVIANDO_con_el_hash_del_token_antes_del_POST_y_con_202_queda_ENVIADO()
    {
        var ct = TestContext.Current.CancellationToken;
        var empresa = Guid.NewGuid();
        ApiDePrueba? referencia = null;
        LiquidacionItem? alEnviar = null;
        var proveedor = new ApiLiquidacionDePrueba
        {
            Responder = r =>
            {
                using (var db = referencia!.Contexto(empresa))
                    alEnviar = db.Set<LiquidacionItem>().AsNoTracking().Single();
                return ApiLiquidacionDePrueba.Json(HttpStatusCode.Accepted, EntornoEjecucion.Aceptada("consulta-1"));
            },
        };
        var runt = new RuntDePrueba();
        var reloj = new RelojDePrueba();
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, runt, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        referencia = api;
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        runt.Llamadas.Should().ContainSingle().Which.Should().Be((empresa, Placa), "RUNT por svc-impuestos con la empresa del sobre (F2a-CF2)");
        alEnviar!.Estado.Should().Be(EstadoItem.Enviando, "el registro previo se confirma antes del POST (F2a-CF7)");
        alEnviar.TokenHash.Should().NotBeNull();
        alEnviar.ConCallback.Should().BeTrue();
        alEnviar.EnviadoEn.Should().NotBeNull();

        var pedido = proveedor.Pedidos.Single(p => p.Metodo == "POST");
        pedido.Cabeceras["X-API-Key"].Should().Be(ClienteApiLiquidacionTests.ApiKey);
        using var cuerpo = JsonDocument.Parse(pedido.Cuerpo);
        var callback = cuerpo.RootElement.GetProperty("callbackUrl").GetString()!;
        callback.Should().StartWith(EntornoEjecucion.UrlAvisos + "/");
        GeneradorTokenAviso.Hash(callback[(EntornoEjecucion.UrlAvisos.Length + 1)..]).Should().Equal(alEnviar.TokenHash, "en la base solo queda el hash del token");
        cuerpo.RootElement.GetProperty("plateNumber").GetString().Should().Be(Placa);

        var item = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        item.Estado.Should().Be(EstadoItem.Enviado);
        item.ConsultaApiId.Should().Be("consulta-1");
        item.ProximoPollEn.Should().Be(item.EnviadoEn!.Value + TimeSpan.FromMinutes(2), "con webhook, primer GET de respaldo a los 2 min");
        item.DepartamentoCodigo.Should().Be("05");
        item.SecretariaCiudad.Should().Be("MEDELLIN");
        item.SecretariaNombre.Should().Be(EntornoEjecucion.OrganismoMedellin, "el organismo del RUNT, el mismo que va como secretaryName");
        item.DocumentoTipo.Should().Be("CC");
        using (var scope = api.App.Services.CreateScope())
        {
            var proteccion = scope.ServiceProvider.GetRequiredService<IProteccionImpuestos>();
            proteccion.DesprotegerPii(item.DocumentoNumeroCifrado!).Should().Be("1000000001");
            proteccion.DesprotegerPii(item.PropietarioNombreCifrado!).Should().Be("PROPIETARIO PRUEBA");
        }

        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).CPendientes.Should().Be(1, "ENVIADO sigue pendiente en los contadores");

        var cliente = api.Cliente(api.Token(empresa: empresa, permisos: [Permisos.LiquidacionesVer]));
        using var items2 = JsonDocument.Parse(await cliente.GetStringAsync($"{Flit.Impuestos.Api.Program.Prefijo}/liquidaciones/{lote}/items", ct));
        var dto = items2.RootElement.GetProperty("items")[0];
        dto.GetProperty("secretaria").GetProperty("ciudad").GetString().Should().Be("MEDELLIN", "delta 5: secretaría según el RUNT");
        dto.GetProperty("secretaria").GetProperty("nombre").GetString().Should().Be(EntornoEjecucion.OrganismoMedellin, "secretaria_nombre (1.2.0)");
        dto.GetProperty("propietario").GetProperty("documentoNumero").GetString().Should().Be("1000000001");
    }

    [Fact]
    public async Task Con_el_lote_cerrado_no_llama_al_proveedor_y_el_item_queda_SIN_RESULTADO_LOTE_CERRADO()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoEjecucion.ProveedorQueAcepta();
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);
        // El cierre confirmó la cabecera mientras el mensaje del ítem (ya en vuelo) esperaba en la cola.
        await using (var db = api.Contexto(empresa))
        {
            await db.Set<Liquidacion>().Where(l => l.Id == lote).ExecuteUpdateAsync(s => s.SetProperty(l => l.Estado, EstadoLote.CerradoManualmente), ct);
        }

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        EntornoEjecucion.Envios(proveedor).Should().Be(0, "un ítem de un lote cerrado no gasta una consulta del proveedor");
        var item = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        item.Estado.Should().Be(EstadoItem.SinResultadoLoteCerrado, "el estado terminal que el cierre usa para los pendientes");
        item.ProximoIntentoEn.Should().BeNull();
        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).Estado.Should().Be(EstadoLote.CerradoManualmente);
    }

    [Fact]
    public async Task Con_el_lote_abierto_el_envio_sigue_igual()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoEjecucion.ProveedorQueAcepta();
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa, "ABC123"], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        EntornoEjecucion.Envios(proveedor).Should().Be(2, "con el lote EN_PROCESO cada ítem se envía una vez");
        foreach (var id in items)
            (await EntornoEjecucion.ItemAsync(api, empresa, id, ct)).Estado.Should().Be(EstadoItem.Enviado);
        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).Estado.Should().Be(EstadoLote.EnProceso);
    }

    [Fact]
    public async Task El_registro_previo_espera_el_bloqueo_del_cierre_y_si_el_lote_cerro_no_envia()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoEjecucion.ProveedorQueAcepta();
        var runt = new RuntDePrueba();
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, runt);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        // Un cierre en curso: tiene la cabecera bloqueada (como CerrarLiquidacion) y todavía no confirmó.
        await using var cierre = api.Contexto(empresa);
        await using var tx = await cierre.Database.BeginTransactionAsync(ct);
        await cierre.Database.ExecuteSqlAsync($"SELECT 1 FROM impuestos.liquidaciones WHERE id = {lote} FOR UPDATE", ct);
        var entrega = Task.Run(() => EntornoEjecucion.ProcesarColaAsync(api, ct), ct);
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (runt.Llamadas.Count == 0 && DateTime.UtcNow < limite && !entrega.IsCompleted)
            await Task.Delay(50, ct);
        await Task.Delay(TimeSpan.FromSeconds(1.5), ct);

        EntornoEjecucion.Envios(proveedor).Should().Be(0, "el envío espera a que el cierre suelte la cabecera");
        await cierre.Set<LiquidacionItem>().Where(i => i.Id == items[0])
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Estado, EstadoItem.SinResultadoLoteCerrado), ct);
        await cierre.Set<Liquidacion>().Where(l => l.Id == lote)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Estado, EstadoLote.CerradoManualmente), ct);
        await tx.CommitAsync(ct);
        await entrega;

        EntornoEjecucion.Envios(proveedor).Should().Be(0, "el cierre se serializó antes del envío: no se gasta la consulta");
        (await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct)).Estado.Should().Be(EstadoItem.SinResultadoLoteCerrado);
    }

    [Fact]
    public async Task Sin_webhook_no_manda_callbackUrl_y_el_primer_GET_va_a_los_10_segundos()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoEjecucion.ProveedorQueAcepta();
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct, EstadoAprovisionamiento.ListoSinWebhook, secreto: false);
        var (_, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        JsonElement.Parse(proveedor.Pedidos.Single(p => p.Metodo == "POST").Cuerpo).TryGetProperty("callbackUrl", out _).Should().BeFalse();
        var item = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        item.Estado.Should().Be(EstadoItem.Enviado);
        item.ConCallback.Should().BeFalse();
        item.ProximoPollEn.Should().Be(item.EnviadoEn!.Value + TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Modo_respaldo_de_firma_no_manda_callbackUrl()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoEjecucion.ProveedorQueAcepta();
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct, modoRespaldo: true);
        await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        JsonElement.Parse(proveedor.Pedidos.Single(p => p.Metodo == "POST").Cuerpo).TryGetProperty("callbackUrl", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Timeout_tras_enviar_queda_INCIERTO_sin_reenvio_y_libera_el_credito()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba { Responder = _ => throw new TaskCanceledException("timeout de prueba") };
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa, "ZZZ999", "YYY888"], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);
        await EntornoEjecucion.EntregarAsync(api, EntornoEjecucion.Sobre(empresa, lote, items[0]), ct);

        EntornoEjecucion.Envios(proveedor).Should().Be(3, "una vez por placa: un incierto nunca se reenvía (ADR-0093)");
        foreach (var id in items)
        {
            var item = await EntornoEjecucion.ItemAsync(api, empresa, id, ct);
            item.Estado.Should().Be(EstadoItem.Incierto);
            item.EnviadoEn.Should().NotBeNull("la ventana de correlación del incierto parte de enviado_en");
            item.ConsultaApiId.Should().BeNull("el incierto adopta el id cuando lo correlaciona M10");
            item.TokenHash.Should().NotBeNull("la correlación del incierto busca el token");
        }

        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).CIncierto.Should().Be(3);
    }

    [Fact]
    public async Task Un_5xx_tras_enviar_tambien_es_INCIERTO()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba { Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.BadGateway, "{}") };
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (_, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        (await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct)).Estado.Should().Be(EstadoItem.Incierto);
    }

    [Fact]
    public async Task Si_el_proceso_cayo_con_el_POST_en_el_aire_la_reentrega_lo_deja_INCIERTO_sin_llamar()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoEjecucion.ProveedorQueAcepta();
        var runt = new RuntDePrueba();
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, runt);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct, EstadoItem.Enviando);

        await EntornoEjecucion.EntregarAsync(api, EntornoEjecucion.Sobre(empresa, lote, items[0]), ct);

        (await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct)).Estado.Should().Be(EstadoItem.Incierto);
        EntornoEjecucion.Envios(proveedor).Should().Be(0);
        runt.Llamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task No_salio_deja_ENVIANDO_con_reintento_a_los_30_s_y_lo_reenvia_con_token_nuevo()
    {
        var ct = TestContext.Current.CancellationToken;
        var intentos = 0;
        var proveedor = new ApiLiquidacionDePrueba
        {
            Responder = _ => Interlocked.Increment(ref intentos) == 1
                ? throw new HttpRequestException(HttpRequestError.NameResolutionError, "dns de prueba")
                : ApiLiquidacionDePrueba.Json(HttpStatusCode.Accepted, EntornoEjecucion.Aceptada("consulta-2")),
        };
        var reloj = new RelojDePrueba();
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, new RuntDePrueba(), reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (_, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        var enEspera = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        enEspera.Estado.Should().Be(EstadoItem.Enviando, "no salió: no cuenta como intento enviado");
        enEspera.ProximoIntentoEn.Should().Be(reloj.Ahora + TimeSpan.FromSeconds(30));
        enEspera.IntentosReenvio.Should().Be(0);

        (await EntornoEjecucion.ReconciliarAsync(api, ct)).Republicados.Should().Be(0, "la espera no ha vencido");
        reloj.Avanzar(TimeSpan.FromSeconds(31));
        (await EntornoEjecucion.ReconciliarAsync(api, ct)).Republicados.Should().Be(1);
        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        var item = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        item.Estado.Should().Be(EstadoItem.Enviado);
        item.ConsultaApiId.Should().Be("consulta-2");
        item.TokenHash.Should().NotEqual(enEspera.TokenHash, "el token no se guarda: el reenvío lleva uno nuevo y el anterior nunca salió");
        EntornoEjecucion.Envios(proveedor).Should().Be(2);
    }

    [Fact]
    public async Task Un_401_deja_ERROR_y_el_aprovisionamiento_en_REQUIERE_CREDENCIALES()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba { Responder = _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"unauthorized\",\"message\":\"x\"}}") };
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var version = (await EntornoEjecucion.AprovisionamientoAsync(api, empresa, ct)).Version;
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        var item = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        item.Estado.Should().Be(EstadoItem.Error);
        item.ErrorCodigo.Should().Be(TransicionesItem.CodigoCredencialRechazada);
        item.ErrorClase.Should().Be(ClaseError.Depende);
        var aprovisionamiento = await EntornoEjecucion.AprovisionamientoAsync(api, empresa, ct);
        aprovisionamiento.Estado.Should().Be(EstadoAprovisionamiento.RequiereCredenciales, "F1a-CF12");
        aprovisionamiento.MotivoCodigo.Should().Be("CREDENCIAL_RECHAZADA");
        aprovisionamiento.Version.Should().Be(version + 1, "el reportador lo lleva a la plataforma por la versión");
        await using var db = api.Contexto(empresa);
        (await db.Set<HistorialAprovisionamiento>().AsNoTracking().SingleAsync(ct)).Hacia.Should().Be(EstadoAprovisionamiento.RequiereCredenciales);
        (await EntornoEjecucion.LoteAsync(api, empresa, lote, ct)).CError.Should().Be(1);
    }

    [Fact]
    public async Task Con_422_webhook_not_configured_reenvia_el_item_sin_callbackUrl_y_avisa_en_el_log()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba
        {
            Responder = r => r.Content!.ReadAsStringAsync().GetAwaiter().GetResult().Contains("callbackUrl", StringComparison.Ordinal)
                ? ApiLiquidacionDePrueba.Json(HttpStatusCode.UnprocessableEntity, "{\"error\":{\"code\":\"webhook_not_configured\",\"message\":\"x\"}}")
                : ApiLiquidacionDePrueba.Json(HttpStatusCode.Accepted, EntornoEjecucion.Aceptada("consulta-3")),
        };
        var logs = new RegistroDeLogs();
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, new RuntDePrueba(), logs: logs);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var version = (await EntornoEjecucion.AprovisionamientoAsync(api, empresa, ct)).Version;
        var (_, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        EntornoEjecucion.Envios(proveedor).Should().Be(2, "el primero con callbackUrl, el segundo sin él");
        var item = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        item.Estado.Should().Be(EstadoItem.Enviado, "no es un error de datos");
        item.ConsultaApiId.Should().Be("consulta-3");
        item.ConCallback.Should().BeFalse();
        item.TokenHash.Should().BeNull("sin callbackUrl no hay token que correlacionar");
        item.ProximoPollEn.Should().Be(item.EnviadoEn!.Value + TimeSpan.FromSeconds(10), "se concilia como LISTO_SIN_WEBHOOK");
        (await EntornoEjecucion.AprovisionamientoAsync(api, empresa, ct)).Version.Should().Be(version, "el aprovisionamiento no se toca (M07)");
        logs.Todo.Should().Contain("webhook_not_configured");
        logs.Todo.Should().NotContain(ClienteApiLiquidacionTests.ApiKey).And.NotContain(ClienteApiLiquidacionTests.WebhookSecret);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "invalid_params", ClaseError.CorregirDatos)]
    [InlineData(HttpStatusCode.Conflict, "source_inactive", ClaseError.Reintentable)]
    [InlineData(HttpStatusCode.BadRequest, "bad_request", ClaseError.Depende)]
    public async Task Un_rechazo_del_API_queda_ERROR_con_su_codigo_y_su_clase(HttpStatusCode status, string codigo, ClaseError clase)
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = new ApiLiquidacionDePrueba { Responder = _ => ApiLiquidacionDePrueba.Json(status, "{\"error\":{\"code\":\"" + codigo + "\",\"message\":\"x\"}}") };
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, new RuntDePrueba());
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (_, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        var item = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        item.Estado.Should().Be(EstadoItem.Error);
        item.ErrorCodigo.Should().Be(codigo);
        item.ErrorClase.Should().Be(clase);
    }

    [Fact]
    public async Task RUNT_transitorio_se_reintenta_a_los_30_s_y_2_min_y_al_tercero_queda_ERROR_reintentable()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoEjecucion.ProveedorQueAcepta();
        var runt = new RuntDePrueba { Responder = _ => new ConsultaRunt.Transitorio("caída de prueba") };
        var reloj = new RelojDePrueba();
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, runt, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (_, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);
        var primero = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        primero.Estado.Should().Be(EstadoItem.ReintentoProgramado);
        primero.IntentosRunt.Should().Be(1);
        primero.ProximoIntentoEn.Should().Be(reloj.Ahora + TimeSpan.FromSeconds(30));

        reloj.Avanzar(TimeSpan.FromSeconds(31));
        (await EntornoEjecucion.ReconciliarAsync(api, ct)).Reprogramados.Should().Be(1);
        await EntornoEjecucion.ProcesarColaAsync(api, ct);
        var segundo = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        segundo.Estado.Should().Be(EstadoItem.ReintentoProgramado);
        segundo.IntentosRunt.Should().Be(2);
        segundo.ProximoIntentoEn.Should().Be(reloj.Ahora + TimeSpan.FromMinutes(2));

        reloj.Avanzar(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1));
        await EntornoEjecucion.ReconciliarAsync(api, ct);
        await EntornoEjecucion.ProcesarColaAsync(api, ct);
        var tercero = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        tercero.Estado.Should().Be(EstadoItem.Error);
        tercero.ErrorCodigo.Should().Be(TransicionesItem.CodigoRuntNoDisponible);
        tercero.ErrorClase.Should().Be(ClaseError.Reintentable);
        runt.Llamadas.Should().HaveCount(3);
        EntornoEjecucion.Envios(proveedor).Should().Be(0);
    }

    [Fact]
    public async Task Rate_limited_se_reenvia_como_consulta_nueva_hasta_3_veces_y_luego_queda_ERROR()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoEjecucion.ProveedorQueAcepta();
        var reloj = new RelojDePrueba();
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, new RuntDePrueba(), reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (_, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);
        await EntornoEjecucion.ProcesarColaAsync(api, ct);
        var opciones = api.App.Services.GetRequiredService<IOptions<OpcionesEjecucion>>().Value;

        var hashes = new List<byte[]>();
        var esperas = new List<TimeSpan?>();
        for (var vuelta = 0; vuelta < 3; vuelta++)
        {
            await using (var db = api.Contexto(empresa))
            {
                var item = await db.Set<LiquidacionItem>().SingleAsync(i => i.Id == items[0], ct);
                item.Estado.Should().Be(EstadoItem.Enviado);
                hashes.Add(item.TokenHash!);
                var r = TransicionesItem.Aplicar(new SituacionItem(item.Estado, item.IntentosRunt, item.IntentosReenvio), new EventoItem.Final("failed", "rate_limited"));
                ConsumidorLiquidacion.AplicarReintento(item, r, reloj.Ahora, opciones);
                esperas.Add(item.ProximoIntentoEn - reloj.Ahora);
                await db.SaveChangesAsync(ct);
            }

            reloj.Avanzar(TimeSpan.FromMinutes(3));
            await EntornoEjecucion.ReconciliarAsync(api, ct);
            await EntornoEjecucion.ProcesarColaAsync(api, ct);
        }

        esperas.Should().Equal(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), null);
        hashes.Should().HaveCount(3);
        hashes.Select(Convert.ToHexString).Should().OnlyHaveUniqueItems("cada reenvío es una consulta nueva con token nuevo");
        var final = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        final.Estado.Should().Be(EstadoItem.Error);
        final.ErrorCodigo.Should().Be("rate_limited");
        final.ErrorClase.Should().Be(ClaseError.Reintentable);
        final.IntentosReenvio.Should().Be(3);
        EntornoEjecucion.Envios(proveedor).Should().Be(3);
    }

    [Fact]
    public async Task Datos_incompletos_departamento_no_habilitado_o_placa_sin_RUNT_no_llaman_al_API()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoEjecucion.ProveedorQueAcepta();
        var runt = new RuntDePrueba
        {
            Responder = placa => placa switch
            {
                "CUN111" => new ConsultaRunt.Encontrado(RuntDePrueba.Cundinamarca(conCorreo: false)),
                "HUI222" => new ConsultaRunt.Encontrado(RuntDePrueba.Antioquia() with { OrganismoNombre = "STRIA TTO NEIVA" }),
                _ => new ConsultaRunt.NoEncontrado(),
            },
        };
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, runt);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        await using (var db = api.Contexto(null))
        {
            db.Add(new Organismo { Codigo = "41001000", Nombre = "STRIA TTO NEIVA", NombreNormalizado = "STRIA TTO NEIVA", DepartamentoCodigo = "41", Municipio = "NEIVA", SincronizadoEn = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
        }

        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, ["CUN111", "HUI222", "NOE333"], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        var cun = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        cun.Estado.Should().Be(EstadoItem.DatosIncompletos);
        cun.CampoFaltante.Should().Be("OwnerEmail", "DA-9a=d: sin contacto de respaldo");
        var hui = await EntornoEjecucion.ItemAsync(api, empresa, items[1], ct);
        hui.Estado.Should().Be(EstadoItem.DepartamentoNoHabilitado);
        hui.DepartamentoCodigo.Should().Be("41");
        var noe = await EntornoEjecucion.ItemAsync(api, empresa, items[2], ct);
        noe.Estado.Should().Be(EstadoItem.Error);
        noe.ErrorCodigo.Should().Be(TransicionesItem.CodigoRuntPlacaNoEncontrada);
        EntornoEjecucion.Envios(proveedor).Should().Be(0);

        var cabecera = await EntornoEjecucion.LoteAsync(api, empresa, lote, ct);
        cabecera.CDatosIncompletos.Should().Be(1);
        cabecera.CDptoNoHabilitado.Should().Be(1);
        cabecera.CError.Should().Be(1);
        cabecera.CPendientes.Should().Be(0);
    }

    [Fact]
    public async Task Un_sobre_de_otra_empresa_no_toca_el_item()
    {
        var ct = TestContext.Current.CancellationToken;
        var proveedor = EntornoEjecucion.ProveedorQueAcepta();
        var runt = new RuntDePrueba();
        await using var api = await EntornoEjecucion.ConApiAsync(proveedor, runt);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (lote, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct, EstadoItem.ConsultandoRunt);

        await EntornoEjecucion.EntregarAsync(api, EntornoEjecucion.Sobre(Guid.NewGuid(), lote, items[0]), ct);

        (await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct)).Estado.Should().Be(EstadoItem.ConsultandoRunt);
        runt.Llamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task El_log_no_lleva_placa_documento_nombre_ni_la_api_key()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new RegistroDeLogs();
        await using var api = await EntornoEjecucion.ConApiAsync(EntornoEjecucion.ProveedorQueAcepta(), new RuntDePrueba(), logs: logs);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        logs.Todo.Should().Contain("consulta", "el consumidor sí deja rastro");
        foreach (var prohibido in new[] { Placa, "1000000001", "PROPIETARIO PRUEBA", ClienteApiLiquidacionTests.ApiKey, ClienteApiLiquidacionTests.WebhookSecret, "pedro@correo.example" })
            logs.Todo.Should().NotContain(prohibido);
    }

    [Fact]
    public void El_freno_global_pausa_tras_5_transitorios_seguidos_y_un_exito_lo_reinicia()
    {
        var reloj = new RelojDePrueba();
        var freno = new FrenoGlobal(Options.Create(new OpcionesEjecucion()), reloj);

        for (var i = 0; i < 4; i++)
            freno.RegistrarTransitorio();
        freno.EnPausa.Should().BeFalse();
        freno.RegistrarExito();
        for (var i = 0; i < 4; i++)
            freno.RegistrarTransitorio();
        freno.EnPausa.Should().BeFalse("el éxito reinició la cuenta");
        freno.RegistrarTransitorio();
        freno.EnPausa.Should().BeTrue();
        freno.PausaHasta.Should().Be(reloj.Ahora + TimeSpan.FromSeconds(60));

        reloj.Avanzar(TimeSpan.FromSeconds(61));
        freno.EnPausa.Should().BeFalse();
    }

    [Fact]
    public async Task Con_el_freno_en_pausa_no_consulta_el_RUNT_y_lo_reprograma_al_fin_de_la_pausa()
    {
        var ct = TestContext.Current.CancellationToken;
        var runt = new RuntDePrueba();
        var reloj = new RelojDePrueba();
        await using var api = await EntornoEjecucion.ConApiAsync(EntornoEjecucion.ProveedorQueAcepta(), runt, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = Guid.NewGuid();
        await EntornoEjecucion.SembrarEmpresaAsync(api, empresa, ct);
        var (_, items) = await EntornoEjecucion.SembrarLoteAsync(api, empresa, [Placa], ct);
        var freno = api.App.Services.GetRequiredService<FrenoGlobal>();
        for (var i = 0; i < 5; i++)
            freno.RegistrarTransitorio();
        await EntornoEjecucion.DespacharAsync(api, empresa, ct);

        await EntornoEjecucion.ProcesarColaAsync(api, ct);

        runt.Llamadas.Should().BeEmpty();
        var item = await EntornoEjecucion.ItemAsync(api, empresa, items[0], ct);
        item.Estado.Should().Be(EstadoItem.ConsultandoRunt);
        item.ProximoIntentoEn.Should().Be(freno.PausaHasta);
    }
}
