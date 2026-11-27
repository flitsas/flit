using System.Net;
using Flit.Impuestos.Api.Modulos.Resultados;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Platform.Sdk.Messaging;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Resultados;

/// <summary>
/// HU #14694 (M10; F2b-CF2, F2b-CF6, F2b-CF7, F1a-CF11, plan §3.2–§3.3) — aplicar un resultado final al ítem y al lote: null no es
/// 0; manda el mayor <c>attempt</c>; los <c>warnings</c> se guardan aparte y las <c>validations</c> (que pueden traer PII) solo en
/// el cuerpo cifrado; la clase de error sale de la tabla §9; un resultado tardío no reabre un lote cerrado; el lote termina y
/// recalcula sus contadores; un éxito con archivos deja el evento <c>impuestos.recibos.descargar</c> en la outbox; el primer
/// webhook válido confirma la firma.
/// </summary>
/// <example>
/// <code>
/// await EntornoResultados.AplicarAsync(api, empresa, item.Id, EntornoResultados.Consulta("c-1", attempt: 2), ct);
/// </code>
/// </example>
public sealed class AplicadorResultadoTests
{
    [Fact]
    public async Task Null_no_es_cero_en_el_total_ni_en_las_vigencias()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 2, ct);
        var sinDato = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => i.ConsultaApiId = "c-1");
        var pazYSalvo = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => i.ConsultaApiId = "c-2");

        (await R.AplicarAsync(api, empresa, sinDato.Id, R.Consulta("c-1", totalToPay: "null", periodTotal: "null"), ct)).Should().Be(Aplicacion.Aplicado);
        (await R.AplicarAsync(api, empresa, pazYSalvo.Id, R.Consulta("c-2", resultStatus: "paz_y_salvo", totalToPay: "0"), ct)).Should().Be(Aplicacion.Aplicado);

        var a = await R.ItemAsync(api, empresa, sinDato.Id, ct);
        a.Estado.Should().Be(EstadoItem.Exitoso);
        a.TotalAPagar.Should().BeNull("el proveedor no dio el dato: «Sin dato», nunca 0");
        a.ResultadoStatus.Should().Be("con_deuda");
        var periodo = (await PeriodosAsync(api, empresa, sinDato.Id, ct)).Should().ContainSingle().Subject;
        periodo.Periodo.Should().Be("2025");
        periodo.TotalAPagar.Should().BeNull();
        periodo.PagosPrevios.Should().BeNull();
        periodo.Sancion.Should().Be(0m, "un 0 del proveedor es 0");
        periodo.Impuesto.Should().Be(710000m);
        periodo.Vencimiento.Should().Be(new DateOnly(2026, 11, 30));
        periodo.FileIdApi.Should().Be("f1a2");
        periodo.OtrosCargos.Should().Contain("Costos administrativos").And.Contain("36700");

        var b = await R.ItemAsync(api, empresa, pazYSalvo.Id, ct);
        b.TotalAPagar.Should().Be(0m);
        b.ResultadoStatus.Should().Be("paz_y_salvo");
        (await PeriodosAsync(api, empresa, pazYSalvo.Id, ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Manda_el_mayor_attempt_aunque_llegue_primero()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 2, ct);
        var desordenado = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => i.ConsultaApiId = "c-1");
        var mejorado = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => i.ConsultaApiId = "c-2");

        // webhook-desordenado: attempt=2 antes que attempt=1.
        (await R.AplicarAsync(api, empresa, desordenado.Id, R.Consulta("c-1", attempt: 2), ct)).Should().Be(Aplicacion.Aplicado);
        (await R.AplicarAsync(api, empresa, desordenado.Id, R.Consulta("c-1", status: "failed", attempt: 1, errorCode: "timeout", errorMessage: "x"), ct))
            .Should().Be(Aplicacion.AttemptMenor);
        var a = await R.ItemAsync(api, empresa, desordenado.Id, ct);
        a.Estado.Should().Be(EstadoItem.Exitoso);
        a.AttemptApiMax.Should().Be(2);

        // El proveedor reintenta un parcial y el attempt 2 lo reemplaza.
        await R.AplicarAsync(api, empresa, mejorado.Id, R.Consulta("c-2", status: "partial", attempt: 1, warnings: "[\"pdf_download_failed\"]"), ct);
        (await R.ItemAsync(api, empresa, mejorado.Id, ct)).Parcial.Should().BeTrue();
        (await R.AplicarAsync(api, empresa, mejorado.Id, R.Consulta("c-2", attempt: 1), ct)).Should().Be(Aplicacion.SinCambio, "mismo attempt ya aplicado");
        (await R.AplicarAsync(api, empresa, mejorado.Id, R.Consulta("c-2", attempt: 2), ct)).Should().Be(Aplicacion.Aplicado);
        var b = await R.ItemAsync(api, empresa, mejorado.Id, ct);
        b.Parcial.Should().BeFalse();
        b.Warnings.Should().BeEmpty();
        b.AttemptApiMax.Should().Be(2);
        (await PeriodosAsync(api, empresa, mejorado.Id, ct)).Should().ContainSingle("las vigencias se reemplazan, no se duplican");
    }

    [Fact]
    public async Task Los_warnings_se_guardan_y_las_validations_solo_van_en_el_cuerpo_cifrado()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var item = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => i.ConsultaApiId = "c-1");

        await R.AplicarAsync(api, empresa, item.Id, R.Consulta("c-1", status: "partial", warnings: "[\"pdf_download_failed\",{\"code\":\"period_not_liquidable\",\"period\":2024}]"), ct);

        var leido = await R.ItemAsync(api, empresa, item.Id, ct);
        leido.Parcial.Should().BeTrue();
        leido.Warnings.Should().Equal("pdf_download_failed", "period_not_liquidable");
        leido.ResultadoCifrado.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(leido.ResultadoCifrado!).Should().NotContain("PROPIETARIO PRUEBA", "el cuerpo va cifrado");
        using var scope = api.App.Services.CreateScope();
        var claro = scope.ServiceProvider.GetRequiredService<IProteccionImpuestos>().DesprotegerResultado(leido.ResultadoCifrado!);
        claro.Should().Contain("\"validations\"").And.Contain("OwnerName").And.Contain("PROPIETARIO PRUEBA");
        new[] { leido.ErrorCodigo, leido.ErrorMensaje, leido.ResultadoStatus, leido.ConsultaStatus, leido.DepartamentoNombre, string.Join(",", leido.Warnings) }
            .Should().NotContain(v => v != null && v.Contains("PROPIETARIO PRUEBA", StringComparison.Ordinal), "ninguna columna en claro lleva datos del propietario");
    }

    [Fact]
    public async Task Un_partial_conserva_los_montos_y_registra_los_errores_de_archivo()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var item = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => i.ConsultaApiId = "c-1");

        await R.AplicarAsync(api, empresa, item.Id, R.Consulta("c-1", status: "partial", fileId: "null",
            fileErrors: "[{\"file_id\":\"f1a2\",\"code\":\"pdf_download_failed\",\"message\":\"x\"}]"), ct);

        var leido = await R.ItemAsync(api, empresa, item.Id, ct);
        leido.Estado.Should().Be(EstadoItem.Exitoso);
        leido.Parcial.Should().BeTrue();
        leido.TotalAPagar.Should().Be(771700m, "los montos están aunque falte el PDF");
        leido.Warnings.Should().Contain("pdf_download_failed", "el error de archivo queda registrado");
        (await PeriodosAsync(api, empresa, item.Id, ct)).Should().ContainSingle().Which.TotalAPagar.Should().Be(771700m);
    }

    [Theory]
    [InlineData("owner_validation_failed", EstadoItem.Error, ClaseError.CorregirDatos)]
    [InlineData("portal_rejected", EstadoItem.Error, ClaseError.Depende)]
    [InlineData("captcha_blocked", EstadoItem.Error, ClaseError.Reintentable)]
    [InlineData("codigo_que_no_existe", EstadoItem.Error, ClaseError.Depende)]
    public async Task Un_fallo_queda_en_ERROR_con_la_clase_de_la_tabla_9_y_el_mensaje_del_proveedor(string codigo, EstadoItem estado, ClaseError clase)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var item = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => i.ConsultaApiId = "c-1");

        await R.AplicarAsync(api, empresa, item.Id, R.Consulta("c-1", status: "failed", errorCode: codigo, errorMessage: "El portal dijo que no."), ct);

        var leido = await R.ItemAsync(api, empresa, item.Id, ct);
        leido.Estado.Should().Be(estado);
        leido.ErrorClase.Should().Be(clase);
        leido.ErrorCodigo.Should().Be(codigo);
        leido.ErrorMensaje.Should().Be("El portal dijo que no.");
        leido.TotalAPagar.Should().BeNull();
        leido.ProximoPollEn.Should().BeNull("un estado final no se vuelve a consultar");
        var cabecera = await R.LoteAsync(api, empresa, lote, ct);
        cabecera.Estado.Should().Be(EstadoLote.CompletadoConNovedades);
        cabecera.CError.Should().Be(1);
        cabecera.CPendientes.Should().Be(0);
    }

    [Fact]
    public async Task Rate_limited_reprograma_una_consulta_nueva_con_token_nuevo_hasta_el_tercer_intento()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        await using var api = await R.ConApiAsync(reloj: reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 2, ct);
        var (primero, _) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);
        var (tercero, _) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-3", ct, i => i.IntentosReenvio = 2);

        await R.AplicarAsync(api, empresa, primero.Id, R.Consulta("c-1", status: "failed", errorCode: "rate_limited", errorMessage: "x"), ct);
        await R.AplicarAsync(api, empresa, tercero.Id, R.Consulta("c-3", status: "failed", errorCode: "rate_limited", errorMessage: "x"), ct);

        var a = await R.ItemAsync(api, empresa, primero.Id, ct);
        a.Estado.Should().Be(EstadoItem.ReintentoProgramado);
        a.IntentosReenvio.Should().Be(1);
        a.ProximoIntentoEn.Should().Be(reloj.Ahora.AddSeconds(30), "DA-5: 30 s, 2 min y 10 min");
        a.TokenHash.Should().BeNull("la consulta nueva lleva un token nuevo");
        a.ConsultaApiId.Should().BeNull();
        var c = await R.ItemAsync(api, empresa, tercero.Id, ct);
        c.Estado.Should().Be(EstadoItem.Error);
        c.ErrorClase.Should().Be(ClaseError.Reintentable);
        c.IntentosReenvio.Should().Be(3);
    }

    [Fact]
    public async Task Un_resultado_tardio_se_aplica_sin_reabrir_el_lote_cerrado_y_recalcula_los_contadores()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.CerradoManualmente, 1, ct);
        var item = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.SinResultadoLoteCerrado, ct, i => i.ConsultaApiId = "c-1");

        (await R.AplicarAsync(api, empresa, item.Id, R.Consulta("c-1"), ct)).Should().Be(Aplicacion.Aplicado);

        var leido = await R.ItemAsync(api, empresa, item.Id, ct);
        leido.Estado.Should().Be(EstadoItem.Exitoso);
        leido.ResultadoTardio.Should().BeTrue();
        var cabecera = await R.LoteAsync(api, empresa, lote, ct);
        cabecera.Estado.Should().Be(EstadoLote.CerradoManualmente, "el lote no se reabre");
        cabecera.CExitosas.Should().Be(1);
        cabecera.CConDeuda.Should().Be(1);
        cabecera.CSinResultado.Should().Be(0);
        cabecera.CPendientes.Should().Be(0);
    }

    [Fact]
    public async Task El_lote_termina_cuando_su_ultimo_item_llega_a_estado_final()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 2, ct);
        var uno = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => i.ConsultaApiId = "c-1");
        var dos = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => i.ConsultaApiId = "c-2");

        await R.AplicarAsync(api, empresa, uno.Id, R.Consulta("c-1"), ct);
        var aMedias = await R.LoteAsync(api, empresa, lote, ct);
        aMedias.Estado.Should().Be(EstadoLote.EnProceso);
        aMedias.CPendientes.Should().Be(1);
        aMedias.TerminadaEn.Should().BeNull();

        await R.AplicarAsync(api, empresa, dos.Id, R.Consulta("c-2", resultStatus: "paz_y_salvo", totalToPay: "0"), ct);

        var cabecera = await R.LoteAsync(api, empresa, lote, ct);
        cabecera.Estado.Should().Be(EstadoLote.Completado);
        cabecera.CExitosas.Should().Be(2);
        cabecera.CConDeuda.Should().Be(1);
        cabecera.CPazYSalvo.Should().Be(1);
        cabecera.CPendientes.Should().Be(0);
        cabecera.TerminadaEn.Should().NotBeNull();
    }

    [Fact]
    public async Task Un_exito_con_archivos_deja_el_evento_de_recibos_en_la_outbox_y_uno_sin_archivos_no()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 2, ct);
        var conArchivo = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => i.ConsultaApiId = "c-1");
        var sinArchivo = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => i.ConsultaApiId = "c-2");

        await R.AplicarAsync(api, empresa, conArchivo.Id, R.Consulta("c-1"), ct);
        await R.AplicarAsync(api, empresa, sinArchivo.Id, R.Consulta("c-2", fileId: "null"), ct);

        await using var db = api.Contexto(empresa);
        var eventos = await db.Set<OutboxMessage>().AsNoTracking().Where(m => m.RoutingKey == AplicadorResultado.TipoRecibos).ToListAsync(ct);
        eventos.Should().ContainSingle().Which.Payload.Should().Contain(conArchivo.Id.ToString()).And.NotContain("LHQ479");
    }

    [Fact]
    public async Task El_primer_webhook_valido_confirma_la_firma_y_sube_la_version()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await R.ConApiAsync();
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.Listo, R.Secreto, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 1, ct);
        var (_, token) = await R.SembrarItemConTokenAsync(api, empresa, lote, EstadoItem.Enviado, "c-1", ct);
        var antes = await R.AprovisionamientoAsync(api, empresa, ct);

        using var r = await api.Anonimo().SendAsync(R.PeticionAviso(token, R.Aviso(R.Consulta("c-1")), R.Secreto, DateTimeOffset.UtcNow), ct);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var despues = await R.AprovisionamientoAsync(api, empresa, ct);
        antes.FirmaConfirmada.Should().BeFalse();
        despues.FirmaConfirmada.Should().BeTrue("F1a-CF11: deja de mostrarse «Firma sin confirmar»");
        despues.Version.Should().Be(antes.Version + 1, "el reportador de M07 la lleva a la plataforma");
        despues.Estado.Should().Be(EstadoAprovisionamiento.Listo);
        await using var db = api.Contexto(empresa);
        (await db.Set<HistorialAprovisionamiento>().AsNoTracking().ToListAsync(ct)).Should().ContainSingle()
            .Which.MotivoCodigo.Should().Be(Flit.Impuestos.Api.Modulos.Avisos.ProcesadorAviso.MotivoFirmaConfirmada);
    }

    private static async Task<List<LiquidacionPeriodo>> PeriodosAsync(Flit.Impuestos.Tests.Api.ApiDePrueba api, Guid empresa, Guid item, CancellationToken ct)
    {
        await using var db = api.Contexto(empresa);
        return await db.Set<LiquidacionPeriodo>().AsNoTracking().Where(p => p.ItemId == item).ToListAsync(ct);
    }
}
