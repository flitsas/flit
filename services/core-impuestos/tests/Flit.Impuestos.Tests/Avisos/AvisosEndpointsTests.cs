using System.Diagnostics;
using System.Net;
using System.Text;
using Flit.Impuestos.Api;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.Resultados;
using FluentAssertions;
using Xunit;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Avisos;

/// <summary>
/// HU #14694 (M10; F2b-CF1, F2b-CF2, F6-CF4, plan §1.5) — el receptor anónimo <c>POST /api/v1/impuestos/avisos/{token}</c>:
/// token desconocido ⇒ 410; token conocido con firma o timestamp inválidos ⇒ 401; válido ⇒ 200 siempre (duplicado, compañía
/// suspendida, <c>consulta.id</c> distinta), sin cuerpo y en menos de 10 s, con la aplicación en segundo plano; cuerpo de más de
/// 256 KB ⇒ 413. No pasa por el guard de suscripción.
/// </summary>
/// <example>
/// <code>
/// using var r = await api.Anonimo().SendAsync(EntornoResultados.PeticionAviso(token, cuerpo, secreto, ahora), ct);
/// </code>
/// </example>
public sealed class AvisosEndpointsTests
{
    [Fact]
    public async Task Token_desconocido_responde_410_sin_cuerpo_y_no_registra_nada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);

        using var desconocido = await api.Anonimo().SendAsync(R.PeticionAviso(R.NuevoToken(), R.Aviso(R.Consulta("c-1")), R.Secreto, DateTimeOffset.UtcNow), ct);
        using var malFormado = await api.Anonimo().SendAsync(R.PeticionAviso("corto", R.Aviso(R.Consulta("c-1")), R.Secreto, DateTimeOffset.UtcNow), ct);

        desconocido.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await desconocido.Content.ReadAsStringAsync(ct)).Should().BeEmpty();
        malFormado.StatusCode.Should().Be(HttpStatusCode.Gone, "un token que no tiene la forma no puede ser de nadie");
        await using var db = api.Contexto(null);
        db.Set<Flit.Impuestos.Api.Persistence.AvisoRecibido>().Should().BeEmpty();
    }

    [Fact]
    public async Task Token_conocido_con_firma_o_timestamp_invalidos_responde_401_y_no_aplica()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (item, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);
        var cuerpo = R.Aviso(R.Consulta("c-1"));

        using var otroSecreto = await api.Anonimo().SendAsync(R.PeticionAviso(token, cuerpo, "whsec_otro-secreto", DateTimeOffset.UtcNow), ct);
        using var viejo = await api.Anonimo().SendAsync(R.PeticionAviso(token, cuerpo, R.Secreto, DateTimeOffset.UtcNow.AddSeconds(-301)), ct);

        otroSecreto.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        viejo.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "fuera de ±300 s");
        (await otroSecreto.Content.ReadAsStringAsync(ct)).Should().BeEmpty();
        (await R.ItemAsync(api, empresa, item.Id, ct)).Estado.Should().Be(EstadoItem.Enviado);
        (await R.AvisosAsync(api, item.Id, ct)).Should().BeEmpty("un aviso sin firma válida no entra al dedupe");
    }

    [Fact]
    public async Task Un_aviso_valido_responde_200_sin_cuerpo_en_menos_de_10_segundos_y_se_aplica_en_segundo_plano()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (item, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);

        var reloj = Stopwatch.StartNew();
        using var r = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-1"), "d-1"), R.Secreto, DateTimeOffset.UtcNow, "d-1"), ct);
        reloj.Stop();

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync(ct)).Should().BeEmpty();
        reloj.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        (await R.EsperarAsync(async () => (await R.ItemAsync(api, empresa, item.Id, ct)).Estado == EstadoItem.Exitoso, TimeSpan.FromSeconds(10), ct))
            .Should().BeTrue("la cola aplica el resultado después de responder");
        var aviso = (await R.AvisosAsync(api, item.Id, ct)).Should().ContainSingle().Subject;
        aviso.DeliveryId.Should().Be("d-1");
        aviso.TenantId.Should().Be(empresa);
        aviso.ConsultaId.Should().Be("c-1");
        aviso.Attempt.Should().Be(1);
        (await R.EsperarAsync(async () => (await R.AvisosAsync(api, item.Id, ct))[0].Resultado == "APLICADO", TimeSpan.FromSeconds(5), ct)).Should().BeTrue();
    }

    [Fact]
    public async Task Un_duplicado_con_firma_y_timestamp_nuevos_responde_200_y_no_se_registra_dos_veces()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (item, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);
        var cuerpo = R.Aviso(R.Consulta("c-1"), "d-dup");

        using var primero = await api.Anonimo().SendAsync(R.PeticionAviso(token, cuerpo, R.Secreto, DateTimeOffset.UtcNow, "d-dup"), ct);
        using var reintento = await api.Anonimo().SendAsync(R.PeticionAviso(token, cuerpo, R.Secreto, DateTimeOffset.UtcNow.AddSeconds(60), "d-dup"), ct);

        primero.StatusCode.Should().Be(HttpStatusCode.OK);
        reintento.StatusCode.Should().Be(HttpStatusCode.OK, "un duplicado se confirma sin repetir");
        (await R.EsperarAsync(async () => (await R.ItemAsync(api, empresa, item.Id, ct)).Estado == EstadoItem.Exitoso, TimeSpan.FromSeconds(10), ct)).Should().BeTrue();
        (await R.AvisosAsync(api, item.Id, ct)).Should().ContainSingle();
    }

    [Fact]
    public async Task Una_compania_suspendida_recibe_200_sin_pasar_por_el_guard_y_el_resultado_se_aplica()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Suspendido, R.Secreto, ct, a => a.EstadoPrevio = EstadoAprovisionamiento.Listo);
        api.Productos.Apagar(empresa);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (item, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);

        using var r = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-1")), R.Secreto, DateTimeOffset.UtcNow), ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, "S6: lo que está en curso termina");
        api.Productos.ConsultasTotales.Should().Be(0, "el receptor no pasa por el guard de suscripción");
        (await R.EsperarAsync(async () => (await R.ItemAsync(api, empresa, item.Id, ct)).Estado == EstadoItem.Exitoso, TimeSpan.FromSeconds(10), ct)).Should().BeTrue();
    }

    [Fact]
    public async Task Un_cuerpo_de_mas_de_256_KB_responde_413_sin_cuerpo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (_, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);
        var grande = "{\"relleno\":\"" + new string('x', (256 * 1024) + 1) + "\"}";

        using var r = await api.Anonimo().SendAsync(R.PeticionAviso(token, grande, R.Secreto, DateTimeOffset.UtcNow), ct);

        r.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await r.Content.ReadAsStringAsync(ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Con_consulta_id_distinta_responde_200_y_descarta_el_aviso()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (item, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);

        using var r = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-otra")), R.Secreto, DateTimeOffset.UtcNow), ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await R.AvisosAsync(api, item.Id, ct)).Should().ContainSingle().Which.Resultado.Should().Be("CONSULTA_DISTINTA");
        await Task.Delay(300, ct);
        var despues = await R.ItemAsync(api, empresa, item.Id, ct);
        despues.Estado.Should().Be(EstadoItem.Enviado);
        despues.ConsultaApiId.Should().Be("c-1");
    }

    [Fact]
    public async Task Un_item_INCIERTO_adopta_el_consulta_id_del_aviso_valido()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (item, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Incierto, null, ct);

        using var r = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-adoptada")), R.Secreto, DateTimeOffset.UtcNow), ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await R.EsperarAsync(async () => (await R.ItemAsync(api, empresa, item.Id, ct)).Estado == EstadoItem.Exitoso, TimeSpan.FromSeconds(10), ct)).Should().BeTrue();
        (await R.ItemAsync(api, empresa, item.Id, ct)).ConsultaApiId.Should().Be("c-adoptada");
    }

    [Fact]
    public async Task Un_evento_que_no_es_consulta_finalizada_con_firma_valida_responde_200_y_no_cambia_nada()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (item, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);

        using var r = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-1")), R.Secreto, DateTimeOffset.UtcNow, evento: "consulta.otra_cosa"), ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK, "un 200 corta los reintentos del proveedor");
        await Task.Delay(300, ct);
        (await R.ItemAsync(api, empresa, item.Id, ct)).Estado.Should().Be(EstadoItem.Enviado);
        (await R.AvisosAsync(api, item.Id, ct)).Should().BeEmpty();
        (await R.AprovisionamientoAsync(api, empresa, ct)).FirmaConfirmada.Should().BeFalse("se ignora sin cambiar ningún estado");
    }

    [Fact]
    public async Task Avisos_desordenados_de_la_misma_consulta_dejan_el_de_mayor_attempt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (item, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);

        using var segundo = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-1", attempt: 2), "d-2"), R.Secreto, DateTimeOffset.UtcNow, "d-2"), ct);
        (await R.EsperarAsync(async () => (await R.ItemAsync(api, empresa, item.Id, ct)).Estado == EstadoItem.Exitoso, TimeSpan.FromSeconds(10), ct)).Should().BeTrue();
        using var primero = await api.Anonimo().SendAsync(
            R.PeticionAviso(token, R.Aviso(R.Consulta("c-1", status: "failed", attempt: 1, errorCode: "timeout", errorMessage: "x"), "d-1"), R.Secreto, DateTimeOffset.UtcNow, "d-1"), ct);

        primero.StatusCode.Should().Be(HttpStatusCode.OK);
        (await R.EsperarAsync(async () => (await R.AvisosAsync(api, item.Id, ct)).Any(a => a.DeliveryId == "d-1" && a.Resultado == "ATTEMPT_MENOR"), TimeSpan.FromSeconds(10), ct))
            .Should().BeTrue("el attempt menor se registra y no se aplica");
        var leido = await R.ItemAsync(api, empresa, item.Id, ct);
        leido.Estado.Should().Be(EstadoItem.Exitoso);
        leido.AttemptApiMax.Should().Be(2);
    }

    [Fact]
    public async Task La_respuesta_no_repite_el_secreto_ni_el_cuerpo()
    {
        var ct = TestContext.Current.CancellationToken;
        var logs = new Flit.Impuestos.Tests.ApiLiquidacion.RegistroDeLogs();
        await using var api = await R.ConApiAsync(logs: logs);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (item, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);

        using var malo = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-1")), "whsec_otro", DateTimeOffset.UtcNow), ct);
        using var bueno = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-1")), R.Secreto, DateTimeOffset.UtcNow), ct);
        await R.EsperarAsync(async () => (await R.ItemAsync(api, empresa, item.Id, ct)).Estado == EstadoItem.Exitoso, TimeSpan.FromSeconds(10), ct);

        logs.Todo.Should().NotContain(R.Secreto).And.NotContain(R.ApiKey).And.NotContain("PEDRO PEREZ").And.NotContain("LHQ479");
        Encoding.UTF8.GetByteCount(await malo.Content.ReadAsStringAsync(ct) + await bueno.Content.ReadAsStringAsync(ct)).Should().Be(0);
    }
}
