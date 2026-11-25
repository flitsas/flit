using System.Net;
using Flit.Impuestos.Api.Modulos.Conciliacion;
using Flit.Impuestos.Domain.Aprovisionamiento;
using Flit.Impuestos.Domain.Liquidaciones;
using Flit.Impuestos.Tests.ApiLiquidacion;
using Flit.Impuestos.Tests.Resultados;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using R = Flit.Impuestos.Tests.Resultados.EntornoResultados;

namespace Flit.Impuestos.Tests.Conciliacion;

/// <summary>
/// HU #14694 (M10; F2b-CF4, F2b-CF5, plan §2.2 y DA-6=c) — calendario del respaldo por <c>GET /api/v1/consultas/{id}</c>, sin
/// vencimiento: con webhook, primer GET a los 2 min, luego cada 5 min hasta 24 h y después cada 6 h; sin webhook
/// (<c>LISTO_SIN_WEBHOOK</c> o modo respaldo por firma), cada 10 s durante 10 min y luego el calendario anterior. Todos los valores
/// salen de <c>Impuestos:Conciliacion:*</c>. La ronda del trabajador aplica el resultado final y reprograma lo que sigue en curso;
/// no consulta ítems de un lote cerrado.
/// </summary>
/// <example>
/// <code>
/// CalendarioConciliacion.Siguiente(enviadoEn, ahora, sinWebhook: false, new OpcionesConciliacion()); // ahora + 5 min
/// </code>
/// </example>
public sealed class CalendarioConciliacionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 25, 15, 0, 0, TimeSpan.Zero);
    private static readonly OpcionesConciliacion Defecto = new();

    [Fact]
    public void Con_webhook_el_primer_GET_es_a_los_2_minutos_luego_cada_5_hasta_24_h_y_despues_cada_6_h()
    {
        CalendarioConciliacion.Primero(T0, sinWebhook: false, Defecto).Should().Be(T0.AddMinutes(2));
        CalendarioConciliacion.Siguiente(T0, T0.AddSeconds(30), false, Defecto).Should().Be(T0.AddMinutes(2));
        CalendarioConciliacion.Siguiente(T0, T0.AddMinutes(3), false, Defecto).Should().Be(T0.AddMinutes(8));
        CalendarioConciliacion.Siguiente(T0, T0.AddHours(23), false, Defecto).Should().Be(T0.AddHours(23).AddMinutes(5));
        CalendarioConciliacion.Siguiente(T0, T0.AddHours(25), false, Defecto).Should().Be(T0.AddHours(31));
    }

    [Fact]
    public void Sin_webhook_cada_10_segundos_durante_10_minutos_y_luego_el_calendario_con_webhook()
    {
        CalendarioConciliacion.Primero(T0, sinWebhook: true, Defecto).Should().Be(T0.AddSeconds(10));
        CalendarioConciliacion.Siguiente(T0, T0.AddSeconds(5), true, Defecto).Should().Be(T0.AddSeconds(15));
        CalendarioConciliacion.Siguiente(T0, T0.AddMinutes(9), true, Defecto).Should().Be(T0.AddMinutes(9).AddSeconds(10));
        CalendarioConciliacion.Siguiente(T0, T0.AddMinutes(11), true, Defecto).Should().Be(T0.AddMinutes(16));
        CalendarioConciliacion.Siguiente(T0, T0.AddHours(30), true, Defecto).Should().Be(T0.AddHours(36));
    }

    [Fact]
    public void No_vence_nunca_mientras_el_item_no_tenga_estado_final()
    {
        var ahora = T0.AddDays(90);
        CalendarioConciliacion.Siguiente(T0, ahora, false, Defecto).Should().Be(ahora.AddHours(6), "DA-6=c: sin vencimiento");
        CalendarioConciliacion.Siguiente(T0, ahora, true, Defecto).Should().Be(ahora.AddHours(6));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void Sin_webhook_es_sin_callbackUrl_o_con_el_modo_respaldo_de_firma(bool conCallback, bool modoRespaldo, bool esperado) =>
        CalendarioConciliacion.SinWebhook(conCallback, modoRespaldo).Should().Be(esperado);

    [Fact]
    public void Los_valores_salen_de_la_configuracion_y_un_valor_no_positivo_no_arranca()
    {
        var o = new OpcionesConciliacion { PrimerGetConWebhook = TimeSpan.FromMinutes(1), IntervaloSinWebhook = TimeSpan.FromSeconds(3) };
        CalendarioConciliacion.Primero(T0, false, o).Should().Be(T0.AddMinutes(1));
        CalendarioConciliacion.Primero(T0, true, o).Should().Be(T0.AddSeconds(3));

        var invalida = () => new OpcionesConciliacion { IntervaloConWebhook = TimeSpan.Zero }.Validar();
        invalida.Should().Throw<InvalidOperationException>().WithMessage("*Impuestos:Conciliacion*");
    }

    [Fact]
    public async Task La_ronda_aplica_el_resultado_final_reprograma_lo_que_sigue_en_curso_y_no_consulta_un_lote_cerrado()
    {
        var ct = TestContext.Current.CancellationToken;
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        var proveedor = new ApiLiquidacionDePrueba
        {
            Responder = r => r.RequestUri!.AbsolutePath switch
            {
                "/api/v1/consultas/c-final" => ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, R.Consulta("c-final")),
                "/api/v1/consultas/c-curso" => ApiLiquidacionDePrueba.Json(HttpStatusCode.OK, R.ConsultaEnCurso("c-curso")),
                _ => ApiLiquidacionDePrueba.Json(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"not_found\",\"message\":\"x\"}}"),
            },
        };
        await using var api = await R.ConApiAsync(proveedor, reloj);
        Assert.SkipWhen(api.Omitir is not null, api.Omitir ?? string.Empty);
        var empresa = await R.SembrarEmpresaAsync(api, EstadoAprovisionamiento.ListoSinWebhook, null, ct);
        var lote = await R.SembrarLoteAsync(api, empresa, EstadoLote.EnProceso, 2, ct);
        var enviado = reloj.Ahora.AddMinutes(-1);
        var final = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => (i.ConsultaApiId, i.EnviadoEn, i.ProximoPollEn) = ("c-final", enviado, reloj.Ahora));
        var curso = await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => (i.ConsultaApiId, i.EnviadoEn, i.ProximoPollEn) = ("c-curso", enviado, reloj.Ahora));
        var cerrado = await R.SembrarLoteAsync(api, empresa, EstadoLote.CerradoManualmente, 1, ct);
        await R.SembrarItemAsync(api, empresa, cerrado, EstadoItem.Enviado, ct, i => (i.ConsultaApiId, i.EnviadoEn, i.ProximoPollEn) = ("c-cerrado", enviado, reloj.Ahora));
        await R.SembrarItemAsync(api, empresa, lote, EstadoItem.Enviado, ct, i => (i.ConsultaApiId, i.ProximoPollEn) = ("c-futuro", reloj.Ahora.AddMinutes(1)));

        await api.App.Services.GetRequiredService<TrabajadorConciliacion>().EjecutarRondaAsync(ct);

        (await R.ItemAsync(api, empresa, final.Id, ct)).Estado.Should().Be(EstadoItem.Exitoso);
        var siguiente = await R.ItemAsync(api, empresa, curso.Id, ct);
        siguiente.Estado.Should().Be(EstadoItem.Enviado);
        siguiente.ProximoPollEn.Should().Be(reloj.Ahora.AddSeconds(10), "sin webhook: cada 10 s los primeros 10 min");
        proveedor.Pedidos.Select(p => new Uri(p.Uri).AbsolutePath).Should().BeEquivalentTo(["/api/v1/consultas/c-final", "/api/v1/consultas/c-curso"]);
        proveedor.Pedidos.Should().OnlyContain(p => p.Cabeceras["X-API-Key"] == R.ApiKey);
    }
}
